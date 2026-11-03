using System.Reactive.Linq;

using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

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
public sealed class CompositionRootTests
{
	private readonly LogLevelControl _logLevelControl = new();

	[AvaloniaFact]
	public void EveryRegisteredServiceResolves()
	{
		var services = new ServiceCollection().AddUi(_logLevelControl);
		using var provider = services.BuildServiceProvider();

		foreach (var descriptor in services)
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
	public void TheAppBuildsTheMainWindowFromTheProviderAndExposesItsPanel()
	{
		using var provider = BuildContainer();
		var app = (App)Application.Current!;

		app.Configure(provider);
		var window = app.CreateMainWindow();

		try
		{
			window.Show();
			Dispatcher.UIThread.RunJobs();

			window.DataContext.Should().BeSameAs(provider.GetRequiredService<MainWindowViewModel>());
			App.ResolveMessagePanel().Should().BeSameAs(provider.GetRequiredService<MessagePanelViewModel>());
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
		return new ServiceCollection().AddUi(_logLevelControl).BuildServiceProvider();
	}
}
