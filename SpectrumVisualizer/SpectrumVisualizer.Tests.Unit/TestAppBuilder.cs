using Avalonia;
using Avalonia.Headless;

using ReactiveUI.Avalonia;

using SpectrumVisualizer.Tests.Unit;

using App = SpectrumVisualizer.UI.App;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace SpectrumVisualizer.Tests.Unit;

public static class TestAppBuilder
{
	public static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>()
			.UseHeadless(new AvaloniaHeadlessPlatformOptions())
			.UseReactiveUI(_ => { });
	}
}
