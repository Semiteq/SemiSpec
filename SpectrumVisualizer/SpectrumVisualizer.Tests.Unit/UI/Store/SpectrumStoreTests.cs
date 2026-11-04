using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Threading.Channels;

using AwesomeAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Tests.Unit.Core.Store;
using SpectrumVisualizer.Tests.Unit.UI.Messages;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Store;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Store;

[Trait("Component", "UI")]
[Trait("Area", "Store")]
[Trait("Category", "Unit")]
public sealed class SpectrumStoreTests : IAsyncLifetime
{
	private const int TestTimeoutMs = 30_000;
	private const byte FailingSeq = 99;

	// A held write lock costs a batch 2 s and the closing checkpoint up to 2 s more.
	private static readonly TimeSpan _stopLimit = TimeSpan.FromSeconds(10);

	// A frame the store took would reach the file within this long; nothing waits on it in the passing case.
	private static readonly TimeSpan _noWriteWait = TimeSpan.FromMilliseconds(200);

	private readonly TempFolder _folder = new();
	private readonly MessagePanelViewModel _panel = new(TimeProvider.System);
	private readonly FakeLogger<FrameDistributor> _distributorLogger = new();
	private readonly FakeLogger<SpectrumStore> _logger = new();
	private readonly string _path;
	private Channel<ReceivedFrame> _channel = LinkChannel();
	private FrameDistributor? _distributor;
	private SpectrumStore? _store;
	private int _delivered;

	public SpectrumStoreTests()
	{
		_path = _folder.FileIn("spectra-1.db");
	}

	private FrameDistributor Distributor => _distributor ?? throw new InvalidOperationException("No store was created.");

	private SpectrumStore Store => _store ?? throw new InvalidOperationException("No store was created.");

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
			await _store.StopAsync().WaitAsync(_stopLimit);
			_store.Dispose();
		}

		_panel.Dispose();
		_folder.Dispose();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task EveryFrameFromTheDistributorIsStored()
	{
		await StartOpenedAsync();

		await FeedAsync(
			StoreSamples.Spectrum(StoreSamples.Start, seq: 1),
			StoreSamples.Heartbeat(StoreSamples.Start.AddSeconds(1), seq: 2),
			StoreSamples.Spectrum(StoreSamples.Start.AddSeconds(2), Spectrometer.Atp2000, seq: 3),
			StoreSamples.Heartbeat(StoreSamples.Start.AddSeconds(3), seq: 4));
		await StopAsync();

		Scalar("SELECT group_concat(seq) FROM (SELECT seq FROM spectrum ORDER BY id)").Should().Be("1,3");
		Scalar("SELECT group_concat(seq) FROM (SELECT seq FROM heartbeat ORDER BY id)").Should().Be("2,4");
		Store.DroppedFrames.Should().Be(0);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task TheSubscriberOnlyEnqueues()
	{
		CreateStore();
		var beforeStore = new ConcurrentQueue<long>();
		var afterStore = new ConcurrentQueue<long>();
		using var before = Distributor.Frames.Subscribe(_ => beforeStore.Enqueue(Stopwatch.GetTimestamp()));
		Store.Start();
		using var after = Distributor.Frames.Subscribe(_ => afterStore.Enqueue(Stopwatch.GetTimestamp()));
		Distributor.Start();
		await OpenedAsync();
		using var holder = HoldWriteLock();

		Feed([.. Enumerable.Range(0, 10).Select(i => StoreSamples.Spectrum(StoreSamples.Start.AddSeconds(i)))]);

		await Waits.UntilAsync(() => afterStore.Count == 10);
		var slowest = beforeStore.Zip(afterStore, Stopwatch.GetElapsedTime).Max();
		slowest.Should().BeLessThan(TimeSpan.FromMilliseconds(50));
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AFullQueueCountsEachDroppedFrame()
	{
		const int Fed = 1500;
		_channel = Channel.CreateUnbounded<ReceivedFrame>();
		await StartOpenedAsync();
		var signals = 0;
		using var signalled = Store.DroppedFramesChanged.Subscribe(_ => Interlocked.Increment(ref signals));
		var frame = StoreSamples.Spectrum(StoreSamples.Start);

		using (HoldWriteLock())
		{
			await FeedAsync([.. Enumerable.Repeat(frame, Fed)]);
		}

		await StopAsync();

		(Count("spectrum") + Store.DroppedFrames).Should().Be(Fed);
		Store.DroppedFrames.Should().BePositive();
		Volatile.Read(ref signals).Should().BePositive();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AFailedBatchCountsItsFramesAndReportsOnce()
	{
		await StartOpenedAsync();
		FailWritesOfSeq99();

		await FailOneBatchAsync(expectedDropped: 1);
		await FailOneBatchAsync(expectedDropped: 2);
		await StopAsync();

		var entry = _panel.Entries.Should().ContainSingle().Subject;
		entry.View.Title.Should().Be(Resources.StoreWriteTitle);
		entry.View.Severity.Should().Be(MessageSeverity.Warning);
		entry.RepeatCount.Should().Be(1);
		_logger.Collector.GetSnapshot().Count(record => record.Level == LogLevel.Error).Should().Be(2);
		_logger.Failures().Should().AllBeOfType<SqliteException>();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AWriteAfterAFailureRunReportsAgainOnlyAfterASuccess()
	{
		await StartOpenedAsync();
		FailWritesOfSeq99();

		await FailOneBatchAsync(expectedDropped: 1);
		await FailOneBatchAsync(expectedDropped: 2);
		Feed(StoreSamples.Spectrum(StoreSamples.Start, seq: 1));
		await Waits.UntilAsync(() => Count("spectrum") == 1);
		await FailOneBatchAsync(expectedDropped: 3);
		await StopAsync();

		var entry = _panel.Entries.Should().ContainSingle().Subject;
		entry.View.Title.Should().Be(Resources.StoreWriteTitle);
		entry.RepeatCount.Should().Be(2, "the second report coalesces into the first entry");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task RetentionRunsOnceAMinuteAlsoWithoutFrames()
	{
		var now = StoreSamples.Start.AddHours(25);
		var clock = new ManualClock(now);
		await StartOpenedAsync(clock);
		Feed(
			StoreSamples.Spectrum(StoreSamples.Start, seq: 1),
			StoreSamples.Heartbeat(StoreSamples.Start, seq: 2),
			StoreSamples.Spectrum(now, seq: 3));
		await Waits.UntilAsync(() => Count("spectrum") == 2 && Count("heartbeat") == 1);

		clock.Advance(TimeSpan.FromSeconds(61));

		await Waits.UntilAsync(() => Count("spectrum") == 1 && Count("heartbeat") == 0);
		Scalar("SELECT seq FROM spectrum").Should().Be(3L);
		Feed(StoreSamples.Spectrum(StoreSamples.Start, seq: 4));
		await Waits.UntilAsync(() => Count("spectrum") == 2);

		clock.Advance(TimeSpan.FromSeconds(61));

		await Waits.UntilAsync(() => Count("spectrum") == 1);
		Scalar("SELECT seq FROM spectrum").Should().Be(3L);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AStopPastItsBudgetCountsTheUndrainedFramesAsDropped()
	{
		const int Fed = 600;
		_channel = Channel.CreateUnbounded<ReceivedFrame>();
		await StartOpenedAsync();
		var frame = StoreSamples.Spectrum(StoreSamples.Start);
		Task stop;

		using (HoldWriteLock())
		{
			await FeedAsync([.. Enumerable.Repeat(frame, Fed)]);
			await Distributor.StopAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);
			stop = Store.StopAsync(new CancellationToken(canceled: true));
		}

		await stop.WaitAsync(_stopLimit, TestContext.Current.CancellationToken);

		(Count("spectrum") + Store.DroppedFrames).Should().Be(Fed);
		Store.DroppedFrames.Should().BeGreaterThanOrEqualTo(Fed - 256, "a batch holds at most 256 frames");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task DisposeOnlyCancelsAndTheWriterClosesTheConnection()
	{
		await StartOpenedAsync();
		Feed(StoreSamples.Spectrum(StoreSamples.Start));
		await Waits.UntilAsync(() => Count("spectrum") == 1);

		Store.Dispose();

		await Waits.UntilAsync(StoppedLogged);
		var delete = () => File.Delete(_path);
		delete.Should().NotThrow();
		_logger.Levels().Should().NotContain(level => level >= LogLevel.Warning);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task DataStaysAcrossLinkSessions()
	{
		await StartOpenedAsync();
		ReceivedFrame[] firstSession = [.. Enumerable.Range(0, 3)
			.Select(i => StoreSamples.Spectrum(StoreSamples.Start.AddSeconds(i), seq: (byte)i))];
		ReceivedFrame[] secondSession = [.. Enumerable.Range(0, 3)
			.Select(i => StoreSamples.Spectrum(StoreSamples.Start.AddMinutes(1).AddSeconds(i), seq: (byte)i))];

		Feed(firstSession);
		await Waits.UntilAsync(() => Count("spectrum") == 3);
		await FeedAsync([StoreSamples.Heartbeat(StoreSamples.Start.AddSeconds(30)), .. secondSession]);
		await StopAsync();

		Count("spectrum").Should().Be(6);
		Count("heartbeat").Should().Be(1);
		Scalar("SELECT group_concat(seq) FROM (SELECT seq FROM spectrum ORDER BY id)").Should().Be("0,1,2,0,1,2");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task StopDrainsTheQueueThenCheckpoints()
	{
		const int Fed = 600;
		_channel = Channel.CreateUnbounded<ReceivedFrame>();
		await StartOpenedAsync();

		await FeedAsync([.. Enumerable.Repeat(StoreSamples.Spectrum(StoreSamples.Start), Fed)]);
		await StopAsync();

		Count("spectrum").Should().Be(Fed);
		Store.DroppedFrames.Should().Be(0);
		var wal = new FileInfo(_path + "-wal");
		(!wal.Exists || wal.Length == 0).Should().BeTrue("the checkpoint truncates the WAL");
		_logger.Collector.LatestRecord.Message.Should().Be(
			$"The spectrum store stopped: {Fed} frames stored, 0 frames dropped");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AStopWithAFullQueueDrainsInsideTheCloseLimit()
	{
		const int QueueCapacity = 1024;
		const int BatchSize = 256;
		const int Fed = QueueCapacity + BatchSize;
		var closeLimit = TimeSpan.FromSeconds(5);
		var closeReserve = TimeSpan.FromSeconds(0.5);
		_channel = Channel.CreateUnbounded<ReceivedFrame>();
		await StartOpenedAsync();

		using (HoldWriteLock())
		{
			await FeedAsync([.. Enumerable.Repeat(StoreSamples.Spectrum(StoreSamples.Start), Fed)]);
		}

		using var drain = new CancellationTokenSource(closeLimit - closeReserve);
		var stopwatch = Stopwatch.StartNew();
		await Store.StopAsync(drain.Token).WaitAsync(_stopLimit, TestContext.Current.CancellationToken);

		stopwatch.Elapsed.Should().BeLessThan(closeLimit);
		(Count("spectrum") + Store.DroppedFrames).Should().Be(Fed);
		Store.DroppedFrames.Should().BeLessThanOrEqualTo(BatchSize, "only frames past the full queue are dropped");
		Count("spectrum").Should().BeGreaterThanOrEqualTo(QueueCapacity, "the full queue drains before the drain token");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task StopBeforeStartCompletesAtOnce()
	{
		CreateStore();

		var stop = Store.StopAsync(TestContext.Current.CancellationToken);

		stop.IsCompletedSuccessfully.Should().BeTrue();
		Store.Start();
		Distributor.Start();
		Feed(StoreSamples.Spectrum(StoreSamples.Start));
		await Task.Delay(_noWriteWait, TestContext.Current.CancellationToken);
		File.Exists(_path).Should().BeFalse("Start after a stop opens no file");
		_logger.Collector.Count.Should().Be(0);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AFailedOpenReportsOneErrorAndStoresNothing()
	{
		var blocker = _folder.FileIn("blocker");
		await File.WriteAllTextAsync(blocker, "a file where the store folder belongs", TestContext.Current.CancellationToken);
		var path = Path.Combine(blocker, "spectra-1.db");
		CreateStore(path: path);
		StartStore();

		await Waits.UntilAsync(() => _panel.Entries.Count == 1);
		await FeedAsync(StoreSamples.Spectrum(StoreSamples.Start), StoreSamples.Heartbeat(StoreSamples.Start));
		await StopAsync();

		var entry = _panel.Entries.Should().ContainSingle().Subject;
		entry.View.Title.Should().Be(Resources.StoreOpenTitle);
		entry.View.Severity.Should().Be(MessageSeverity.Error);
		Store.DroppedFrames.Should().Be(0, "no history exists to have holes");
		File.Exists(path).Should().BeFalse();
		_logger.Failures().Should().ContainSingle().Which.Should().BeAssignableTo<IOException>();
		_logger.Levels().Should().NotContain(LogLevel.Information);
	}

	private static Channel<ReceivedFrame> LinkChannel()
	{
		return Channel.CreateBounded<ReceivedFrame>(new BoundedChannelOptions(64)
		{
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleWriter = true,
			SingleReader = true
		});
	}

	private void CreateStore(TimeProvider? clock = null, string? path = null)
	{
		_distributor = new FrameDistributor(_channel.Reader, _panel, ImmediateScheduler.Instance, _distributorLogger);
		_store = new SpectrumStore(
			_distributor,
			new StoreFile(path ?? _path),
			_panel,
			ImmediateScheduler.Instance,
			clock ?? TimeProvider.System,
			_logger);
	}

	/// <summary>Starts the store, then counts what the distributor delivers after the store's own subscriber.</summary>
	private void StartStore()
	{
		Store.Start();
		Distributor.Frames.Subscribe(_ => Interlocked.Increment(ref _delivered));
		Distributor.Start();
	}

	private async Task StartOpenedAsync(TimeProvider? clock = null)
	{
		CreateStore(clock);
		StartStore();
		await OpenedAsync();
	}

	private Task OpenedAsync()
	{
		return Waits.UntilAsync(() => _logger.Collector.GetSnapshot().Any(record => record.Level == LogLevel.Information));
	}

	private Task StopAsync()
	{
		return Store.StopAsync().WaitAsync(_stopLimit);
	}

	private bool StoppedLogged()
	{
		return _logger.Collector.GetSnapshot()
			.Any(record => record.Message.StartsWith("The spectrum store stopped", StringComparison.Ordinal));
	}

	private void Feed(params ReceivedFrame[] frames)
	{
		foreach (var frame in frames)
		{
			_channel.Writer.TryWrite(frame).Should().BeTrue();
		}
	}

	/// <summary>Feeds the frames and waits until the store's subscriber has taken every one.</summary>
	private Task FeedAsync(params ReceivedFrame[] frames)
	{
		var expected = Volatile.Read(ref _delivered) + frames.Length;
		Feed(frames);
		return Waits.UntilAsync(() => Volatile.Read(ref _delivered) == expected);
	}

	private async Task FailOneBatchAsync(long expectedDropped)
	{
		Feed(StoreSamples.Spectrum(StoreSamples.Start, seq: FailingSeq));
		await Waits.UntilAsync(() => Store.DroppedFrames == expectedDropped);
	}

	private void FailWritesOfSeq99()
	{
		using var connection = OpenPlain();
		using var command = connection.CreateCommand();
		command.CommandText = $"""
			CREATE TRIGGER fail_seq_{FailingSeq} BEFORE INSERT ON spectrum WHEN NEW.seq = {FailingSeq}
			BEGIN SELECT RAISE(ABORT, 'test'); END
			""";
		command.ExecuteNonQuery();
	}

	/// <summary>A second connection inside BEGIN IMMEDIATE: the writer's batches wait out the busy timeout and fail.</summary>
	private SqliteConnection HoldWriteLock()
	{
		var holder = OpenPlain();
		using var command = holder.CreateCommand();
		command.CommandText = "BEGIN IMMEDIATE";
		command.ExecuteNonQuery();
		return holder;
	}

	private long Count(string table)
	{
		return (long)Scalar($"SELECT count(*) FROM {table}")!;
	}

	private object? Scalar(string sql)
	{
		using var connection = OpenPlain();
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return command.ExecuteScalar();
	}

	private SqliteConnection OpenPlain()
	{
		var connection = new SqliteConnection(
			new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString());
		connection.Open();
		return connection;
	}

	/// <summary>A clock that moves only on Advance; its timers are one-shot and fire on the Advance that passes them.</summary>
	private sealed class ManualClock(DateTimeOffset start) : TimeProvider
	{
		private readonly Lock _gate = new();
		private readonly List<ManualTimer> _timers = [];
		private DateTimeOffset _now = start;

		public override DateTimeOffset GetUtcNow()
		{
			lock (_gate)
			{
				return _now;
			}
		}

		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{
			var timer = new ManualTimer(this, callback, state);
			timer.Change(dueTime, period);
			return timer;
		}

		public void Advance(TimeSpan by)
		{
			List<ManualTimer> due;
			lock (_gate)
			{
				_now += by;
				due = [.. _timers.Where(timer => timer.DueAt <= _now)];
				_timers.RemoveAll(due.Contains);
			}

			foreach (var timer in due)
			{
				timer.Fire();
			}
		}

		private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
		{
			public DateTimeOffset DueAt { get; private set; }

			public bool Change(TimeSpan dueTime, TimeSpan period)
			{
				lock (clock._gate)
				{
					clock._timers.Remove(this);
					if (dueTime != Timeout.InfiniteTimeSpan)
					{
						DueAt = clock._now + dueTime;
						clock._timers.Add(this);
					}
				}

				return true;
			}

			public void Fire()
			{
				callback(state);
			}

			public void Dispose()
			{
				lock (clock._gate)
				{
					clock._timers.Remove(this);
				}
			}

			public ValueTask DisposeAsync()
			{
				Dispose();
				return ValueTask.CompletedTask;
			}
		}
	}
}
