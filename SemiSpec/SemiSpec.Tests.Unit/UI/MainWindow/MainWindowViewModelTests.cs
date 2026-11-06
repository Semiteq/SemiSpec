using System.Reactive.Concurrency;
using System.Reactive.Linq;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using SemiSpec.Tests.Unit.UI.Link;
using SemiSpec.Tests.Unit.UI.Logging;
using SemiSpec.Tests.Unit.UI.Spectrum;
using SemiSpec.Tests.Unit.UI.Store;
using SemiSpec.Tests.Unit.UI.Trend;
using SemiSpec.UI.Link;
using SemiSpec.UI.Logging;
using SemiSpec.UI.MainWindow;
using SemiSpec.UI.Messages;
using SemiSpec.UI.Spectrum;

using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.MainWindow;

[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowViewModelTests : IDisposable
{
	private readonly LogLevelControl _logLevelControl = new();
	private readonly CollectingSink _sink = new();
	private readonly Logger _serilogLogger;
	private readonly SerilogLoggerFactory _loggerFactory;
	private readonly MessagePanelViewModel _messagePanel = new(TimeProvider.System);
	private readonly SerialLink _serialLink;
	private readonly UnstartedStore _store;
	private readonly ConnectionBarViewModel _connectionBar;
	private readonly AppStatusBarViewModel _statusBar;
	private readonly FakeDisplayedSpectrumSource _spectrumSource = new();
	private readonly SwitchingSpectrumSource _switchingSource;
	private readonly SpectrumViewModel _spectrum;
	private readonly TrendStand _trend;
	private readonly MainWindowViewModel _viewModel;

	public MainWindowViewModelTests()
	{
		var portProvider = new FakeSerialPortProvider();
		_serialLink = new SerialLink(portProvider, ImmediateScheduler.Instance, NullLogger<SerialLink>.Instance);
		_connectionBar = new ConnectionBarViewModel(
			portProvider,
			_serialLink,
			_messagePanel,
			ImmediateScheduler.Instance,
			NullLogger<ConnectionBarViewModel>.Instance);
		_serilogLogger = new LoggerConfiguration()
			.MinimumLevel.ControlledBy(_logLevelControl.Switch)
			.WriteTo.Sink(_sink)
			.CreateLogger();
		_loggerFactory = new SerilogLoggerFactory(_serilogLogger);
		_store = new UnstartedStore(_messagePanel);
		_statusBar = new AppStatusBarViewModel(
			_serialLink,
			_store.Store,
			_messagePanel,
			ImmediateScheduler.Instance,
			NullLogger<AppStatusBarViewModel>.Instance);
		_switchingSource = new SwitchingSpectrumSource(_spectrumSource);
		_spectrum = new SpectrumViewModel(
			_switchingSource,
			_serialLink,
			_messagePanel,
			TimeProvider.System,
			ImmediateScheduler.Instance,
			NullLogger<SpectrumViewModel>.Instance);
		_trend = new TrendStand(_switchingSource, _messagePanel);
		_viewModel = new MainWindowViewModel(
			_messagePanel,
			_connectionBar,
			_statusBar,
			_spectrum,
			_trend.Trend,
			_logLevelControl,
			_loggerFactory.CreateLogger<MainWindowViewModel>());
	}

	[AvaloniaFact]
	public void VerboseLoggingStartsOff()
	{
		_viewModel.IsVerboseLogging.Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task ToggleVerboseLoggingRaisesTheLevelAndTheCheckedStateFollows()
	{
		var changed = new List<string?>();
		_viewModel.PropertyChanged += (_, arguments) => changed.Add(arguments.PropertyName);

		await _viewModel.ToggleVerboseLoggingCommand.Execute();

		_logLevelControl.IsVerbose.Should().BeTrue();
		_viewModel.IsVerboseLogging.Should().BeTrue();
		changed.Should().Contain(nameof(MainWindowViewModel.IsVerboseLogging));
	}

	[AvaloniaFact]
	public async Task ToggleVerboseLoggingTwiceLowersTheLevelBack()
	{
		await _viewModel.ToggleVerboseLoggingCommand.Execute();
		await _viewModel.ToggleVerboseLoggingCommand.Execute();

		_logLevelControl.IsVerbose.Should().BeFalse();
		_viewModel.IsVerboseLogging.Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task EachToggleLogsTheNewLevelAtInformation()
	{
		await _viewModel.ToggleVerboseLoggingCommand.Execute();
		await _viewModel.ToggleVerboseLoggingCommand.Execute();

		_sink.Events
			.Where(logEvent => logEvent.Level == LogEventLevel.Information)
			.Select(logEvent => logEvent.Properties["Level"].ToString())
			.Should().Equal(nameof(LogEventLevel.Debug), nameof(LogEventLevel.Information));
	}

	[AvaloniaFact]
	public async Task TheToggleWritesADebugLineOnlyWhenItTurnsVerboseOn()
	{
		await _viewModel.ToggleVerboseLoggingCommand.Execute();
		var debugLinesWhileVerbose = DebugLineCount();
		await _viewModel.ToggleVerboseLoggingCommand.Execute();

		debugLinesWhileVerbose.Should().Be(1);
		DebugLineCount().Should().Be(1, "switching verbose off filters the toggle's own Debug line");
	}

	[AvaloniaFact]
	public async Task ADebugEventReachesTheSinkOnlyWhileVerbose()
	{
		var debugLogger = _serilogLogger.ForContext("Probe", true);

		debugLogger.Debug("before");
		await _viewModel.ToggleVerboseLoggingCommand.Execute();
		debugLogger.Debug("verbose");
		await _viewModel.ToggleVerboseLoggingCommand.Execute();
		debugLogger.Debug("after");

		_sink.Events
			.Where(logEvent => logEvent.Properties.ContainsKey("Probe"))
			.Select(logEvent => logEvent.MessageTemplate.Text)
			.Should().Equal("verbose");
	}

	[AvaloniaFact]
	public void TheSpectrumIsTheOneItWasGiven()
	{
		_viewModel.Spectrum.Should().BeSameAs(_spectrum);
	}

	[AvaloniaFact]
	public void DisposeLeavesTheInjectedTrendToItsOwner()
	{
		var trend = _viewModel.Trend;
		var oneMinute = trend.Presets[0];

		_viewModel.Dispose();
		trend.SelectedPreset = oneMinute;

		trend.Should().BeSameAs(_trend.Trend);
		trend.Chart.Width.Should().Be(oneMinute.Width, "the trend view model still drives its chart");
	}

	[AvaloniaFact]
	public void DisposeLeavesTheInjectedSpectrumToItsOwner()
	{
		var spectrum = SpectrumSamples.Atp8000Peak;

		_viewModel.Dispose();
		_spectrumSource.Current = spectrum;
		_spectrumSource.Signal();

		_spectrum.SignalInfo.Spectrum.Should().BeSameAs(spectrum, "the spectrum view model still shows new spectra");
	}

	[AvaloniaFact]
	public async Task ExitAsksTheWindowToClose()
	{
		var requests = 0;
		using var subscription = _viewModel.ExitRequests.Subscribe(_ => requests++);

		await _viewModel.ExitCommand.Execute();

		requests.Should().Be(1);
	}

	public void Dispose()
	{
		_viewModel.Dispose();
		_spectrum.Dispose();
		_trend.Dispose();
		_switchingSource.Dispose();
		_spectrumSource.Dispose();
		_connectionBar.Dispose();
		_statusBar.Dispose();
		_serialLink.Dispose();
		_store.Dispose();
		_messagePanel.Dispose();
		_loggerFactory.Dispose();
		_serilogLogger.Dispose();
	}

	private int DebugLineCount()
	{
		return _sink.Events.Count(logEvent => logEvent.Level == LogEventLevel.Debug);
	}
}
