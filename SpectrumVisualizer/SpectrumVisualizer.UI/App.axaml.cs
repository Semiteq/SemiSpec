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

using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.MainWindow;
using SpectrumVisualizer.UI.Messages;

namespace SpectrumVisualizer.UI;

public class App : Application
{
	private const string RussianLanguage = "ru";

	private static readonly TimeSpan _backgroundStopLimit = TimeSpan.FromSeconds(5);

	// Started by the first close of the main window; a later close passes once the stop completes.
	private Task? _backgroundStop;

	// Written once on the UI thread in Configure; read by UnhandledErrorObserver on whichever thread
	// ReactiveUI raises the exception, so the write is published with volatile.
	private volatile ConfiguredServices? _services;

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
		if (_services is not { } services)
		{
			throw new InvalidOperationException(
				"The app is not configured. Call Run() before starting the app.");
		}

		var window = new MainWindow.MainWindow { DataContext = services.MainWindow };
		window.Opened += (_, _) =>
		{
			services.MainWindow.ConnectionBar.RefreshPorts();
			services.FrameDistributor.Start();
		};
		window.Closing += (sender, e) => OnMainWindowClosing(services, sender, e);

		return window;
	}

	/// <summary>Holds an operator close until the link and the frame distributor have stopped, at most for the stop limit.</summary>
	private async void OnMainWindowClosing(ConfiguredServices services, object? sender, WindowClosingEventArgs e)
	{
		try
		{
			if (_backgroundStop is not null)
			{
				e.Cancel = !_backgroundStop.IsCompleted;
				return;
			}

			_backgroundStop = StopBackgroundServicesAsync(services);

			// Services that never ran stop at once; re-closing from inside Closing would re-enter Close.
			if (_backgroundStop.IsCompleted || sender is not Window window)
			{
				return;
			}

			e.Cancel = true;
			await _backgroundStop;
			window.Close();
		}
		catch (Exception exception)
		{
			services.Logger.LogError(exception, "Stopping the link and the frame distributor on window close failed");
		}
	}

	private static async Task StopBackgroundServicesAsync(ConfiguredServices services)
	{
		try
		{
			await StopLinkThenDistributorAsync(services).WaitAsync(_backgroundStopLimit);
		}
		catch (TimeoutException)
		{
			services.Logger.LogWarning(
				"The link and the frame distributor did not stop within {Limit}; the window closes anyway",
				_backgroundStopLimit);
		}
	}

	/// <summary>The link stops first, so no frame enters the channel after the distributor has stopped reading it.</summary>
	private static async Task StopLinkThenDistributorAsync(ConfiguredServices services)
	{
		await services.SerialLink.DisconnectAsync();
		await services.FrameDistributor.StopAsync();
	}

	/// <summary>
	/// Runs the app until its window closes; the logger receives what ReactiveUI or a dispatcher job raises unhandled.
	/// </summary>
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
		_services = new ConfiguredServices(
			services.GetRequiredService<MainWindowViewModel>(),
			services.GetRequiredService<MessagePanelViewModel>(),
			services.GetRequiredService<SerialLink>(),
			services.GetRequiredService<FrameDistributor>(),
			services.GetRequiredService<ILogger<App>>());
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
		return (Current as App)?._services?.MessagePanel;
	}

	private sealed record ConfiguredServices(
		MainWindowViewModel MainWindow,
		MessagePanelViewModel MessagePanel,
		SerialLink SerialLink,
		FrameDistributor FrameDistributor,
		ILogger<App> Logger);
}
