using System.Reactive.Concurrency;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ReactiveUI.Avalonia;

using Serilog;

using SpectrumVisualizer.Core;
using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Logging;
using SpectrumVisualizer.UI.MainWindow;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Spectrum;
using SpectrumVisualizer.UI.Store;

namespace SpectrumVisualizer.UI;

public static class UiServiceCollectionExtensions
{
	public static IServiceCollection AddUi(this IServiceCollection services, LoggingSetup logging, AppDataPaths paths)
	{
		// Program owns the Serilog logger and flushes it after the container is gone.
		services.AddLogging(builder => builder.AddSerilog(logging.Logger, dispose: false));
		services.AddSingleton(logging.LevelControl);
		services.AddSingleton(TimeProvider.System);
		services.AddSingleton<IScheduler>(AvaloniaScheduler.Instance);
		services.AddSingleton<ISerialPortProvider, RjcpSerialPortProvider>();
		services.AddSingleton(provider => new SerialLink(
			provider.GetRequiredService<ISerialPortProvider>(),
			DefaultScheduler.Instance,
			provider.GetRequiredService<ILogger<SerialLink>>()));
		services.AddSingleton(provider => new FrameDistributor(
			provider.GetRequiredService<SerialLink>().Frames,
			provider.GetRequiredService<MessagePanelViewModel>(),
			provider.GetRequiredService<IScheduler>(),
			provider.GetRequiredService<ILogger<FrameDistributor>>()));
		services.AddSingleton(new StoreFile(StoreFiles.InstanceFile(paths.StoreFolder, Environment.ProcessId)));
		services.AddSingleton<SpectrumStore>();
		services.AddSingleton<LiveSpectrumSource>();
		services.AddSingleton(provider =>
			new SwitchingSpectrumSource(provider.GetRequiredService<LiveSpectrumSource>()));
		services.AddSingleton<IDisplayedSpectrumSource>(provider =>
			provider.GetRequiredService<SwitchingSpectrumSource>());
		services.AddSingleton<MessagePanelViewModel>();
		services.AddSingleton<ConnectionBarViewModel>();
		services.AddSingleton<AppStatusBarViewModel>();
		services.AddSingleton<SpectrumViewModel>();
		services.AddSingleton<MainWindowViewModel>();

		return services;
	}
}
