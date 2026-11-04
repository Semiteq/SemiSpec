using System.ComponentModel;
using System.Threading.Channels;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Reactive.Testing;

using ReactiveUI.Avalonia;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Tests.Unit.Core.Store;
using SpectrumVisualizer.Tests.Unit.UI.Link;
using SpectrumVisualizer.Tests.Unit.UI.Messages;
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
[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class AppStatusBarViewModelTests : IAsyncDisposable
{
	private const string PortName = "COM3";
	private const int LinkChannelCapacity = 64;
	private const int LinkOverflow = 3;
	private const byte FailingSeq = 99;

	// A held store keeps its writer until StopAsync has closed the file.
	private static readonly TimeSpan _storeStopLimit = TimeSpan.FromSeconds(10);

	private readonly TestScheduler _linkScheduler = new();
	private readonly FakeSerialPortProvider _portProvider = new(PortName);
	private readonly FakePortStream _port = new();
	private readonly MessagePanelViewModel _messagePanel = new(TimeProvider.System);
	private readonly FakeLogger<AppStatusBarViewModel> _logger = new();
	private readonly SerialLink _serialLink;
	private readonly TempFolder _storeFolder = new();
	private readonly Channel<ReceivedFrame> _storeFrames = Channel.CreateUnbounded<ReceivedFrame>();
	private readonly FakeLogger<SpectrumStore> _storeLogger = new();
	private readonly FrameDistributor _storeDistributor;
	private readonly SpectrumStore _store;
	private readonly AppStatusBarViewModel _viewModel;
	private readonly List<(string Text, LinkTone Tone)> _shownStates = [];

	public AppStatusBarViewModelTests()
	{
		_serialLink = new SerialLink(_portProvider, _linkScheduler, new FakeLogger<SerialLink>());
		_storeDistributor = new FrameDistributor(
			_storeFrames.Reader,
			_messagePanel,
			AvaloniaScheduler.Instance,
			new FakeLogger<FrameDistributor>());
		_store = new SpectrumStore(
			_storeDistributor,
			new StoreFile(StorePath),
			_messagePanel,
			AvaloniaScheduler.Instance,
			TimeProvider.System,
			_storeLogger);
		_viewModel = new AppStatusBarViewModel(
			_serialLink,
			_store,
			_messagePanel,
			AvaloniaScheduler.Instance,
			_logger);
		_viewModel.PropertyChanged += (_, arguments) =>
		{
			var shown = (_viewModel.LinkText, _viewModel.LinkTone);
			if (arguments.PropertyName == nameof(AppStatusBarViewModel.Status)
				&& (_shownStates.Count == 0 || _shownStates[^1] != shown))
			{
				_shownStates.Add(shown);
			}
		};
	}

	private string StorePath => _storeFolder.FileIn("spectra-1.db");

	public static TheoryData<BoardState, string, string> SpectrometerStates => new()
	{
		{ BoardState.None, Resources.StatusSpectrometerNotConnected, Resources.StatusSpectrometerNotConnected },
		{
			BoardState.Atp8000Connected,
			Resources.StatusSpectrometerNotConnected,
			Resources.StatusSpectrometerConnected
		},
		{
			BoardState.Atp2000Connected | BoardState.Atp8000Connected | BoardState.Atp2000MeasurementFailed,
			Resources.StatusSpectrometerFailed,
			Resources.StatusSpectrometerConnected
		},
		{
			BoardState.Atp8000Connected | BoardState.Atp8000MeasurementFailed,
			Resources.StatusSpectrometerNotConnected,
			Resources.StatusSpectrometerFailed
		},
		{
			BoardState.Atp2000MeasurementFailed,
			Resources.StatusSpectrometerNotConnected,
			Resources.StatusSpectrometerNotConnected
		},
	};

	[AvaloniaFact]
	public async Task EachLinkStateShowsItsTextAndTone()
	{
		await ConnectAsync();
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));
		await AtAsync(11, port => port.PushTimeout());
		await LoseAsync(12);
		await DisconnectAsync();

		_shownStates.Should().Equal(
			(Resources.StatusConnecting, LinkTone.Neutral),
			(Resources.StatusWaiting, LinkTone.Neutral),
			(Resources.StatusReceiving, LinkTone.Ok),
			(Resources.StatusNotResponding, LinkTone.Fault),
			(Resources.StatusPortUnavailable, LinkTone.Fault),
			(Resources.StatusNotConnected, LinkTone.Neutral));
	}

	[AvaloniaFact]
	public async Task BytesWithoutAValidFrameShowOtherDevice()
	{
		await ConnectAsync();
		await AtAsync(1, port => port.PushBytes(Corrupted(SpectrumFrame(Spectrometer.Atp2000, 1))));
		await AtAsync(4, port => port.PushBytes(Corrupted(SpectrumFrame(Spectrometer.Atp2000, 2))));
		await AtAsync(10, port => port.PushTimeout());

		_viewModel.LinkText.Should().Be(Resources.StatusOtherDevice);
		_viewModel.LinkTone.Should().Be(LinkTone.Fault);
		_messagePanel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ErrorMessageMapper.Map(new UnrecognisedDataError(PortName)));
	}

	[AvaloniaFact]
	public void TheInitialTextComesFromTheLinkStatus()
	{
		_viewModel.Status.Should().BeSameAs(_serialLink.Status);
		_viewModel.LinkText.Should().Be(Resources.StatusNotConnected);
		_viewModel.LinkTone.Should().Be(LinkTone.Neutral);
	}

	[AvaloniaFact]
	public async Task PortAndLostCountReadNoValueOnlyWhileDisconnected()
	{
		_viewModel.PortText.Should().Be(Resources.StatusNoValue);
		_viewModel.LostSpectraText.Should().Be(Resources.FormatStatusLostSpectraFormat(Resources.StatusNoValue));

		await ConnectAsync();
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 1))));
		await AtAsync(2, port => port.PushBytes(FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 4))));

		_viewModel.PortText.Should().Be(PortName);
		_viewModel.LostSpectraText.Should().Be(Resources.FormatStatusLostSpectraFormat(2));

		await DisconnectAsync();

		_viewModel.PortText.Should().Be(Resources.StatusNoValue);
		_viewModel.LostSpectraText.Should().Be(Resources.FormatStatusLostSpectraFormat(Resources.StatusNoValue));
	}

	[AvaloniaFact]
	public async Task BothSpectrometersReadNoDataBeforeTheFirstFrame()
	{
		await ConnectAsync();

		_viewModel.Atp2000Text.Should().Be(SpectrometerText("ATP-2000", Resources.StatusSpectrometerNoData));
		_viewModel.Atp8000Text.Should().Be(SpectrometerText("ATP-8000", Resources.StatusSpectrometerNoData));
	}

	[AvaloniaTheory]
	[MemberData(nameof(SpectrometerStates))]
	public async Task EachSpectrometerTextFollowsItsStateBits(BoardState state, string atp2000, string atp8000)
	{
		await ConnectAsync();
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(new BoardFrame(state, 1, null))));

		_viewModel.Atp2000Text.Should().Be(SpectrometerText("ATP-2000", atp2000));
		_viewModel.Atp8000Text.Should().Be(SpectrometerText("ATP-8000", atp8000));
	}

	[AvaloniaFact]
	public async Task AFaultIsReportedOnceForItsFaultedStatus()
	{
		await ConnectAsync();
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));
		await AtAsync(11, port => port.PushTimeout());
		await AtAsync(21, port => port.PushTimeout());
		await AtAsync(31, port => port.PushTimeout());

		_messagePanel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ErrorMessageMapper.Map(new BoardNotRespondingError(PortName)));
		_logger.Collector.GetSnapshot().Select(record => record.Level).Should().Equal(LogLevel.Warning);
	}

	[AvaloniaFact]
	public async Task ReceivingAfterAFaultAddsOneInfoEntry()
	{
		await ConnectAsync();
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));
		await AtAsync(11, port => port.PushTimeout());
		await AtAsync(12, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(2))));
		await AtAsync(13, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(3))));

		_messagePanel.Entries.Select(entry => entry.View).Should().Equal(
			new MessageView(
				Resources.LinkRestoredTitle,
				Resources.LinkRestoredDetail,
				string.Empty,
				MessageSeverity.Info),
			ErrorMessageMapper.Map(new BoardNotRespondingError(PortName)));
		_logger.Collector.GetSnapshot().Select(record => record.Level)
			.Should().Equal(LogLevel.Warning, LogLevel.Information);
	}

	[AvaloniaFact]
	public async Task TheFirstReceivingAfterConnectAddsNoEntry()
	{
		await ConnectAsync();
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));

		_viewModel.LinkText.Should().Be(Resources.StatusReceiving);
		_messagePanel.Entries.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task AConnectAfterAFaultStartsCleanAndAddsNoInfoEntry()
	{
		await using var reconnected = new FakePortStream();
		await ConnectAsync();
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));
		await AtAsync(11, port => port.PushTimeout());
		await DisconnectAsync();

		await ConnectAsync(reconnected);
		await AtAsync(reconnected, 12, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(2))));

		_viewModel.LinkText.Should().Be(Resources.StatusReceiving);
		_messagePanel.Entries.Select(entry => entry.View.Severity).Should().Equal(MessageSeverity.Warning);
	}

	[AvaloniaFact]
	public void AFreshAppShowsZeroDroppedFramesNotTheNoValueDash()
	{
		_viewModel.DroppedFramesText.Should().Be(Resources.FormatStatusDroppedFramesFormat(0));
		_viewModel.LostSpectraText.Should().Be(
			Resources.FormatStatusLostSpectraFormat(Resources.StatusNoValue),
			"only the lost count describes the current connection");
	}

	[AvaloniaFact]
	public async Task TheDroppedSegmentSumsTheLinkAndTheStore()
	{
		await StartStoreAsync();
		await FailOneStoreBatchAsync();
		await ConnectAsync();

		await OverflowTheLinkChannelAsync(1);

		await Waits.UntilAsync(
			() => _viewModel.DroppedFramesText == Resources.FormatStatusDroppedFramesFormat(LinkOverflow + 1),
			pumpDispatcher: true);
		_serialLink.Status.DroppedFrames.Should().Be(LinkOverflow);
		_store.DroppedFrames.Should().Be(1);
	}

	[AvaloniaFact]
	public async Task TheDroppedCountStaysAfterADisconnect()
	{
		await ConnectAsync();
		await OverflowTheLinkChannelAsync(1);

		await DisconnectAsync();

		_viewModel.LinkText.Should().Be(Resources.StatusNotConnected);
		_viewModel.DroppedFramesText.Should().Be(Resources.FormatStatusDroppedFramesFormat(LinkOverflow));
	}

	[AvaloniaFact]
	public async Task AStoreDropOnAnotherThreadReachesTheTextOnTheUiScheduler()
	{
		var changedOnUiThread = new List<bool>();
		void OnChanged(object? sender, PropertyChangedEventArgs arguments)
		{
			if (arguments.PropertyName == nameof(AppStatusBarViewModel.DroppedFramesText))
			{
				changedOnUiThread.Add(Dispatcher.UIThread.CheckAccess());
			}
		}

		_viewModel.PropertyChanged += OnChanged;
		await StartStoreAsync();

		await FailOneStoreBatchAsync();

		await Waits.UntilAsync(
			() => _viewModel.DroppedFramesText == Resources.FormatStatusDroppedFramesFormat(1),
			pumpDispatcher: true);
		_viewModel.PropertyChanged -= OnChanged;
		changedOnUiThread.Should().NotBeEmpty().And.OnlyContain(onUiThread => onUiThread);
	}

	[AvaloniaFact]
	public async Task AThrowingPanelGoesToTheLog()
	{
		using var poison = ReportingTestDoubles.PoisonEntries(_messagePanel);
		await ConnectAsync();
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));
		await AtAsync(11, port => port.PushTimeout());
		Dispatcher.UIThread.RunJobs();

		_viewModel.LinkText.Should().Be(
			Resources.StatusNotResponding,
			"a failed report leaves the shown state in place");
		var last = _logger.LatestRecord;
		last.Level.Should().Be(LogLevel.Error);
		last.Message.Should().Be("The failure could not reach the message panel");
		last.Exception.Should().BeOfType<PoisonedPanelException>();
	}

	public async ValueTask DisposeAsync()
	{
		_viewModel.Dispose();
		await _storeDistributor.StopAsync().WaitAsync(Waits.Limit);
		_storeDistributor.Dispose();
		await _store.StopAsync().WaitAsync(_storeStopLimit);
		_store.Dispose();
		_storeFolder.Dispose();
		_serialLink.Dispose();
		await _port.DisposeAsync();
		_messagePanel.Dispose();
	}

	private static string SpectrometerText(string device, string state)
	{
		return Resources.FormatStatusSpectrometerFormat(device, state);
	}

	private static long Ticks(double seconds)
	{
		return TimeSpan.FromSeconds(seconds).Ticks;
	}

	private async Task ConnectAsync(FakePortStream? port = null)
	{
		port ??= _port;
		_portProvider.QueueOpen(port);
		_serialLink.Connect(PortName);
		await port.WaitForPendingReadAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);
		await SettleAsync();
	}

	private Task AtAsync(double seconds, Action<FakePortStream> push)
	{
		return AtAsync(_port, seconds, push);
	}

	/// <summary>Moves virtual time first, so the link stamps the step with the given second.</summary>
	private async Task AtAsync(FakePortStream port, double seconds, Action<FakePortStream> push)
	{
		_linkScheduler.AdvanceTo(Ticks(seconds));
		push(port);
		await port.WaitForPendingReadAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);
		await SettleAsync();
	}

	private async Task LoseAsync(double seconds)
	{
		_linkScheduler.AdvanceTo(Ticks(seconds));
		_port.PushZeroRead();
		await Waits.UntilAsync(() => _serialLink.Status.State == LinkState.PortUnavailable, pumpDispatcher: true);
		await SettleAsync();
	}

	private async Task DisconnectAsync()
	{
		await _serialLink.DisconnectAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);
		await SettleAsync();
	}

	private Task SettleAsync()
	{
		return Waits.StatusBarSettledAsync(_viewModel, _serialLink);
	}

	/// <summary>Consecutive seq values, so the sequence tracker sees no gap; the channel drops the overflow.</summary>
	private Task OverflowTheLinkChannelAsync(double seconds)
	{
		var frames = Enumerable.Range(1, LinkChannelCapacity + LinkOverflow)
			.Select(seq => FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, (byte)seq)));
		return AtAsync(seconds, port => port.PushBytes(Concat(frames)));
	}

	private async Task StartStoreAsync()
	{
		_store.Start();
		_storeDistributor.Start();
		await Waits.UntilAsync(
			() => _storeLogger.Collector.GetSnapshot().Any(record => record.Level == LogLevel.Information));
	}

	/// <summary>A trigger rejects the frame, so its batch fails on the writer task and counts one drop there.</summary>
	private async Task FailOneStoreBatchAsync()
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
		await Waits.UntilAsync(() => _store.DroppedFrames == 1);
	}
}
