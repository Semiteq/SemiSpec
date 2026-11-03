using Microsoft.Extensions.DependencyInjection;

using SpectrumVisualizer.UI.Logging;

namespace SpectrumVisualizer.UI;

public static class UiServiceCollectionExtensions
{
	public static IServiceCollection AddUi(this IServiceCollection services, LogLevelControl logLevelControl)
	{
		services.AddSingleton(logLevelControl);

		return services;
	}
}
