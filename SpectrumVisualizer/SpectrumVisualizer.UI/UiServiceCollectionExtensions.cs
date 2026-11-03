using Microsoft.Extensions.DependencyInjection;

using Serilog;

using SpectrumVisualizer.UI.Logging;
using SpectrumVisualizer.UI.MainWindow;
using SpectrumVisualizer.UI.Messages;

namespace SpectrumVisualizer.UI;

public static class UiServiceCollectionExtensions
{
	public static IServiceCollection AddUi(this IServiceCollection services, LoggingSetup logging)
	{
		// Program owns the Serilog logger and flushes it after the container is gone.
		services.AddLogging(builder => builder.AddSerilog(logging.Logger, dispose: false));
		services.AddSingleton(logging.LevelControl);
		services.AddSingleton(TimeProvider.System);
		services.AddSingleton<MessagePanelViewModel>();
		services.AddSingleton<AppStatusBarViewModel>();
		services.AddSingleton<MainWindowViewModel>();

		return services;
	}
}
