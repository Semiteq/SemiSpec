using System.Threading.Channels;

using AwesomeAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Reactive.Testing;

using ReactiveUI.Avalonia;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Tests.Unit.Core.Store;
using SpectrumVisualizer.Tests.Unit.UI.Link;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.MainWindow;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Store;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SpectrumVisualizer.Tests.Unit.UI.MainWindow;

/// <summary>
/// Drives a real link over the fake port and a real store on a temp file, so the bar sees what both publish. Nothing
/// reads the link's frames, so its channel fills and drops.
/// </summary>
public abstract class AppStatusBarTestBase : IAsyncDisposable
{
	protected const string PortName = "COM3";
	protected const int LinkOverflow = 3;
	private const int LinkChannelCapacity = 64;
	private const byte FailingSeq = 99;

	private static readonly TimeSpan _storeStopLimit = TimeSpan.FromSeconds(10);

	private readonly TestScheduler _linkScheduler = new();
	private readonly FakeSerialPortProvider _portProvider = new(PortName);
	private readonly TempFolder _storeFolder = new();
	private readonly Channel<ReceivedFrame> _storeFrames = Channel.CreateUnbounded<ReceivedFrame>();
	private readonly FakeLogger<SpectrumStore> _storeLogger = new();
	private readonly FrameDistributor _storeDistributor;
	private readonly List<(string Text, LinkTone Tone)> _shownStates = [];

	protected AppStatusBarTestBase()
	{
		SerialLink = new SerialLink(_portProvider, _linkScheduler, new FakeLogger<SerialLink>());
		_storeDistributor = new FrameDistributor(
			_storeFrames.Reader,
			MessagePanel,
			AvaloniaScheduler.Instance,
			new FakeLogger<FrameDistributor>());
		Store = new SpectrumStore(
			_storeDistributor,
			new StoreFile(StorePath),
			MessagePanel,
			AvaloniaScheduler.Instance,
			TimeProvider.System,
			_storeLogger);
		ViewModel = new AppStatusBarViewModel(
			SerialLink,
			Store,
			MessagePanel,
			AvaloniaScheduler.Instance,
			Logger);
		ViewModel.PropertyChanged += (_, arguments) =>
		{
			var shown = (ViewModel.LinkText, ViewModel.LinkTone);
			if (arguments.PropertyName == nameof(AppStatusBarViewModel.Status)
				&& (_shownStates.Count == 0 || _shownStates[^1] != shown))
			{
				_shownStates.Add(shown);
			}
		};
	}

	private protected FakePortStream Port { get; } = new();

	protected MessagePanelViewModel MessagePanel { get; } = new(TimeProvider.System);

	protected FakeLogger<AppStatusBarViewModel> Logger { get; } = new();

	protected SerialLink SerialLink { get; }

	protected SpectrumStore Store { get; }

	protected AppStatusBarViewModel ViewModel { get; }

	/// <summary>Each distinct link text and tone the bar showed, in order.</summary>
	protected IReadOnlyList<(string Text, LinkTone Tone)> ShownStates => _shownStates;

	private string StorePath => _storeFolder.FileIn("spectra-1.db");

	public async ValueTask DisposeAsync()
	{
		ViewModel.Dispose();
		await _storeDistributor.StopAsync().WaitAsync(Waits.Limit);
		_storeDistributor.Dispose();
		await Store.StopAsync().WaitAsync(_storeStopLimit);
		Store.Dispose();
		_storeFolder.Dispose();
		SerialLink.Dispose();
		await Port.DisposeAsync();
		MessagePanel.Dispose();
		GC.SuppressFinalize(this);
	}

	protected static string SpectrometerText(string device, string state)
	{
		return Resources.FormatStatusSpectrometerFormat(device, state);
	}

	private static long Ticks(double seconds)
	{
		return TimeSpan.FromSeconds(seconds).Ticks;
	}

	private protected async Task ConnectAsync(FakePortStream? port = null)
	{
		port ??= Port;
		_portProvider.QueueOpen(port);
		SerialLink.Connect(PortName);
		await port.WaitForPendingReadAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);
		await SettleAsync();
	}

	private protected Task AtAsync(double seconds, Action<FakePortStream> push)
	{
		return AtAsync(Port, seconds, push);
	}

	/// <summary>Moves virtual time first, so the link stamps the step with the given second.</summary>
	private protected async Task AtAsync(FakePortStream port, double seconds, Action<FakePortStream> push)
	{
		_linkScheduler.AdvanceTo(Ticks(seconds));
		push(port);
		await port.WaitForPendingReadAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);
		await SettleAsync();
	}

	protected async Task LoseAsync(double seconds)
	{
		_linkScheduler.AdvanceTo(Ticks(seconds));
		Port.PushZeroRead();
		await Waits.UntilAsync(() => SerialLink.Status.State == LinkState.PortUnavailable, pumpDispatcher: true);
		await SettleAsync();
	}

	protected async Task DisconnectAsync()
	{
		await SerialLink.DisconnectAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);
		await SettleAsync();
	}

	protected Task SettleAsync()
	{
		return Waits.StatusBarSettledAsync(ViewModel, SerialLink);
	}

	/// <summary>Consecutive seq values, so the sequence tracker sees no gap; the channel drops the overflow.</summary>
	protected Task OverflowTheLinkChannelAsync(double seconds)
	{
		var frames = Enumerable.Range(1, LinkChannelCapacity + LinkOverflow)
			.Select(seq => FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, (byte)seq)));
		return AtAsync(seconds, port => port.PushBytes(Concat(frames)));
	}

	protected async Task StartStoreAsync()
	{
		Store.Start();
		_storeDistributor.Start();
		await Waits.UntilAsync(
			() => _storeLogger.Collector.GetSnapshot().Any(record => record.Level == LogLevel.Information));
	}

	/// <summary>A trigger rejects the frame, so its batch fails on the writer task and counts one drop there.</summary>
	protected async Task FailOneStoreBatchAsync()
	{
		using (var connection = new SqliteConnection(
			new SqliteConnectionStringBuilder { DataSource = StorePath, Pooling = false }.ToString()))
		{
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = $"""
				CREATE TRIGGER fail_seq_{FailingSeq} BEFORE INSERT ON spectrum WHEN NEW.seq = {FailingSeq}
				BEGIN SELECT RAISE(ABORT, 'test'); END
				""";
			command.ExecuteNonQuery();
		}

		_storeFrames.Writer.TryWrite(StoreSamples.Spectrum(StoreSamples.Start, seq: FailingSeq)).Should().BeTrue();
		await Waits.UntilAsync(() => Store.DroppedFrames == 1);
	}
}
