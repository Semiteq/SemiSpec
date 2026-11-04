using System.Globalization;
using System.Reactive.Concurrency;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ReactiveUI.Avalonia;

using Semi.Avalonia;

using SpectrumVisualizer.UI.MainWindow;
using SpectrumVisualizer.UI.Messages;

namespace SpectrumVisualizer.UI;

public class App : Application
{
	private const string RussianLanguage = "ru";

	private MainWindowViewModel? _mainWindowViewModel;

	// Written once on the UI thread in Configure; read by UnhandledErrorObserver on whichever thread
	// ReactiveUI raises the exception, so the write is published with volatile.
	private volatile MessagePanelViewModel? _messagePanel;

	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);

		SemiTheme.OverrideLocaleResources(this, SemiLocaleFor(CultureInfo.CurrentUICulture));
	}

	/// <summary>Semi keys its own strings by specific culture and falls back to zh-CN for any other.</summary>
	internal static CultureInfo SemiLocaleFor(CultureInfo uiCulture)
	{
		return uiCulture.TwoLetterISOLanguageName == RussianLanguage
			? CultureInfo.GetCultureInfo("ru-RU")
			: CultureInfo.GetCultureInfo("en-US");
	}

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.MainWindow = CreateMainWindow();
		}

		base.OnFrameworkInitializationCompleted();
	}

	internal Window CreateMainWindow()
	{
		if (_mainWindowViewModel is not { } viewModel)
		{
			throw new InvalidOperationException(
				"The main window view model is not set. Call Run() before starting the app.");
		}

		return new MainWindow.MainWindow { DataContext = viewModel };
	}

	/// <summary>Runs the app until its window closes; the logger receives what ReactiveUI or a dispatcher job raises unhandled.</summary>
	public static void Run(IServiceProvider services, ILogger unhandledErrorLogger)
	{
		// Process lifetime by design: ReactiveUI takes its exception handler once, before any window exists,
		// and the dispatcher subscription below is made once and never removed.
		var unhandledErrors = new UnhandledErrorObserver(
			ResolveMessagePanel,
			services.GetRequiredService<IScheduler>(),
			unhandledErrorLogger);

		BuildAvaloniaApp(unhandledErrors)
			.AfterSetup(builder =>
			{
				// Dispatcher.UIThread exists only once the platform is set up.
				Dispatcher.UIThread.UnhandledException += unhandledErrors.OnDispatcherUnhandledException;
				((App)builder.Instance!).Configure(services);
			})
			.StartWithClassicDesktopLifetime([]);
	}

	internal void Configure(IServiceProvider services)
	{
		_mainWindowViewModel = services.GetRequiredService<MainWindowViewModel>();
		_messagePanel = services.GetRequiredService<MessagePanelViewModel>();
	}

	internal static AppBuilder BuildAvaloniaApp(IObserver<Exception> unhandledErrors)
	{
		return AppBuilder.Configure<App>()
			.UseWin32()
			.UseSkia()
			// Avalonia 12: Skia no longer brings a text shaper with it. Without UseHarfBuzz the desktop
			// application fails at AppBuilder.Setup with "No text shaping system configured".
			.UseHarfBuzz()
			.UseReactiveUI(builder => builder.WithExceptionHandler(unhandledErrors))
			.LogToTrace();
	}

	/// <summary>The panel of the configured app; before Configure a failure is only logged.</summary>
	internal static MessagePanelViewModel? ResolveMessagePanel()
	{
		return (Current as App)?._messagePanel;
	}
}
