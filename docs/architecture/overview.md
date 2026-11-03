# Overview

## Purpose

SpectrumVisualizer shows the operator the spectrum the spectrometer board sends over a serial port. The code under
`SpectrumVisualizer/` is the application shell: it starts, logs to a fixed folder, shows the main window, and routes
reported failures and ReactiveUI's unhandled exceptions to the message panel. Nothing in it reads a port or the board; the content area shows a placeholder
text and the status bar shows a fixed "Not connected".

## Stack

- .NET 10, C# 14, Windows (`net10.0`, published win-x64 self-contained).
- Avalonia 12.0.5 with Win32, Skia and HarfBuzz; Semi.Avalonia 12.0.3; ReactiveUI.Avalonia 12.0.3; System.Reactive 6.1.0.
- Microsoft.Extensions.DependencyInjection for the composition root.
- Serilog with the file sink, bridged to `Microsoft.Extensions.Logging` through Serilog.Extensions.Logging.
- FluentResults 4.0.0 for failures returned as values.
- Microsoft.CodeAnalysis.ResxSourceGenerator for the text accessor (`ui-text.md`).

`SpectrumVisualizer/Directory.Packages.props` holds every version.

## Components

| Component | Project | Responsibility |
| --- | --- | --- |
| `AppDataPaths` | Core | Resolves the log folder `SpectrumVisualizer\Logs` under the local app data root; fails on a root that is not fully qualified |
| `AppLogging` | UI | Creates the log folder and the Serilog logger: `spectrum-visualizer.log`, 5 MB per file, 5 files kept, shared between processes, invariant culture, process id in every line |
| `LogLevelControl` | UI | Wraps the logger's `LoggingLevelSwitch`; `IsVerbose` moves it between Information and Debug |
| `AddUi(LogLevelControl)` | UI | The composition root: registers the log level control, `TimeProvider.System`, the message panel, the status bar and the main window view model, all singletons |
| `Program` | UI | Entry point: paths, logging, container, `App.Run` with the logger for unhandled exceptions |
| `App` | UI | Builds the Avalonia app with the exception handler, sets Semi's control strings to the UI culture, creates the main window from the container |
| `MainWindowViewModel` | UI | Exit request and the verbose-logging command |
| `AppStatusBarViewModel` | UI | The status bar text |
| `MessagePanelViewModel` | UI | The bounded, newest-first list of messages the operator reads |
| `ResultReporting`, `ErrorMessageMapper` | UI | The route from a failed `Result` to the panel and the log |
| `UnhandledErrorObserver` | UI | ReactiveUI's last-resort exception handler |

## Startup

`Program.Main` runs these steps in order:

1. `AppDataPaths.Under` resolves the log folder from `Environment.SpecialFolder.LocalApplicationData`.
2. `AppLogging.Create` creates `%LOCALAPPDATA%\SpectrumVisualizer\Logs` and the logger, and sets `Log.Logger`.
3. `AddUi` builds the service provider.
4. `App.Run` receives a logger from a `SerilogLoggerFactory` over that logger, builds the `UnhandledErrorObserver` and
   the Avalonia app; in `AfterSetup`, `App.Configure` resolves `MainWindowViewModel` and `MessagePanelViewModel`, and
   `OnFrameworkInitializationCompleted` shows `MainWindow` with that view model as its `DataContext`.

A failure in step 1 or 2 ends with exit code 1 and shows nothing, because there is no log to write to. A failure after
the logger exists is logged as Fatal and ends with exit code 1.

No ReactiveUI object is constructed before `AppBuilder.Setup()`. ReactiveUI takes its exception handler once, on first
use, so a command or property helper built earlier would make the handler that `BuildAvaloniaApp` installs a silent
no-op. The view models come from the container inside `AfterSetup` for this reason.

## Main window

`SpectrumVisualizer/SpectrumVisualizer.UI/MainWindow/MainWindow.axaml` has four rows, top to bottom:

| Row | Height | Content |
| --- | --- | --- |
| Menu | `Auto` | `AppMenuBar`: File, Exit; View, Verbose logging |
| Content | `*` | Placeholder text |
| Message panel | `Auto` | `MessagePanelView`; the row is hidden while the list is empty |
| Status bar | `Auto` | `AppStatusBar` with the connection text |

The window is 1280 by 800 and opens centred. Exit is view work: the view model emits on `ExitRequests`, and the window
subscribes in `OnLoaded`, unsubscribes in `OnUnloaded`, and closes itself.

The Verbose logging item reads `IsVerboseLogging` one-way and writes only through `ToggleVerboseLoggingCommand`, so the
command is the level's single writer. `LogLevelControl` raises no notification; the command raises
`IsVerboseLogging` after it flips the switch. The level resets to Information at every start.

## Error route

Every failure the operator should see goes to `MessagePanelViewModel` through one of the two `ResultReporting`
extensions:

- `ReportFailure(IError, ILogger)` maps the error with `ErrorMessageMapper`, adds it to the panel, and writes one log
  line at Error. The log line is written even when the panel throws.
- `TryReportFailure(IError, ILogger, IScheduler)` moves the report to the UI scheduler and never throws there, for
  callers whose exception would end an Rx stream or a dispatcher job.

`ErrorMessageMapper.Map` turns an `IError` into a `MessageView` (title, detail, remedy). An exception error keeps the
exception type and message in the detail; any other error keeps its own message. So two different failures stay two
rows. Every entry is an error and carries the error dot; a severity scale arrives with the first failure that recovers
by itself.

The panel holds at most 200 entries, newest first. A message equal to the newest entry does not add a row: it raises
that entry's repeat count and last-seen time, and the log line drops to Debug. The panel row is visible while the list
has entries: the first entry opens it, Clear empties the list and hides it. An entry with an empty remedy shows no
remedy line.

`UnhandledErrorObserver` is the handler `BuildAvaloniaApp` passes to `UseReactiveUI(... WithExceptionHandler ...)`.
`App.Run` builds it once with the logger `Program` hands over, and it lives for the process, because ReactiveUI takes
the handler once, before any window exists. On each exception it reads the panel `App.Configure` resolved through
`App.ResolveMessagePanel`; before `Configure` it only logs.

An exception that escapes a dispatcher job outside ReactiveUI, for example in a code-behind event handler, is not
routed to the panel. It ends the dispatcher loop, `App.Run` throws, and `Program` logs it as Fatal and exits with
code 1.
