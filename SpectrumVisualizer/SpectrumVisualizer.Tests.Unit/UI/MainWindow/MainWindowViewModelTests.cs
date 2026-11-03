using System.Reactive.Linq;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Serilog;
using Serilog.Core;
using Serilog.Events;

using SpectrumVisualizer.UI.Logging;
using SpectrumVisualizer.UI.MainWindow;
using SpectrumVisualizer.UI.Messages;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.MainWindow;

[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowViewModelTests : IDisposable
{
	private readonly LogLevelControl _logLevelControl = new();
	private readonly MessagePanelViewModel _messagePanel = new(TimeProvider.System);
	private readonly MainWindowViewModel _viewModel;

	public MainWindowViewModelTests()
	{
		_viewModel = new MainWindowViewModel(_messagePanel, new AppStatusBarViewModel(), _logLevelControl);
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
	public async Task ADebugEventReachesTheSinkOnlyWhileVerbose()
	{
		var sink = new CollectingSink();
		using var logger = new LoggerConfiguration()
			.MinimumLevel.ControlledBy(_logLevelControl.Switch)
			.WriteTo.Sink(sink)
			.CreateLogger();

		logger.Debug("before");
		await _viewModel.ToggleVerboseLoggingCommand.Execute();
		logger.Debug("verbose");
		await _viewModel.ToggleVerboseLoggingCommand.Execute();
		logger.Debug("after");

		sink.Messages.Should().Equal("verbose");
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
		_messagePanel.Dispose();
	}

	private sealed class CollectingSink : ILogEventSink
	{
		private readonly List<string> _messages = [];

		public IReadOnlyList<string> Messages => _messages;

		public void Emit(LogEvent logEvent)
		{
			_messages.Add(logEvent.MessageTemplate.Text);
		}
	}
}
