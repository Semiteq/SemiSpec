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

	private static readonly TimeSpan _linkStopLimit = TimeSpan.FromSeconds(5);

	private MainWindowViewModel? _mainWindowViewModel;
	private SerialLink? _serialLink;
	private ILogger<App>? _logger;

	// Started by the first close of the main window; a later close passes once the stop completes.
	private Task? _linkStop;

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

		var window = new MainWindow.MainWindow { DataContext = viewModel };
		window.Opened += (_, _) => viewModel.ConnectionBar.RefreshPorts();
		window.Closing += OnMainWindowClosing;

		return window;
	}

	/// <summary>Holds an operator close until the link has stopped, at most for the stop limit.</summary>
	private async void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
	{
		try
		{
			if (_linkStop is not null)
			{
				e.Cancel = !_linkStop.IsCompleted;
				return;
			}

			_linkStop = StopLinkAsync();

			// An idle link stops at once; re-closing from inside Closing would re-enter Close.
			if (_linkStop.IsCompleted || sender is not Window window)
			{
				return;
			}

			e.Cancel = true;
			await _linkStop;
			window.Close();
		}
		catch (Exception exception)
		{
			_logger?.LogError(exception, "Stopping the link on window close failed");
		}
	}

	private async Task StopLinkAsync()
	{
		if (_serialLink is null)
		{
			return;
		}

		try
		{
			await _serialLink.DisconnectAsync().WaitAsync(_linkStopLimit);
		}
		catch (TimeoutException)
		{
			_logger?.LogWarning("The link did not stop within {Limit}; the window closes anyway", _linkStopLimit);
		}
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
		_serialLink = services.GetRequiredService<SerialLink>();
		_logger = services.GetRequiredService<ILogger<App>>();
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
