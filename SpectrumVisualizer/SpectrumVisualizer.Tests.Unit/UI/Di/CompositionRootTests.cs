using System.Reactive.Linq;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using Serilog;
using Serilog.Events;

using SpectrumVisualizer.Tests.Unit.UI.Logging;

using SpectrumVisualizer.UI;
using SpectrumVisualizer.UI.Logging;
using SpectrumVisualizer.UI.MainWindow;
using SpectrumVisualizer.UI.Messages;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Di;

/// <summary>The container Program builds through AddUi, not a look-alike assembled here.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class CompositionRootTests : IDisposable
{
	private readonly LogLevelControl _logLevelControl = new();
	private readonly CollectingSink _sink = new();
	private readonly LoggingSetup _logging;

	public CompositionRootTests()
	{
		var logger = new LoggerConfiguration()
			.MinimumLevel.ControlledBy(_logLevelControl.Switch)
			.WriteTo.Sink(_sink)
			.CreateLogger();
		_logging = new LoggingSetup(logger, _logLevelControl);
	}

	[AvaloniaFact]
	public void EveryRegisteredServiceResolves()
	{
		var services = new ServiceCollection().AddUi(_logging);
		using var provider = services.BuildServiceProvider();

		foreach (var descriptor in services.Where(descriptor => !descriptor.ServiceType.IsGenericTypeDefinition))
		{
			provider.GetRequiredService(descriptor.ServiceType).Should().NotBeNull(descriptor.ServiceType.Name);
		}
	}

	[AvaloniaFact]
	public async Task TheMainWindowViewModelSharesTheContainersPanelAndLogLevelControl()
	{
		using var provider = BuildContainer();

		var viewModel = provider.GetRequiredService<MainWindowViewModel>();
		await viewModel.ToggleVerboseLoggingCommand.Execute();

		viewModel.MessagePanel.Should().BeSameAs(provider.GetRequiredService<MessagePanelViewModel>());
		_logLevelControl.IsVerbose.Should().BeTrue("the command writes the control the container was given");
	}

	[AvaloniaFact]
	public async Task TheVerboseToggleWritesThroughTheApplicationLoggerAtBothLevels()
	{
		using var provider = BuildContainer();

		await provider.GetRequiredService<MainWindowViewModel>().ToggleVerboseLoggingCommand.Execute();

		_sink.Events.Select(logEvent => logEvent.Level)
			.Should().Equal(LogEventLevel.Information, LogEventLevel.Debug);
	}

	[AvaloniaFact]
	public void TheAppBuildsTheMainWindowFromTheProvider()
	{
		using var provider = BuildContainer();
		var app = new App();

		app.Configure(provider);
		var window = app.CreateMainWindow();

		try
		{
			window.Show();
			Dispatcher.UIThread.RunJobs();

			window.DataContext.Should().BeSameAs(provider.GetRequiredService<MainWindowViewModel>());
		}
		finally
		{
			window.Close();
			Dispatcher.UIThread.RunJobs();
		}
	}

	[AvaloniaFact]
	public void AnAppThatWasNeverConfiguredRefusesToBuildTheMainWindow()
	{
		var createMainWindow = () => new App().CreateMainWindow();

		createMainWindow.Should().Throw<InvalidOperationException>();
	}

	private ServiceProvider BuildContainer()
	{
		return new ServiceCollection().AddUi(_logging).BuildServiceProvider();
	}

	public void Dispose()
	{
		_logging.Logger.Dispose();
	}
}
