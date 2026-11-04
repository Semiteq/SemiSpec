using System.Reactive.Concurrency;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ReactiveUI.Avalonia;

using Serilog;

using SpectrumVisualizer.Core;
using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Logging;
using SpectrumVisualizer.UI.MainWindow;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Spectrum;
using SpectrumVisualizer.UI.Store;
using SpectrumVisualizer.UI.Trend;

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
		services.AddSingleton(provider => new SpectrumHistory(provider.GetRequiredService<StoreFile>().Path));
		services.AddSingleton<IntensityCache>();
		services.AddSingleton<LiveSpectrumSource>();
		services.AddSingleton(provider =>
			new SwitchingSpectrumSource(provider.GetRequiredService<LiveSpectrumSource>()));
		services.AddSingleton<MessagePanelViewModel>();
		services.AddSingleton<ConnectionBarViewModel>();
		services.AddSingleton<AppStatusBarViewModel>();
		services.AddSingleton<SpectrumViewModel>();
		services.AddSingleton<TrendChartViewModel>();
		services.AddSingleton<OverviewViewModel>();
		services.AddSingleton(provider =>
		{
			var history = provider.GetRequiredService<SpectrumHistory>();
			return new TrendFeed(
				provider.GetRequiredService<FrameDistributor>(),
				provider.GetRequiredService<IntensityCache>(),
				provider.GetRequiredService<TrendChartViewModel>(),
				provider.GetRequiredService<SwitchingSpectrumSource>(),
				history.IntensityPointsAsync,
				history.SpectrumAtAsync,
				provider.GetRequiredService<MessagePanelViewModel>(),
				provider.GetRequiredService<IScheduler>(),
				DefaultScheduler.Instance,
				provider.GetRequiredService<ILogger<TrendFeed>>());
		});
		services.AddSingleton<TrendViewModel>();
		services.AddSingleton<MainWindowViewModel>();

		return services;
	}
}
