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
	public void TheMainWindowViewModelSharesTheContainersPanelAndLogLevelControl()
	{
		using var provider = BuildContainer();

		var viewModel = provider.GetRequiredService<MainWindowViewModel>();

		viewModel.MessagePanel.Should().BeSameAs(provider.GetRequiredService<MessagePanelViewModel>());
		provider.GetRequiredService<LogLevelControl>().Should().BeSameAs(_logLevelControl);
	}

	[AvaloniaFact]
	public void TheAppBuildsTheMainWindowFromTheProviderAndExposesItsPanel()
	{
		using var provider = BuildContainer();
		var app = (App)Application.Current!;

		app.Configure(provider);
		var window = app.CreateMainWindow();
		window.Show();
		Dispatcher.UIThread.RunJobs();

		window.DataContext.Should().BeSameAs(provider.GetRequiredService<MainWindowViewModel>());
		App.ResolveMessagePanel().Should().BeSameAs(provider.GetRequiredService<MessagePanelViewModel>());

		window.Close();
		Dispatcher.UIThread.RunJobs();

		App.ResolveMessagePanel().Should().BeNull("a closed window carries no panel");
	}

	private ServiceProvider BuildContainer()
	{
		return new ServiceCollection().AddUi(_logLevelControl).BuildServiceProvider();
	}
}
