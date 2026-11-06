using System.Reactive.Concurrency;
using System.Reactive.Linq;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using ReactiveUI.Avalonia;

using SemiSpec.Core;
using SemiSpec.Core.Store;
using SemiSpec.Core.Trend;
using SemiSpec.Tests.Unit.UI.Logging;
using SemiSpec.UI;
using SemiSpec.UI.Link;
using SemiSpec.UI.Logging;
using SemiSpec.UI.MainWindow;
using SemiSpec.UI.Messages;
using SemiSpec.UI.Spectrum;
using SemiSpec.UI.Store;
using SemiSpec.UI.Trend;

using Serilog;
using Serilog.Events;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Di;

/// <summary>The container Program builds through AddUi, not a look-alike assembled here.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class CompositionRootTests : IAsyncDisposable
{
	private static readonly TimeSpan _storeStopLimit = TimeSpan.FromSeconds(10);

	private readonly LogLevelControl _logLevelControl = new();
	private readonly CollectingSink _sink = new();
	private readonly LoggingSetup _logging;
	// Every test in the process gets the same instance file name, so each test owns its root.
	private readonly TempFolder _root = new();
	private readonly AppDataPaths _paths;
	private readonly List<SpectrumStore> _stores = [];

	public CompositionRootTests()
	{
		var logger = new LoggerConfiguration()
			.MinimumLevel.ControlledBy(_logLevelControl.Switch)
			.WriteTo.Sink(_sink)
			.CreateLogger();
		_logging = new LoggingSetup(logger, _logLevelControl, _root.Root);
		_paths = AppDataPaths.Under(_root.Root).Value;
	}

	[AvaloniaFact]
	public void EveryRegisteredServiceResolves()
	{
		var services = new ServiceCollection().AddUi(_logging, _paths);
		using var provider = services.BuildServiceProvider();

		foreach (var descriptor in services.Where(descriptor => !descriptor.ServiceType.IsGenericTypeDefinition))
		{
			provider.GetRequiredService(descriptor.ServiceType).Should().NotBeNull(descriptor.ServiceType.Name);
		}
	}

	[AvaloniaFact]
	public void TheSchedulerIsTheUiScheduler()
	{
		using var provider = BuildContainer();

		provider.GetRequiredService<IScheduler>().Should().BeSameAs(AvaloniaScheduler.Instance);
	}

	[AvaloniaFact]
	public void TheLinkIsOneSingletonOverTheRjcpPortProvider()
	{
		using var provider = BuildContainer();

		provider.GetRequiredService<ISerialPortProvider>().Should().BeOfType<RjcpSerialPortProvider>();
		provider.GetRequiredService<SerialLink>().Should().BeSameAs(provider.GetRequiredService<SerialLink>());
	}

	[AvaloniaFact]
	public void TheDistributorAndTheSpectrumSourcesAreOneInstanceEach()
	{
		using var provider = BuildContainer();

		provider.GetRequiredService<FrameDistributor>()
			.Should().BeSameAs(provider.GetRequiredService<FrameDistributor>());
		provider.GetRequiredService<LiveSpectrumSource>()
			.Should().BeSameAs(provider.GetRequiredService<LiveSpectrumSource>());
		provider.GetRequiredService<SwitchingSpectrumSource>()
			.Should().BeSameAs(provider.GetRequiredService<SwitchingSpectrumSource>());
	}

	[AvaloniaFact]
	public void TheStoreIsOneInstanceOverTheHistoryFileUnderTheStoreFolder()
	{
		using var provider = BuildContainer();

		provider.GetRequiredService<SpectrumStore>().Should().BeSameAs(provider.GetRequiredService<SpectrumStore>());
		provider.GetRequiredService<StoreFile>().Path.Should().Be(Path.Combine(_paths.StoreFolder, "spectra.db"));
	}

	[AvaloniaFact]
	public void TheTrendPartsAreOneInstanceEach()
	{
		using var provider = BuildContainer();

		ShouldBeOneInstance<IntensityCache>(provider);
		ShouldBeOneInstance<SpectrumHistory>(provider);
		ShouldBeOneInstance<TrendFeed>(provider);
		ShouldBeOneInstance<TrendChartViewModel>(provider);
		ShouldBeOneInstance<OverviewViewModel>(provider);
		ShouldBeOneInstance<TrendViewModel>(provider);
	}

	[AvaloniaFact]
	public void TheTrendViewModelSharesTheContainersChartAndOverview()
	{
		using var provider = BuildContainer();

		var trend = provider.GetRequiredService<TrendViewModel>();

		trend.Chart.Should().BeSameAs(provider.GetRequiredService<TrendChartViewModel>());
		trend.Overview.Should().BeSameAs(provider.GetRequiredService<OverviewViewModel>());
	}

	[AvaloniaFact]
	public void TheMainWindowShowsTheContainersTrendViewModel()
	{
		using var provider = BuildContainer();

		var trend = provider.GetRequiredService<MainWindowViewModel>().Trend;

		trend.Should().BeSameAs(provider.GetRequiredService<TrendViewModel>());
	}

	[AvaloniaFact]
	public void TheMainWindowShowsTheContainersStatusBarSeededFromTheLink()
	{
		using var provider = BuildContainer();

		var statusBar = provider.GetRequiredService<MainWindowViewModel>().StatusBar;

		statusBar.Should().BeSameAs(provider.GetRequiredService<AppStatusBarViewModel>());
		statusBar.Status.Should().BeSameAs(provider.GetRequiredService<SerialLink>().Status);
	}

	[AvaloniaFact]
	public void TheMainWindowShowsTheContainersSpectrumViewModel()
	{
		using var provider = BuildContainer();

		var spectrum = provider.GetRequiredService<MainWindowViewModel>().Spectrum;

		spectrum.Should().BeSameAs(provider.GetRequiredService<SpectrumViewModel>());
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

	private static void ShouldBeOneInstance<T>(IServiceProvider provider)
		where T : notnull
	{
		provider.GetRequiredService<T>().Should().BeSameAs(provider.GetRequiredService<T>(), typeof(T).Name);
	}

	private ServiceProvider BuildContainer()
	{
		var provider = new ServiceCollection().AddUi(_logging, _paths).BuildServiceProvider();
		_stores.Add(provider.GetRequiredService<SpectrumStore>());

		return provider;
	}

	public async ValueTask DisposeAsync()
	{
		// Showing the window starts the store; a writer still open would hold the instance file and fail the delete.
		foreach (var store in _stores)
		{
			await store.StopAsync().WaitAsync(_storeStopLimit);
		}

		_root.Dispose();
		_logging.Logger.Dispose();
	}
}
