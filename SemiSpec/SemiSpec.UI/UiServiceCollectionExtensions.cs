using System.Reactive.Concurrency;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ReactiveUI.Avalonia;

using SemiSpec.Core;
using SemiSpec.Core.Store;
using SemiSpec.Core.Trend;
using SemiSpec.UI.Link;
using SemiSpec.UI.Logging;
using SemiSpec.UI.MainWindow;
using SemiSpec.UI.Messages;
using SemiSpec.UI.Spectrum;
using SemiSpec.UI.Store;
using SemiSpec.UI.Trend;

using Serilog;

namespace SemiSpec.UI;

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
