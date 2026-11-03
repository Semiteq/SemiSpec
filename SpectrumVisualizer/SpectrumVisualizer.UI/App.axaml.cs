using System.Globalization;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using ReactiveUI.Avalonia;

using Semi.Avalonia;

namespace SpectrumVisualizer.UI;

public class App : Application
{
	private const string RussianLanguage = "ru";

	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);

		SemiTheme.OverrideLocaleResources(this, SemiLocaleFor(CultureInfo.CurrentUICulture));
	}

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.MainWindow = new MainWindow.MainWindow();
		}

		base.OnFrameworkInitializationCompleted();
	}

	public static void Run()
	{
		BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
	}

	/// <summary>Semi keys its own strings by specific culture and falls back to zh-CN for any other.</summary>
	internal static CultureInfo SemiLocaleFor(CultureInfo uiCulture)
	{
		return uiCulture.TwoLetterISOLanguageName == RussianLanguage
			? CultureInfo.GetCultureInfo("ru-RU")
			: CultureInfo.GetCultureInfo("en-US");
	}

	internal static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>()
			.UseWin32()
			.UseSkia()
			// Avalonia 12: Skia no longer brings a text shaper with it. Without UseHarfBuzz the desktop
			// application fails at AppBuilder.Setup with "No text shaping system configured".
			.UseHarfBuzz()
			.UseReactiveUI(_ => { })
			.LogToTrace();
	}
}
