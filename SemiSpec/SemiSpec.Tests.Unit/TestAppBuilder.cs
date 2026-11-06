using Avalonia;
using Avalonia.Headless;

using ReactiveUI.Avalonia;

using SemiSpec.Tests.Unit;

using App = SemiSpec.UI.App;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace SemiSpec.Tests.Unit;

public static class TestAppBuilder
{
	public static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>()
			.UseHeadless(new AvaloniaHeadlessPlatformOptions())
			.UseReactiveUI(_ => { });
	}
}
