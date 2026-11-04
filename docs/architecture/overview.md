# Overview

## Purpose

SpectrumVisualizer shows the operator the spectrum the spectrometer board sends over a serial port. The code under
`SpectrumVisualizer/` starts, logs to a fixed folder and shows the main window. The operator picks a COM port in the
connection row and connects; the serial link reads the board's frames, reopens the port after an unplug, and the
status bar shows the link state, both spectrometers' bits and the spectra lost since Connect. Link faults, reported
failures, ReactiveUI's unhandled exceptions and exceptions that escape a dispatcher job reach the message panel. No
code reads the decoded frames: the content area shows a placeholder text and there is no chart. A console tool,
the board emulator, writes protocol frames to a serial port for manual runs.

## Stack

- .NET 10, C# 14, Windows (`net10.0`, published win-x64 self-contained).
- Avalonia 12.0.5 with Win32, Skia and HarfBuzz; Semi.Avalonia 12.0.3; ReactiveUI.Avalonia 12.0.3; System.Reactive 6.1.0.
- Microsoft.Extensions.DependencyInjection for the composition root.
- Serilog with the file sink, bridged to `Microsoft.Extensions.Logging` through Serilog.Extensions.Logging.
- FluentResults 4.0.0 for failures returned as values.
- Microsoft.CodeAnalysis.ResxSourceGenerator for the text accessor (`ui-text.md`).
- System.IO.Pipelines and System.Threading.Channels from the shared framework for the frame reader and the link.
- RJCP.SerialPortStream 3.0.5 for the serial port, in the UI and the board emulator.
- The board emulator only: System.CommandLine 2.0.2.

`SpectrumVisualizer/Directory.Packages.props` holds every version.

## Components

| Component | Project | Responsibility |
| --- | --- | --- |
| `AppDataPaths` | Core | Resolves the log folder `SpectrumVisualizer\Logs` under the local app data root; fails on a root that is not fully qualified |
| `Crc16Modbus` | Core | CRC-16/MODBUS of the frame, over a span |
| `BoardFrame`, `SpectrumMeasurement` | Core | One decoded frame: state, `seq` and the measurement of a spectrum frame; the measurement rejects a pixel count that does not match its device |
| `Spectrometer` | Core | The two devices, their pixel count and the size of their spectrum frame |
| `FrameEncoder` | Core | The bytes of a frame; used by the emulator and the tests |
| `FrameDecoder` | Core | Finds and validates frames in a byte sequence; drops one byte per failed candidate |
| `FrameReader` | Core | Yields the frames of a `PipeReader`, or decodes what is buffered without a wait, and counts the dropped bytes |
| `SequenceTracker` | Core | Counts the spectra lost between frames from `seq` |
| `SpectrometerCalibration` | Core | Per device: the wavelength of each pixel, the pixel for a wavelength, the pixel window for a wavelength and `delta` |
| `FrameSchedule`, `SpectrumGenerator` | Tools.BoardEmulator | Which frame the emulated board sends at a tick and its `state`, and the synthetic and dark spectra |
| `EmulatorCommand` | Tools.BoardEmulator | Parses and range-checks the command line into an `EmulatorOptions` record; rejects `--failed` for the other spectrometer without `--both-connected` |
| `Program` (emulator) | Tools.BoardEmulator | Opens the port, checks every 10 ms which frame is due and writes it, until Ctrl+C |
| `ISerialPortProvider`, `RjcpSerialPortProvider` | UI | Lists the COM ports and opens one for the link, mapping open failures to typed errors |
| `SerialLink` | UI | The link's surface: Connect, Disconnect, the published status and the bounded frame channel |
| `LinkSession` | UI | One Connect: opens, reads, decodes, judges the health, drains, reopens and publishes every status, in one task |
| `LinkHealth` | UI | The 10 s health rules and the drain rule, as pure functions of timestamps |
| `LinkState`, `LinkStatus` | UI | What the link publishes: the state, the port, the latest `state` bits, the lost total and the fault on entry |
| `LinkError` and its five errors | UI | `PortBusyError`, `PortNotFoundError`, `PortLostError`, `BoardNotRespondingError`, `UnrecognisedDataError`, each with the port in its message |
| `PortListError` | UI | Windows did not return the port list; no port name, the listing failure as its cause |
| `DisplayedSpectrum`, `IDisplayedSpectrumSource` | UI | The spectrum the chart shows and its receive time; the source's `Current` and the `Changed` signal. The interface exists for two implementations: the live source now, and a historical source that shows a stored moment's spectrum in the intensity-trend slice |
| `LiveSpectrumSource` | UI | The live `IDisplayedSpectrumSource`: `Current` is the distributor's latest spectrum frame, one instance per frame; `Changed` signals each spectrum frame and skips heartbeats |
| `AppLogging` | UI | Creates the log folder and the Serilog logger: `spectrum-visualizer.log`, 5 MB per file, 5 files kept, shared between processes, invariant culture, process id in every line |
| `LogLevelControl` | UI | Wraps the logger's `LoggingLevelSwitch`; `IsVerbose` moves it between Information and Debug |
| `AddUi(LoggingSetup)` | UI | The composition root: registers Microsoft.Extensions.Logging over the Serilog logger, the log level control, `TimeProvider.System`, `AvaloniaScheduler.Instance` as the `IScheduler`, the port provider, `SerialLink` built with `DefaultScheduler.Instance`, the message panel, the connection row, the status bar and the main window view model, all singletons |
| `Program` | UI | Entry point: paths, logging, container, `App.Run` with the logger for unhandled exceptions |
| `App` | UI | Builds the Avalonia app with the exception handler, subscribes dispatcher exceptions, sets Semi's control strings to the UI culture, creates the main window from the container, refreshes the port list when the window opens and stops the link before it closes |
| `MainWindowViewModel` | UI | Exit request, the verbose-logging command, and the panel, connection row and status bar view models |
| `ConnectionBarViewModel` | UI | The port list, the selection and the Refresh, Connect and Disconnect commands; reports a failed listing to the panel |
| `AppStatusBarViewModel` | UI | The status bar texts and dot tone from `SerialLink.Status`; reports each fault and the restored entry to the panel |
| `MessagePanelViewModel` | UI | The bounded, newest-first list of messages the operator reads |
| `MessageSeverity` | UI | Info, Warning or Error on a message; the panel picks the dot class with `ObjectConverters.Equal` against each value |
| `ResultReporting`, `ErrorMessageMapper` | UI | The route from a failed `Result` to the panel and the log |
| `UnhandledErrorObserver` | UI | ReactiveUI's last-resort exception handler and the dispatcher's unhandled-exception handler |

`board-protocol.md` describes the link's read loop, health rules, drain, reopen and channel.

## Startup

`Program.Main` runs these steps in order:

1. `AppDataPaths.Under` resolves the log folder from `Environment.SpecialFolder.LocalApplicationData`.
2. `AppLogging.Create` creates `%LOCALAPPDATA%\SpectrumVisualizer\Logs` and the logger; `Program.Main` sets `Log.Logger` to it.
3. `AddUi` builds the service provider.
4. `App.Run` receives a logger from the container's `ILoggerFactory`, which writes to that logger, builds the
   `UnhandledErrorObserver` with the container's `IScheduler` and builds the Avalonia app. In `AfterSetup` it subscribes
   `Dispatcher.UIThread.UnhandledException`, which exists only once the platform is set up, and `App.Configure`
   resolves `MainWindowViewModel`, `MessagePanelViewModel`, `SerialLink` and the app's logger.
   `OnFrameworkInitializationCompleted` shows `MainWindow` with that view model as its `DataContext`.

A failure in step 1 or 2 ends with exit code 1 and shows nothing, because there is no log to write to. A failure after
the logger exists is logged as Fatal and ends with exit code 1.

No ReactiveUI object is constructed before `AppBuilder.Setup()`. ReactiveUI takes its exception handler once, on first
use, so a command or property helper built earlier would make the handler that `BuildAvaloniaApp` installs a silent
no-op. The view models come from the container inside `AfterSetup` for this reason.

The window's `Opened` event refreshes the port list once. The window's `Closing` event is where an operator close (the
close button, File, Exit) stops the link:

- When the link is idle, `DisconnectAsync` completes at once and the close goes ahead.
- When the stop has not completed by the time the first `Closing` checks it, that `Closing` cancels the close, awaits
  `SerialLink.DisconnectAsync()` for at most 5 s of real time (a timeout logs a Warning) and closes the window again.
  For an active link this is the usual case, because `DisconnectAsync` cancels through `CancelAsync` and the session
  finishes on the thread pool. A close that arrives while the stop runs stays cancelled and issues no second close.
- An OS shutdown closes the window through the same event, but a window cannot prevent it, so the stop is best-effort
  there.

The container's dispose after `App.Run` returns calls `SerialLink.Dispose()`, which cancels a session that is still
running without waiting for it. After a 5 s timeout the session can outlive the window: `Program` then calls
`Log.CloseAndFlush()` while the session may still write its last lines (the port close, "The link on port ... stopped"),
and those lines are not guaranteed to reach the log file. The process exits regardless.

## Main window

`SpectrumVisualizer/SpectrumVisualizer.UI/MainWindow/MainWindow.axaml` has five rows, top to bottom:

| Row | Height | Content |
| --- | --- | --- |
| Menu | `Auto` | `AppMenuBar`: File, Exit; View, Verbose logging |
| Connection row | `Auto` | `ConnectionBar`: label "Port", a 240 px port list, Refresh, Connect, Disconnect |
| Content | `*` | Placeholder text |
| Message panel | `Auto` | `MessagePanelView`; the row is hidden while the list is empty |
| Status bar | `Auto` | `AppStatusBar`: dot and link text, port, ATP-2000, ATP-8000, lost spectra |

The window is 1280 by 800 and opens centred. Exit is view work: the view model emits on `ExitRequests`, and the window
subscribes in `OnLoaded`, unsubscribes in `OnUnloaded`, and closes itself.

`MainWindow.MinimumWidth` is 1048 px, the window's `MinWidth`. It is the widest row with its longest texts, rounded up
to the 8 px grid: the Russian status bar with `COM256`, a six-digit lost count and "Порт недоступен, переподключение"
measures 1042.4 px (English 900.8 px), the connection row 654.9 px in Russian (English 595.4 px). The headless text stub
gives no real widths, so `MainWindowTextFitTests` adds the realised row chrome to each text measured through SkiaSharp at
14 px in the widest of Inter and every installed family of Semi's font chain; on Windows the app resolves that chain to
Microsoft YaHei. Both rows stay left-aligned at any width above the minimum, and no link state changes the set of rows
or a row's height.

The Verbose logging item reads `IsVerboseLogging` one-way and writes only through `ToggleVerboseLoggingCommand`, so the
command is the level's single writer. `LogLevelControl` raises no notification; the command raises
`IsVerboseLogging` after it flips the switch. The level resets to Information at every start. Each toggle writes
an Information line with the new minimum level and a Debug line; the Debug line reaches the log file only when the
toggle turned verbose logging on, so the operator sees the switch take effect in the file.

### Connection row

The port list and Connect are enabled only while the link is Disconnected; Connect also needs a selected port.
Disconnect is enabled in every other state, and Refresh in every state. `IsIdle` follows
`SerialLink.StatusUpdates(uiScheduler)`, which starts with `SerialLink.Status` and moves every later change to the UI
scheduler, so it is right from construction. Refresh reads the port list again and keeps the selection when its port is
still listed; a listing that fails shows an empty list and reports a `PortListError` to the message panel. With no ports the list shows the placeholder "No ports found"; the placeholder is empty while ports are
listed. Connect and Refresh are synchronous commands; Disconnect awaits `DisconnectAsync`.

### Status bar

`AppStatusBarViewModel` follows `SerialLink.StatusUpdates(uiScheduler)` the same way as the connection row and holds the
latest `LinkStatus`; every text derives from it:

| Segment | Text |
| --- | --- |
| Link | One text per `LinkState`: "Not connected", "Connecting", "Port unavailable, retrying", "Waiting for data", "Receiving data", "Board not responding", "Other device" |
| Port | The port name; "-" while disconnected |
| ATP-2000, ATP-8000 | "connected", "not connected", "measurement failed" (connected and failed bits set), or "no data" before the first frame |
| Lost spectra | The total since Connect; "-" while disconnected |

The dot is `AppConnectionOkBrush` in Receiving, `AppConnectionFaultBrush` in PortUnavailable, NotResponding and
OtherDevice, and `AppSecondaryForegroundBrush` otherwise. The link text always names the state, so colour never carries
it alone.

A status with a fault reports it through `ReportFailure`. The first Receiving after a fault adds one Info entry,
"Receiving data" with the detail "The board sends valid frames.". The text holds both when data returns after a loss
and when a Connect that first met a busy or missing port starts receiving. A Connecting status clears the seen-fault
flag, so the first Receiving after a fault-free Connect adds none. A throw inside the handler goes to
`TryReportFailure`.

## Error route

Every failure the operator should see goes to `MessagePanelViewModel` through one of the two `ResultReporting`
extensions:

- `ReportFailure(IError, ILogger)` maps the error with `ErrorMessageMapper`, adds it to the panel, and writes one log
  line at the level of its severity: Error, Warning or Information. The log line is written even when the panel throws.
- `TryReportFailure(IError, ILogger, IScheduler)` moves the report to the UI scheduler and never throws there, for
  callers whose exception would end an Rx stream or a dispatcher job.

`ErrorMessageMapper.Map` turns an `IError` into a `MessageView` (title, detail, remedy, severity):

| Error | Severity |
| --- | --- |
| `PortBusyError`, `PortNotFoundError` | Error: the operator acts |
| `PortLostError`, `BoardNotRespondingError`, `UnrecognisedDataError` | Warning: the link retries and the operator watches |
| `PortListError` | Warning: the operator presses Refresh |
| an exception error | Error; the detail keeps the exception type and message |
| any other error | Error; the detail keeps the error's own message |

A link error's view never names the port, so a repeat on another port coalesces. No error maps to Info: the status
bar's restored entry is the one Info entry, and it goes to the panel as a `MessageView` with its own Information log
line. The entry's dot takes `AppSeverityErrorBrush`, `AppSeverityWarningBrush` or `AppSeverityInfoBrush` through a
severity class.

The panel holds at most 200 entries, newest first. A message equal to the newest entry, severity included, does not add
a row: it raises that entry's repeat count and last-seen time, and the log line drops to Debug. Only the newest entry
is compared, so a lost port, a restored link and a second lost port are three rows. The panel row is visible while the
list has entries: the first entry opens it, Clear empties the list and hides it. An entry with an empty remedy shows
no remedy line.

`UnhandledErrorObserver` is the handler `BuildAvaloniaApp` passes to `UseReactiveUI(... WithExceptionHandler ...)`.
`App.Run` builds it once with the logger `Program` hands over, and it lives for the process, because ReactiveUI takes
the handler once, before any window exists. On each exception it reads the panel `App.Configure` resolved through
`App.ResolveMessagePanel`; before `Configure` it only logs.

The same observer handles `Dispatcher.UIThread.UnhandledException`, for an exception that escapes a dispatcher job
outside ReactiveUI, for example in a code-behind event handler. With a panel it reports an `ExceptionalError` through
`TryReportFailure` and sets `Handled`, so the app keeps running. Before `Configure` there is no panel: the exception
stays unhandled, ends the dispatcher loop, and `Program` logs it as Fatal and exits with code 1; the handler does not
log it a second time.
