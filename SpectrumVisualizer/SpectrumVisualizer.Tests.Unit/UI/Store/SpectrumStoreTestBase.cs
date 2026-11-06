using System.Reactive.Concurrency;
using System.Threading.Channels;

using AwesomeAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Tests.Unit.Core.Store;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Store;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Store;

/// <summary>A real store over a real distributor and file in a fresh temp folder.</summary>
public abstract class SpectrumStoreTestBase : IAsyncLifetime
{
	protected const int TestTimeoutMs = 30_000;
	protected const byte FailingSeq = 99;
	protected const int QueueCapacity = 1024;
	protected const int BatchSize = 256;

	private readonly FakeLogger<FrameDistributor> _distributorLogger = new();
	private FrameDistributor? _distributor;
	private SpectrumStore? _store;
	private int _delivered;

	protected SpectrumStoreTestBase()
	{
		StorePath = Folder.FileIn("spectra-1.db");
	}

	/// <summary>
	/// A held write lock costs a batch the 2 s busy timeout, 1 s once the stop began; the checkpoint never waits.
	/// </summary>
	protected static TimeSpan StopLimit { get; } = TimeSpan.FromSeconds(10);

	/// <summary>A copy of App.axaml.cs _backgroundStopLimit, the limit of the app's close chain.</summary>
	protected static TimeSpan CloseLimit { get; } = TimeSpan.FromSeconds(5);

	/// <summary>A copy of App.axaml.cs _storeCloseReserve: the drain token fires this long before CloseLimit.</summary>
	protected static TimeSpan CloseReserve { get; } = TimeSpan.FromSeconds(1.5);

	/// <summary>
	/// A frame the store took would reach the file within this long; nothing waits on it in the passing case.
	/// </summary>
	protected static TimeSpan NoWriteWait { get; } = TimeSpan.FromMilliseconds(200);

	private protected TempFolder Folder { get; } = new();

	protected MessagePanelViewModel Panel { get; } = new(TimeProvider.System);

	protected FakeLogger<SpectrumStore> Logger { get; } = new();

	protected string StorePath { get; }

	protected Channel<ReceivedFrame> LinkFrames { get; set; } = LinkChannel();

	protected FrameDistributor Distributor =>
		_distributor ?? throw new InvalidOperationException("No store was created.");

	protected SpectrumStore Store => _store ?? throw new InvalidOperationException("No store was created.");

	public ValueTask InitializeAsync()
	{
		return ValueTask.CompletedTask;
	}

	public async ValueTask DisposeAsync()
	{
		if (_distributor is not null)
		{
			await _distributor.StopAsync().WaitAsync(Waits.Limit);
			_distributor.Dispose();
		}

		if (_store is not null)
		{
			await _store.StopAsync().WaitAsync(StopLimit);
			_store.Dispose();
		}

		Panel.Dispose();
		Folder.Dispose();
		GC.SuppressFinalize(this);
	}

	protected static Channel<ReceivedFrame> LinkChannel()
	{
		return Channel.CreateBounded<ReceivedFrame>(new BoundedChannelOptions(64)
		{
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleWriter = true,
			SingleReader = true
		});
	}

	protected void CreateStore(TimeProvider? clock = null, string? path = null)
	{
		_distributor = new FrameDistributor(LinkFrames.Reader, Panel, ImmediateScheduler.Instance, _distributorLogger);
		_store = new SpectrumStore(
			_distributor,
			new StoreFile(path ?? StorePath),
			Panel,
			ImmediateScheduler.Instance,
			clock ?? TimeProvider.System,
			Logger);
	}

	/// <summary>Starts the store, then counts what the distributor delivers after the store's own subscriber.</summary>
	protected void StartStore()
	{
		Store.Start();
		Distributor.Frames.Subscribe(_ => Interlocked.Increment(ref _delivered));
		Distributor.Start();
	}

	protected async Task StartOpenedAsync(TimeProvider? clock = null)
	{
		CreateStore(clock);
		StartStore();
		await OpenedAsync();
	}

	protected Task OpenedAsync()
	{
		return Waits.UntilAsync(
			() => Logger.Collector.GetSnapshot().Any(record => record.Level == LogLevel.Information));
	}

	protected Task StopAsync()
	{
		return Store.StopAsync().WaitAsync(StopLimit);
	}

	protected bool StoppedLogged()
	{
		return LatestStoppedLine() is not null;
	}

	protected string? LatestStoppedLine()
	{
		return Logger.Collector.GetSnapshot()
			.Select(record => record.Message)
			.LastOrDefault(message => message.StartsWith("The spectrum store stopped", StringComparison.Ordinal));
	}

	protected List<DateTimeOffset> LogTimes(string messageStart)
	{
		return [.. Logger.Collector.GetSnapshot()
			.Where(record => record.Message.StartsWith(messageStart, StringComparison.Ordinal))
			.Select(record => record.Timestamp)];
	}

	/// <summary>
	/// The log times of failed batches: the reported first one of a run and the repeats the store logs itself.
	/// </summary>
	protected List<DateTimeOffset> BatchFailureTimes()
	{
		return [.. BatchFailureRecords().Select(record => record.Timestamp)];
	}

	/// <summary>The log lines of failed batches: the reported first one of a run and the repeats.</summary>
	protected List<FakeLogRecord> BatchFailureRecords()
	{
		return [.. Logger.Collector.GetSnapshot()
			.Where(record => record.Message.StartsWith(Resources.StoreWriteTitle, StringComparison.Ordinal)
				|| record.Message.StartsWith("A batch of", StringComparison.Ordinal))];
	}

	protected void Feed(params ReceivedFrame[] frames)
	{
		foreach (var frame in frames)
		{
			LinkFrames.Writer.TryWrite(frame).Should().BeTrue();
		}
	}

	/// <summary>Feeds the frames and waits until the store's subscriber has taken every one.</summary>
	protected Task FeedAsync(params ReceivedFrame[] frames)
	{
		var expected = Volatile.Read(ref _delivered) + frames.Length;
		Feed(frames);
		return Waits.UntilAsync(() => Volatile.Read(ref _delivered) == expected);
	}

	protected async Task FailOneBatchAsync(long expectedDropped)
	{
		Feed(StoreSamples.Spectrum(StoreSamples.Start, seq: FailingSeq));
		await Waits.UntilAsync(() => Store.DroppedFrames == expectedDropped);
	}

	protected void FailWritesOfSeq99()
	{
		using var connection = OpenPlain();
		using var command = connection.CreateCommand();
		command.CommandText = $"""
			CREATE TRIGGER fail_seq_{FailingSeq} BEFORE INSERT ON spectrum WHEN NEW.seq = {FailingSeq}
			BEGIN SELECT RAISE(ABORT, 'test'); END
			""";
		command.ExecuteNonQuery();
	}

	/// <summary>
	/// Advances the clock past the next retention time, then feeds one spectrum and waits until the file holds
	/// expectedSpectra: the writer runs a due pass before the next batch, so the pass has ended once the row landed.
	/// </summary>
	protected async Task RetentionPassAsync(RetentionClock clock, TimeSpan advance, long expectedSpectra)
	{
		clock.Advance(advance);
		Feed(StoreSamples.Spectrum(StoreSamples.Start, seq: 1));
		await Waits.UntilAsync(() => Count("spectrum") == expectedSpectra);
	}

	/// <summary>
	/// A second connection inside BEGIN IMMEDIATE: the writer's batches wait out the busy timeout and fail.
	/// </summary>
	protected SqliteConnection HoldWriteLock()
	{
		var holder = OpenPlain();
		using var command = holder.CreateCommand();
		command.CommandText = "BEGIN IMMEDIATE";
		command.ExecuteNonQuery();
		return holder;
	}

	protected long Count(string table)
	{
		return (long)Scalar($"SELECT count(*) FROM {table}")!;
	}

	protected object? Scalar(string sql)
	{
		using var connection = OpenPlain();
		return Scalar(connection, sql);
	}

	protected static object? Scalar(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return command.ExecuteScalar();
	}

	protected SqliteConnection OpenPlain()
	{
		var connection = new SqliteConnection(
			new SqliteConnectionStringBuilder { DataSource = StorePath, Pooling = false }.ToString());
		connection.Open();
		return connection;
	}

	/// <summary>
	/// A FakeTimeProvider that counts its GetUtcNow calls and throws NowFailure from them once it is set.
	/// </summary>
	protected sealed class RetentionClock(DateTimeOffset start) : FakeTimeProvider(start)
	{
		private int _reads;

		public Exception? NowFailure { get; set; }

		/// <summary>
		/// The GetUtcNow calls so far; the store reads the clock only right before a retention delete.
		/// </summary>
		public int Reads => Volatile.Read(ref _reads);

		public override DateTimeOffset GetUtcNow()
		{
			Interlocked.Increment(ref _reads);
			return NowFailure is { } failure ? throw failure : base.GetUtcNow();
		}
	}
}
