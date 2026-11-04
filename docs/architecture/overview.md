# Overview

## Purpose

SpectrumVisualizer shows the operator the spectrum the spectrometer board sends over a serial port. The code under
`SpectrumVisualizer/` starts, logs to a fixed folder and shows the main window. The operator picks a COM port in the
connection row and connects; the serial link reads the board's frames, reopens the port after an unplug, and the
status bar shows the link state, both spectrometers' bits and the spectra lost since Connect. Link faults, reported
failures, ReactiveUI's unhandled exceptions and exceptions that escape a dispatcher job reach the message panel. The
frame distributor reads the decoded frames, and the content row shows the latest spectrum over wavelength on a ScottPlot
chart, with the board's signal values and the chart controls in a pane on its right. A console tool, the board emulator,
writes protocol frames to a serial port for manual runs.

## Stack

- .NET 10, C# 14, Windows (`net10.0`, published win-x64 self-contained).
- Avalonia 12.0.5 with Win32, Skia and HarfBuzz; Semi.Avalonia 12.0.3; ReactiveUI.Avalonia 12.0.3; System.Reactive 6.1.0.
- ScottPlot.Avalonia 5.1.59 for the spectrum chart.
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
| `SpectrumDisplay` | Core | How the spectrum is drawn: inverted or not, linear or log axis (`Core/Spectrum/`) |
| `SpectrumProjection` | Core | Writes the displayed value of each pixel into a line's buffer: inverted over the frame's maximum, then `log10` floored at 1 on the log axis; reports the raw maximum and whether the frame is dark (`Core/Spectrum/`) |
| `SpectrumScale` | Core | The axis limits of the shown spectrum while following and after Stick to zero, as pure functions (`Core/Spectrum/`) |
| `LogAxis` | Core | The ticks of an axis that runs in decades: major and minor positions for the limits and the axis length (`Core/Chart/`) |
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
| `FrameDistributor` | UI | The one reader of `SerialLink.Frames`: keeps the latest frame that carries a spectrum and hands every frame to each subscriber on its read task |
| `DisplayedSpectrum`, `IDisplayedSpectrumSource` | UI | The spectrum the chart shows and its receive time; the source's `Current` and the `Changed` signal. `LiveSpectrumSource` is its implementation |
| `LiveSpectrumSource` | UI | The live `IDisplayedSpectrumSource`: `Current` wraps the distributor's latest spectrum frame in a new record on each read, around the frame's own measurement; `Changed` signals each spectrum frame and skips heartbeats |
| `SpectrumChartViewModel` | UI | Owns the ScottPlot `Plot`: one line per spectrometer, the window band, the centre line, the axis type and the follow or frozen scale; asks the view for a redraw |
| `SignalInfoViewModel` | UI | The seven signal pane texts of the shown spectrum |
| `SpectrumViewModel` | UI | The spectrum screen: applies the source's latest spectrum to the chart and the signal pane, the Invert and Logarithmic checkboxes, Reset scale and Stick to zero |
| `ChartPalette` | UI | Paints ScottPlot's figure, data area, grid and axes from the palette, and resolves the colours of the line, the band and the centre line |
| `LogTickGenerator` | UI | ScottPlot's tick generator over `LogAxis`: the labels under the current culture, and a cache of the last two results keyed on the limits, the length and the culture |
| `AppLogging` | UI | Creates the log folder and the Serilog logger: `spectrum-visualizer.log`, 5 MB per file, 5 files kept, shared between processes, invariant culture, process id in every line |
| `LogLevelControl` | UI | Wraps the logger's `LoggingLevelSwitch`; `IsVerbose` moves it between Information and Debug |
| `AddUi(LoggingSetup)` | UI | The composition root: registers Microsoft.Extensions.Logging over the Serilog logger, the log level control, `TimeProvider.System`, `AvaloniaScheduler.Instance` as the `IScheduler`, the port provider, `SerialLink` built with `DefaultScheduler.Instance`, `FrameDistributor` over `SerialLink.Frames`, `LiveSpectrumSource` as the `IDisplayedSpectrumSource`, the message panel, the connection row, the status bar, the spectrum view model and the main window view model, all singletons |
| `Program` | UI | Entry point: paths, logging, container, `App.Run` with the logger for unhandled exceptions |
| `App` | UI | Builds the Avalonia app with the exception handler, subscribes dispatcher exceptions, sets Semi's control strings to the UI culture, creates the main window from the container, refreshes the port list and starts the frame distributor when the window opens, and stops the link, then the distributor, before it closes |
| `MainWindowViewModel` | UI | Exit request, the verbose-logging command, and the panel, connection row, status bar and spectrum view models; it disposes none of them, the container owns them |
| `ConnectionBarViewModel` | UI | The port list, the selection and the Refresh, Connect and Disconnect commands; reports a failed listing to the panel |
| `AppStatusBarViewModel` | UI | The status bar texts and dot tone from `SerialLink.Status`; reports each fault and the restored entry to the panel |
| `MessagePanelViewModel` | UI | The bounded, newest-first list of messages the operator reads |
| `MessageSeverity` | UI | Info, Warning or Error on a message; the panel picks the dot class with `ObjectConverters.Equal` against each value |
| `ResultReporting`, `ErrorMessageMapper` | UI | The route from a failed `Result` to the panel and the log |
| `UnhandledErrorObserver` | UI | ReactiveUI's last-resort exception handler and the dispatcher's unhandled-exception handler |

`board-protocol.md` describes the link's read loop, health rules, drain, reopen and channel.

Core holds every algorithm that needs no UI type: the projection and the scale rules under `Core/Spectrum/`, the log
axis ticks under `Core/Chart/`. The UI keeps what touches ScottPlot or Avalonia: `LogTickGenerator`, `ChartPalette` and
the chart view model.

## Startup

`Program.Main` runs these steps in order:

1. `AppDataPaths.Under` resolves the log folder from `Environment.SpecialFolder.LocalApplicationData`.
2. `AppLogging.Create` creates `%LOCALAPPDATA%\SpectrumVisualizer\Logs` and the logger; `Program.Main` sets `Log.Logger` to it.
3. `AddUi` builds the service provider.
4. `App.Run` receives a logger from the container's `ILoggerFactory`, which writes to that logger, builds the
   `UnhandledErrorObserver` with the container's `IScheduler` and builds the Avalonia app. In `AfterSetup` it subscribes
   `Dispatcher.UIThread.UnhandledException`, which exists only once the platform is set up, and `App.Configure`
   resolves `MainWindowViewModel`, `MessagePanelViewModel`, `SerialLink`, `FrameDistributor` and the app's logger.
   `OnFrameworkInitializationCompleted` shows `MainWindow` with that view model as its `DataContext`.

A failure in step 1 or 2 ends with exit code 1 and shows nothing, because there is no log to write to. A failure after
the logger exists is logged as Fatal and ends with exit code 1.

No ReactiveUI object is constructed before `AppBuilder.Setup()`. ReactiveUI takes its exception handler once, on first
use, so a command or property helper built earlier would make the handler that `BuildAvaloniaApp` installs a silent
no-op. The view models come from the container inside `AfterSetup` for this reason.

The window's `Opened` event refreshes the port list once and starts the frame distributor. The window's `Closing` event
is where an operator close (the close button, File, Exit) stops both. `StopBackgroundServicesAsync` awaits
`SerialLink.DisconnectAsync()`, then `FrameDistributor.StopAsync()`, both inside one 5 s limit of real time; a timeout
logs a Warning. The link stops first, so no frame enters the channel after the distributor has stopped reading it. The
distributor stops even when the link's stop throws; any failure logs an Error, and the window closes anyway, because
the stop task never faults.

- When the stop has completed by the time the first `Closing` checks it, the close goes ahead. That happens only when
  neither ran, before `Opened`.
- Otherwise that `Closing` cancels the close, awaits the stop and closes the window again. After `Opened` this is every
  close: the distributor's read task ends on the thread pool, and an active link's `DisconnectAsync` cancels through
  `CancelAsync` and the session finishes there too. A close that arrives while the stop runs stays cancelled and
  issues no second close.
- An OS shutdown closes the window through the same event, but a window cannot prevent it, so the stop is best-effort
  there.

The container's dispose after `App.Run` returns calls `SerialLink.Dispose()` and `FrameDistributor.Dispose()`, which
cancel what still runs without waiting for it. After a 5 s timeout the session can outlive the window: `Program` then
calls `Log.CloseAndFlush()` while the session may still write its last lines (the port close, "The link on port ...
stopped"), and those lines are not guaranteed to reach the log file. The process exits regardless.

## Main window

`SpectrumVisualizer/SpectrumVisualizer.UI/MainWindow/MainWindow.axaml` has five rows, top to bottom:

| Row | Height | Content |
| --- | --- | --- |
| Menu | `Auto` | `AppMenuBar`: File, Exit; View, Verbose logging |
| Connection row | `Auto` | `ConnectionBar`: label "Port", a 240 px port list, Refresh, Connect, Disconnect |
| Content | `*` | `ContentArea`, columns `*,Auto`: `SpectrumPane` with `SpectrumChartView`, and the 392 px `SignalPane` with `SpectrumPanel` |
| Message panel | `Auto` | `MessagePanelView`; the row is hidden while the list is empty |
| Status bar | `Auto` | `AppStatusBar`: dot and link text, port, ATP-2000, ATP-8000, lost spectra |

The window is 1280 by 800 and opens centred. Exit is view work: the view model emits on `ExitRequests`, and the window
subscribes in `OnLoaded`, unsubscribes in `OnUnloaded`, and closes itself.

`MainWindow.MinimumWidth` is 1048 px, the window's `MinWidth`. It is the widest of the rows with their longest texts and
the content row's floor, rounded up to the 8 px grid: the Russian status bar with `COM256`, a six-digit lost count and
"Порт недоступен, переподключение" measures 1042.4 px (English 900.8 px), the connection row 654.9 px in Russian
(English 595.4 px), and the signal pane plus a 400 px chart floor 792 px. At 400 px an ATP-8000 spectrum keeps at least
four labelled wavelength ticks; the measured axis shows eight, 1000 to 2400 nm. The headless text stub gives no real
widths, so `MainWindowTextFitTests` adds the realised row chrome to each text measured through SkiaSharp at 14 px in the
widest of Inter and every installed family of Semi's font chain; on Windows the app resolves that chain to Microsoft
YaHei. Both rows stay left-aligned at any width above the minimum, and no link state changes the set of rows or a row's
height.

The Verbose logging item reads `IsVerboseLogging` one-way and writes only through `ToggleVerboseLoggingCommand`, so the
command is the level's single writer. `LogLevelControl` raises no notification; the command raises
`IsVerboseLogging` after it flips the switch. The level resets to Information at every start. Each toggle writes
an Information line with the new minimum level and a Debug line; the Debug line reaches the log file only when the
toggle turned verbose logging on, so the operator sees the switch take effect in the file.

### Content row

The chart is the one element that stretches. `SpectrumPane` holds the chart edge to edge with no padding, because
ScottPlot draws its own axis margins over the panel background. `SignalPane` is a `Border` with a 1 px left border and
`Padding="16"`, `SpectrumPanel.PaneWidth` wide, holding one form grid: a label column, a 12 px gap and a field column of
at least 120 px, the number width, with 8 px between rows. The "Signal" header comes first, then seven label and value
rows; the "Chart" header sits 16 px below them, then the "Invert spectrum" and "Logarithmic scale" checkboxes and the
Reset scale and Stick to zero buttons, one per row in the field column with an empty label cell. The buttons carry
`HorizontalAlignment="Left"`, because Semi's button otherwise centres in the column.

Every text in the pane stays on one line. A pane-scoped style sets `NoWrap` on every `TextBlock`, and each checkbox
holds a `TextBlock` instead of a string, because Semi's `CheckBox` template wraps string content at template priority,
where no style reaches. `PaneWidth` is 392 px: the 1 px border, 16 padding, the label column, 12, the field column, 16
padding and 24 px slack, rounded up to the 8 px grid. Russian sets it: labels 117.5 px for "Окно измерения", field
column 198.9 px for "Логарифмическая шкала", 385.4 px in all (English 91.0 and 140.6 px, 300.6 px). The longest values
("-1,23457E-38", "65535 nm", "±255 nm", "23:59:59.999") measure at most 92.1 px and fit the 120 px floor.
`MainWindowTextFitTests` measures them the same way as the status bar, in the labels' face, which every label and value
shares.

The message panel row takes its height from the content row: the chart and the pane lose that height and keep their
width, and the window keeps its size. No spectrum state changes the window size or either pane's bounds.

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

## Spectrum

### Data flow

1. The link writes each `ReceivedFrame` to its channel (session task).
2. `FrameDistributor` reads it. For a frame that carries a measurement it publishes `LatestSpectrum` with
   `Volatile.Write`; then it calls each subscriber's `OnNext` inside its own try/catch (distributor task).
3. `LiveSpectrumSource` passes spectrum frames on as `Changed` (distributor task).
4. `SpectrumViewModel` moves the signal to the UI scheduler, reads `Current`, drops a repeat of the same measurement, calls
   `Chart.Show` and sets `SignalInfo.Spectrum` (UI thread).
5. `Chart.Show` fills the line's buffer and the limits under `lock (Plot.Sync)` and requests a redraw.
6. The view serves one redraw in the next display frame.

`LatestSpectrum` is the latest frame that carries a spectrum, not the latest frame: a heartbeat has no measurement, and
the chart would have nothing to read for up to 3 s after one.

The distributor keeps its own subscriber list. `Frames` is a small `IObservable` over it, not `Observable.Create`,
whose wrapper unsubscribes an observer that throws. The list is an `ImmutableArray` replaced under a lock, because
subscribers come and go on any thread, and the read task takes one snapshot per frame. A subscriber that throws stays
subscribed and takes the frame from no other subscriber: the distributor reports the exception once through
`TryReportFailure`, which logs it at Error, and the frame still reaches every later subscriber. The channel never
completes, so the read task lives across link sessions and ends only on `StopAsync` or `Dispose`, with a Debug line at
its start and its end. Any other exception from the channel read ends the loop with an Error line before the end line,
so `StopAsync` still completes. `Start` after a start or a stop does nothing; `StopAsync` completes at once when nothing
runs, and disposes the cancellation source once the read task has ended. Subscribers own their scheduling and never
block the read task.

`SpectrumViewModel` subscribes in its constructor with `Changed.ObserveOn(uiScheduler).StartWith(...)`, then a read of
`source.Current`, `WhereNotNull` and `DistinctUntilChanged` on the measurement by reference, then `Show`. Signals queued
while the UI thread is busy each read the same latest measurement, so the chart applies the latest spectrum once without
comparing pixel arrays (record equality on `SpectrumMeasurement` compares every pixel). A spectrum present at
construction shows at once. A throw from the `Current` read or from `Show` goes to `TryReportFailure` and skips that
spectrum only; the next signal reads and shows again. Only a failure of `Changed` itself ends the subscription, through
the same report. The Invert and Logarithmic subscription reports a failure the same way. The view model is a container
singleton, so the subscriptions live for the process.

### The chart

`SpectrumChartViewModel` builds one `SignalXY` per spectrometer, with the device's wavelengths from
`SpectrometerCalibration.WavelengthOf` as `xs` and its own `ys` buffer; only the shown device's line is visible. The
window is a `HorizontalSpan` from `WavelengthNm - Delta` to `WavelengthNm + Delta`, and a `VerticalLine` marks
`WavelengthNm`. When that window does not overlap the device's wavelength range, both are hidden, and with `Delta` 0
only the centre line is drawn; the signal pane still shows the wavelength and the window. The axis labels come from
resx, and `Plot.Font.Automatic()` picks a font that holds Cyrillic. Every write to the plot (buffers, visibility,
limits, the tick generator, the minor grid width, colours) happens inside `lock (Plot.Sync)`, which ScottPlot's
render takes too, so a render never sees half an update. The frame's pixel array is read, never written.

Projection, per pixel: `value = inverted ? max - pixel : pixel` over the frame's own maximum, then
`ys[i] = logarithmic ? log10(max(value, 1)) : value`. Pixels are integers, so 1 is the smallest positive reading and a
zero pixel lands on the 10^0 line; the log axis never needs a decade below 0. An all-zero spectrum inverted stays all
zero. ScottPlot has no log axis, so the log state lives in the axis's tick generator: a `LogTickGenerator` with 1 px
minor gridlines, or a fresh `NumericAutomatic` with none. A major tick reads `10^position` as `"0.###"` under the
current culture, and a label that repeats turns into a minor tick.

### The scale

The scale either follows the data or is frozen. `SpectrumScale` computes the limits; ScottPlot's autoscale never
sets them, so a flat range has a defined answer and no limit is NaN.

| Case | X | Y, linear | Y, log (decades) |
| --- | --- | --- | --- |
| Following | `xs[0] .. xs[P - 1]` of the shown device | `min - 5 % .. max + 5 %` of the reference's span | the same, in decades |
| Following, flat reference (`min == max`) | same | `v - 1 .. v + 1` | `v - 0.5 .. v + 0.5` |
| Stick to zero | same | `0 .. rawMax x 1.2`; `0 .. 1` when `rawMax` is 0 | `0 .. log10(rawMax x 1.2)`; `0 .. 1` when `rawMax <= 1` |
| Frozen | unchanged | unchanged | unchanged |

The reference is the spectrum the Y limits come from: the shown spectrum, or, while a dark frame is shown, the last
non-dark measurement, projected again in the current inversion and axis mode. A dark frame is one whose raw pixels are
all zero. The chart keeps the last non-dark measurement of each device, and a dark frame takes its own device's. With
no earlier non-dark frame of that device the dark frame is its own reference and takes the flat range: the other
device's intensities say nothing about this one. So a dark frame
while following keeps the Y limits of the spectrum before it, Stick to zero on a dark frame takes that spectrum's
`rawMax`, and a log or inversion toggle during a dark frame shows that spectrum's limits in the new mode; X follows the
dark frame's device in every case. On the log axis the dark line lies at 0 decades, below a range kept from a spectrum
whose minimum is above 1, so the data area is empty for that frame. `rawMax` is the reference's largest displayed value
before the log step, after inversion.

The scale follows from the start, after Reset scale and after a log or inversion toggle. Once a spectrum is shown it
freezes on a wheel step over the plot, on a pointer move with a button held 5 px or more from the press, and after
Stick to zero. Before the first spectrum neither input freezes it, so the first spectrum sets the limits. Tunnel
handlers for `PointerWheelChanged`, `PointerPressed`, `PointerMoved` and `PointerReleased` on `AvaPlot` record the
press and forward to the chart: a wheel step calls `TakeManualScale()`, and a move calls `OnHeldMove(press, current,
isButtonHeld)`, which decides the freeze from the held button and the larger of the X and Y distances. The handlers run
before ScottPlot's class handler, which marks the wheel event handled. The 5 px is ScottPlot 5.1.59's own floor:
`MouseDragPan` and `MouseDragZoomRectangle` ignore a held move shorter than that, so a shorter move would freeze limits
ScottPlot never moved. ScottPlot still applies that short pan on release, and `MouseDragZoom` (right button) has no
floor, so a right drag under 5 px zooms a little; while following, the next spectrum sets the limits back in both
cases. ScottPlot's `MouseDragPan` also restores
the press-time limits on release before it applies the drag, so a press held across a frame ends on limits panned from
the press-time view, not from the last frame. A bare click and a key press keep the scale following. While frozen, a
frame from the other device keeps the frozen limits, and its line can lie outside them until Reset scale. Reset scale
is enabled only while a spectrum is shown and the scale is frozen, so the button also shows the frozen state; Stick to
zero is enabled once a spectrum is shown. `IsFollowing` raises its change after `Plot.Sync` is released, so no
subscriber runs under the plot lock.

ScottPlot's input processor stays on with five responses: `MouseInteractWithPlottables`, `MouseDragZoomRectangle`,
`MouseDragPan`, `MouseDragZoom` and `MouseWheelZoom`. The view removes `DoubleClickBenchmark`, `SingleClickAutoscale`,
`SingleClickContextMenu`, `KeyboardAutoscale` and `KeyboardPanAndZoom`: the app is a read-only viewer, ScottPlot's
autoscaler never sets the limits, and keyboard input never moves them.

### The view

`SpectrumChartView` holds `AvaPlot` alone inside the `PlotLayer` decorator, whose `CacheMode` is a `BitmapCache`. The
cache never goes on `AvaPlot` itself: there Avalonia 12 freezes the plot on its first frame. The view binds the chart
with `PlotControl.Reset(chart.Plot)` when its `DataContext` changes and subscribes `RedrawRequested`; disposing the
chart completes that stream and clears a pending frame. `AvaPlot.Reset` disposes the plot it replaces, so the view
skips it when the control already holds the chart's plot and leaves the control alone on a null `DataContext`: the same
chart coming back keeps its plottables. A switch to another chart still disposes the previous chart's plot, because
ScottPlot has no public way to detach a plot; the app builds one chart for the process. Each request asks
`TopLevel.RequestAnimationFrame` once per pending frame and invalidates the plot in the callback, so every request
before the next display frame joins one redraw. Nothing calls `AvaPlot.Refresh()`; ScottPlot redraws on its own only
while the operator drags or zooms. The view repaints the plot from the palette on `OnLoaded` and on the application's
`ActualThemeVariantChanged`, and removes that handler in `OnUnloaded` (`ui-theme.md`). One hold of `Plot.Sync` paints
the surfaces and the series colours, so no render sees one without the other.

Until the first spectrum a line over the plot reads "No spectrum yet. Connect to the board in the row above." After a
Disconnect the last spectrum stays and its update time tells its age; a new Connect does not clear it.

### The signal pane

`SignalInfoViewModel` holds the shown `DisplayedSpectrum` and raises its seven texts from that one setter, which
compares by reference: spectrometer, wavelength (`{0} nm`), window (`±{0} nm`), amplitude, SNR, Q factor and the update
time. Amplitude, SNR and Q are the board's values in `G6` under the current culture; a NaN or infinite value reads "-".
SNR has no unit, because the board sends a ratio. The update time is `ReceivedAt` in the `TimeProvider`'s local zone as
`HH:mm:ss.fff`. Before the first spectrum every row reads "-", so every row exists in every state.

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
