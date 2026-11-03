using Microsoft.Extensions.DependencyInjection;

using SpectrumVisualizer.UI.Logging;
using SpectrumVisualizer.UI.MainWindow;
using SpectrumVisualizer.UI.Messages;

namespace SpectrumVisualizer.UI;

public static class UiServiceCollectionExtensions
{
	public static IServiceCollection AddUi(this IServiceCollection services, LogLevelControl logLevelControl)
	{
		services.AddSingleton(logLevelControl);
		services.AddSingleton(TimeProvider.System);
		services.AddSingleton<MessagePanelViewModel>();
		services.AddSingleton<AppStatusBarViewModel>();
		services.AddSingleton<MainWindowViewModel>();

		return services;
	}
}
