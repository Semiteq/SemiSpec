# Overview

## Purpose

SemiSpec shows the operator the spectrum the spectrometer board sends over a serial port. The code under
`SemiSpec/` starts, logs to `C:\DISTR\Logs\SemiSpec` and shows the main window; one copy runs per Windows session, and
a second launch brings that copy's window to the front. The operator picks a COM port in the
connection row and connects; the serial link reads the board's frames, reopens the port after an unplug, and the
status bar shows the link state, both spectrometers' bits, the spectra lost since Connect and the frames dropped since
the start. Link faults, reported failures, ReactiveUI's unhandled exceptions and exceptions that escape a dispatcher job
reach the message panel. The frame distributor reads the decoded frames, and the content row shows the latest spectrum
over wavelength on a ScottPlot chart, with the board's signal values in a readout pane on its right. A left click on
an axis of either chart opens that axis's flyout with its bounds and modes. Under the spectrum chart the trend shows
the intensity at the board's wavelength over time across the full window width, from an in-memory cache of the last
24 h, with an overview strip of that whole history under it; a click on the trend shows the spectrum of that moment
on the spectrum chart until the operator returns to live. The spectrum store writes every received frame
to one SQLite history file, kept across restarts, and keeps the last 24 h; at start the trend loads the stored day
from it. The store's query service reads a stored spectrum, the intensity series and the intensity points at a
wavelength. Two console tools support manual runs: the board emulator
writes protocol frames to a serial port, and the store benchmark measures the store file's writes, size and reads and
the trend's folds and re-reads.

## Stack

- .NET 10, C# 14, Windows (`net10.0`, published win-x64 self-contained).
- Avalonia 12.0.5 with Win32, Skia and HarfBuzz; Semi.Avalonia 12.0.3; ReactiveUI.Avalonia 12.0.3; System.Reactive 6.1.0.
- ScottPlot.Avalonia 5.1.59 for the spectrum chart and the trend chart.
- Microsoft.Extensions.DependencyInjection for the composition root.
- Serilog with the file sink, bridged to `Microsoft.Extensions.Logging` through Serilog.Extensions.Logging.
- FluentResults 4.0.0 for failures returned as values.
- Microsoft.CodeAnalysis.ResxSourceGenerator for the text accessor (`ui-text.md`).
- System.IO.Pipelines and System.Threading.Channels from the shared framework for the frame reader, the link and the
  store queue.
- RJCP.SerialPortStream 3.0.5 for the serial port, in the UI and the board emulator.
- Microsoft.Data.Sqlite 10.0.14 for the spectrum store, in Core.
- Core also references System.Reactive: the intensity cache exposes its signals as `IObservable` streams. Core logs
  nothing; the trend feed logs the cache's key changes.
- The console tools only: System.CommandLine 2.0.2.

`SemiSpec/Directory.Packages.props` holds every version.

## Components

| Component | Project | Responsibility |
| --- | --- | --- |
| `AppDataPaths` | Core | Resolves the store folder `SemiSpec\Spectra` and the fallback log folder `SemiSpec\Logs` under the local app data root; fails on a root that is not fully qualified |
| `Crc16Modbus` | Core | CRC-16/MODBUS of the frame, over a span |
| `BoardFrame`, `SpectrumMeasurement` | Core | One decoded frame: state, `seq` and the measurement of a spectrum frame; the measurement rejects a pixel count that does not match its device |
| `ReceivedFrame` | Core | A decoded frame with its receive time; the item of the link's channel and of the store queue (`Core/Protocol/`) |
| `Spectrometer` | Core | The two devices, their pixel count, the size of their spectrum frame and the wavelength both tools default to |
| `FrameEncoder` | Core | The bytes of a frame; used by the emulator and the tests |
| `FrameDecoder` | Core | Finds and validates frames in a byte sequence; drops one byte per failed candidate |
| `FrameReader` | Core | Yields the frames of a `PipeReader`, or decodes what is buffered without a wait, and counts the dropped bytes |
| `SequenceTracker` | Core | Counts the spectra lost between frames from `seq` |
| `SpectrometerCalibration` | Core | Per device: the wavelength of each pixel, the pixel for a wavelength, the pixel window for a wavelength and `delta` |
| `SpectrumDisplay` | Core | How the spectrum is drawn: inverted or not, linear or log axis (`Core/Spectrum/`) |
| `SpectrumProjection` | Core | Writes the displayed value of each pixel into a line's buffer: inverted over the frame's maximum, then `log10` floored at 1 on the log axis; reports the raw maximum and whether the frame is dark (`Core/Spectrum/`) |
| `SpectrumScale` | Core | The axis limits of the shown spectrum while following, with and without From zero, as pure functions (`Core/Spectrum/`) |
| `FromZeroRange` | Core | The From zero range shared by the spectrum, the trend and the overview band: `0 .. max + 5 %`, `0 .. 1` when the maximum is not above 0 (`Core/Chart/`) |
| `LogAxis` | Core | The ticks of an axis that runs in decades: major and minor positions for the limits and the axis length (`Core/Chart/`) |
| `StoreFiles` | Core | Names the history file `spectra.db` and prepares the store folder: creates it and deletes the per-process `spectra-<digits>.db` files of older builds with their sidecars; marks the history file damaged with `spectra.db.damaged`; sets a damaged file aside as `spectra.broken-<stamp>.db` and keeps the newest two (`Core/Store/`) |
| `StoreDamage` | Core | Tells a SQLite failure that means a damaged file, code 11 (`SQLITE_CORRUPT`) or 26 (`SQLITE_NOTADB`) anywhere in the cause chain, from any other (`Core/Store/`) |
| `StoreConnection` | Core | Opens the writer's connection to a store file, creating it, with the store's connection string and pragmas; `OpenReader` opens an existing file only, with `query_only` (`Core/Store/`) |
| `SpectrumDatabase` | Core | The writer connection: the open gated on the schema version in `PRAGMA user_version`, a batch of frames in one transaction, the deletion of rows older than a cutoff with a fast path when every row has expired, and the closing checkpoint; `StoreSchemaMismatchError` and `StoreFileUnreadableError` are its open failures (`Core/Store/`) |
| `QueryWindows` | Core | The queried window of each device as byte offsets in the stored pixels, whether the device covers the wavelength, and the mean of a window's pixels (`Core/Store/`) |
| `IntensityQuery`, `IntensityBuckets`, `IntensitySeries` | Core | The validated series request with its epoch-aligned column grid, the fold of window means, breaks and failure marks into columns on that grid (min, max, last value, counts, the OR of the failure bits), and its result (`Core/Store/`) |
| `SpectrumHistory` | Core | The query service: the stored spectrum at a moment, the intensity series, the intensity points and the stored range, each on its own reader connection; a read that fails on a damaged file marks it (`Core/Store/`) |
| `FrameContinuity` | Core | Turns spectra and heartbeats, in receive order, into the trend's Loss breaks and failure marks (`Core/Trend/`) |
| `IntensityCache` | Core | One intensity point per spectrum at the key of the latest spectrum frame, with the run's breaks and failure marks, for the last 24 h; the recent spectra, the key change and its re-read request, the run start and the stored day request, the landing of stored chunks, the completion of a re-read, the fold of the current extent under one lock hold and the moment lookup; `TrendTimeline` holds the key's points, breaks and failure marks in time order and folds them, with a NoSpectra break at each gap of more than 10 s between points. `TrendKey`, `TrendExtent`, `TrendFold` and `ReReadRequest` are its results (`Core/Trend/`) |
| `TrendHistoryPlan` | Core | The store ranges a re-read runs, in order: the visible window, then the rest of the day in hour chunks, newest first (`Core/Trend/`) |
| `IntensityChunk`, `TrendPoint` | Core | The result of the store's points read: its start and one `(UnixMs, Value)` point per spectrum; `IntensityBuckets.Add(TrendPoint)` folds a NaN point as uncovered (`Core/Store/`) |
| `TrendNavigationModel` | Core | The trend's visible window in UTC: width 10 s to 24 h, follow, pan, zoom around an anchor, set width, set an exact window, the pan floor at the first point, and the rule that a pan or a zoom leaving now outside the window stops following (`Core/Trend/`) |
| `TrendColumns` | Core | The column width for a data area, the fold query that includes its end, the gap rule and the joined failure ranges (`Core/Trend/`) |
| `OverviewGeometry` | Core | Places the trend window, the pointer and the band along the overview strip, applies the trend's gap rule to the band, and scales the band from zero when the trend runs From zero (`Core/Trend/`) |
| `FrameSchedule`, `SpectrumGenerator` | Tools.BoardEmulator | Which frame the emulated board sends at a tick and its `state`, and the synthetic and dark spectra |
| `EmulatorCommand` | Tools.BoardEmulator | Parses and range-checks the command line into an `EmulatorOptions` record; rejects `--failed` for the other spectrometer without `--both-connected` |
| `Program` (emulator) | Tools.BoardEmulator | Opens the port, checks every 10 ms which frame is due and writes it, until Ctrl+C |
| `BenchmarkCommand` | Tools.StoreBenchmark | Parses and range-checks the command line into a `BenchmarkOptions` record |
| `ToolCommandLine` | both tools | The model numbers of `--device` and `--failed` and the whole-number option parser; one source file under `SemiSpec/Shared/` that each tool project compiles as its own internal copy, since the two tools share no project but Core |
| `BenchmarkRun`, `BenchmarkFile` | Tools.StoreBenchmark | Fills a store file through `SpectrumDatabase`, churns it, measures inserts, size, series reads, `SpectrumHistory`, retention, WAL growth, the trend cache's folds and the trend's re-reads, and prints one table |
| `ISerialPortProvider`, `RjcpSerialPortProvider` | UI | Lists the COM ports and opens one for the link, mapping open failures to typed errors |
| `SerialLink` | UI | The link's surface: Connect, Disconnect, the published status and the bounded frame channel |
| `LinkSession` | UI | One Connect: opens, reads, decodes, judges the health, drains, reopens and publishes every status, in one task |
| `LinkHealth` | UI | The 10 s health rules and the drain rule, as pure functions of timestamps; the 10 s is `LinkTiming.SilenceLimit` (`Core/Protocol/`), which `FrameContinuity` shares |
| `LinkState`, `LinkStatus` | UI | What the link publishes: the state, the port, the latest `state` bits, the lost total, the fault on entry and the count of frames the channel dropped |
| `LinkError` and its five errors | UI | `PortBusyError`, `PortNotFoundError`, `PortLostError`, `BoardNotRespondingError`, `UnrecognisedDataError`, each with the port in its message |
| `PortListError` | UI | Windows did not return the port list; no port name, the listing failure as its cause |
| `FrameDistributor` | UI | The one reader of `SerialLink.Frames`: keeps the latest frame that carries a spectrum and hands every frame to each subscriber on its read task |
| `DisplayedSpectrum`, `IDisplayedSpectrumSource` | UI | The spectrum the chart shows, its receive time and whether it is historical; the source's `Current` and the `CurrentChanged` signal. `LiveSpectrumSource` and `SwitchingSpectrumSource` implement it |
| `LiveSpectrumSource` | UI | The live `IDisplayedSpectrumSource`: `Current` wraps the distributor's latest spectrum frame in a new record on each read, around the frame's own measurement; `CurrentChanged` signals each spectrum frame and skips heartbeats |
| `SwitchingSpectrumSource` | UI | The `IDisplayedSpectrumSource` the app reads: the live source's spectrum, or the spectrum of a past moment until the operator returns to live; `ShownMoment` is that moment's receive time (`UI/Spectrum/`) |
| `SpectrumChartViewModel` | UI | Owns the ScottPlot `Plot`: one line per spectrometer (`DeviceLine`), the window band, the centre line, the axis type, From zero and the follow or frozen scale; reads and sets the drawn axis ranges for the flyouts and names a range the intensity axis cannot show; asks the view for a redraw |
| `SignalInfoViewModel` | UI | The seven readout pane texts of the shown spectrum |
| `SpectrumViewModel` | UI | The spectrum screen: applies the source's latest spectrum to the chart and the readout pane; the Invert, Logarithmic, From zero and Autoscale commands, the two axis forms and Back to live; composes the chart's state line from `ShownMoment`, the staleness and the modes |
| `SpectrumStaleness` | UI | Whether the shown live spectrum is stale: the link is not Receiving, or the last live spectrum is more than 10 s old (`UI/Spectrum/`) |
| `IAxisFlyoutForm`, `AxisFlyout` | UI | The form behind an axis flyout (seed on open, commit on close, Escape cancels) and the view helper that opens the flyout at the pointer, closes the drop-downs inside it first on hide, and hides it when the window starts to close (`UI/Chart/`) |
| `AxisRangeForm` | UI | The two number fields of an axis flyout, seeded from the drawn range, validated, and written back when a valid pair differs from the seed; takes the axis's own rule (`UI/Chart/`) |
| `ChartAxisRegion` | UI | The band of the left or bottom axis panel of the last render, for a press on the axis (`UI/Chart/`) |
| `ChartStateLine` | UI | Places a chart's one-line state text in the 24 px top margin over the data area (`UI/Chart/`) |
| `ChartFrames` | UI | A chart view's redraws: joins redraw requests into one animation frame and repaints the plot and the series on a theme change; both chart views own one (`UI/Chart/`) |
| `ChartPalette` | UI | Paints ScottPlot's figure, data area, grid and axes from the palette, and resolves the colours of the spectrum line, the band, the centre line, the trend's envelope, the shown-moment line and the failure strip |
| `LogTickGenerator` | UI | ScottPlot's tick generator over `LogAxis`: the labels under the current culture, and a cache of the last two results keyed on the limits, the length and the culture |
| `EnvelopeLine`, `EnvelopePath` | UI | The trend's ScottPlot plottable: the min/max envelope path over the envelope columns, broken at each gap column; the chart writes it under `Plot.Sync`, which every ScottPlot render holds (`UI/Trend/`) |
| `EnvelopeColumn` | UI | One envelope column at chart X; maps folded columns to envelope columns and gaps, and the Y range (`UI/Trend/`) |
| `TrendMarkerStrip` | UI | A 4 px bar along the bottom of the trend's data area over each failure range, converted to axis X in its own buffer (`UI/Trend/`) |
| `TrendPlotLayers` | UI | The trend's layers on its plot: the envelope, the failure strip, the shown-moment line and the axes, written under `Plot.Sync` (`UI/Trend/`) |
| `TrendCursorOverlay` | UI | Places the trend's hover line and readout box over the data area (`UI/Trend/`) |
| `LocalTimeAxis` | UI | Converts a `DateTimeOffset` to the trend's X value, local-time OADate, and back (`UI/Trend/`) |
| `TrendChartViewModel` | UI | Owns the trend's `Plot`: folds the cache over the navigation window at one column per data-area pixel, the Y limits with From zero and a typed range, the Y label, the failure strip, the shown-moment line, navigation and the hover text (`UI/Trend/`) |
| `TrendReadout` | UI | The hover text of one folded column: local time, value or gap reason, failures (`UI/Trend/`) |
| `TrendPresets` | UI | The time window presets 1 min, 10 min, 1 h, 6 h and 24 h, 10 min by default (`UI/Trend/`) |
| `TrendFeed` | UI | Fills the cache from the distributor, loads the stored day once the store has opened, logs each re-read request, runs the re-read after a key change or the stored day request and the read of a clicked moment, each as a pipeline where a newer request cancels the read in flight, and cancels the moment read on Back to live (`UI/Trend/`) |
| `TrendViewModel` | UI | The trend: the chart, the overview, the time window form, the intensity form, From zero, Autoscale, Follow live, the state line and the click to a moment (`UI/Trend/`) |
| `TimeWindowPanelViewModel` | UI | The time axis flyout: the window's local start and end (time and date), its width and the presets, ported from SemiPlot's time window panel without its archive calendar (`UI/Trend/`) |
| `OverviewViewModel`, `OverviewBand` | UI | The overview strip: the whole cache folded to 250 columns, the window's place along it, and the press that moves the trend there; the band control draws it (`UI/Trend/`) |
| `TrendHistoryError`, `TrendMomentError` | UI | The stored points at a new key could not be read; the stored spectrum of a clicked moment could not be read (`UI/Trend/`) |
| `SpectrumStore`, `StoreWriter`, `StoreOpening`, `StoreRetention`, `StoreFile` | UI | The background writer: `SpectrumStore` enqueues every distributor frame, runs one `StoreWriter` task, counts the frames lost and signals `Opened`; `StoreWriter` opens the history file through `StoreOpening`, writes the queue to it in batches and runs `StoreRetention` at the open and once a minute, which deletes rows older than 24 h; `StoreOpening` prepares the folder, opens the file, replaces a file of another schema version and sets a damaged one aside; `StoreFile` is the history file's path (`UI/Store/`) |
| `StoreOpenError`, `StoreWriteError`, `StoreStoppedError`, `StoreFileReplacedError` | UI | The store file could not be prepared or opened; a batch could not be written; an unexpected failure after the open ended the writer; a damaged history file was set aside and a new one started (`UI/Store/`) |
| `AppLogging` | UI | Picks the log folder (`C:\DISTR\Logs\SemiSpec`, or the fallback when the write check or the logger's open there fails), creates it and the Serilog logger: `semispec.log`, 5 MB per file, 5 files kept, shared between processes, invariant culture, process id in every line |
| `LogLevelControl` | UI | Wraps the logger's `LoggingLevelSwitch`; `IsVerbose` moves it between Information and Debug |
| `AddUi(LoggingSetup, AppDataPaths)` | UI | The composition root: registers Microsoft.Extensions.Logging over the Serilog logger, the log level control, `TimeProvider.System`, `AvaloniaScheduler.Instance` as the `IScheduler`, the port provider, `SerialLink` built with `DefaultScheduler.Instance`, `FrameDistributor` over `SerialLink.Frames`, the `StoreFile` `spectra.db` under the store folder, `SpectrumStore`, `SpectrumHistory` over the `StoreFile`, `IntensityCache`, `LiveSpectrumSource`, `SwitchingSpectrumSource` over it, the message panel, the connection row, the status bar, the spectrum view model, `TrendChartViewModel`, `OverviewViewModel`, `TrendFeed` built over `SpectrumHistory.IntensityPointsAsync`, `SpectrumAtAsync` and `SpectrumStore.Opened` with `DefaultScheduler.Instance` for its throttle, `TrendViewModel` and the main window view model, all singletons |
| `SingleInstance` | UI | The claim on one copy per Windows session: a named mutex in `Local\`, the ask and answer events through which a second launch has the first copy activate its window, the listener that runs the activation on the UI scheduler and answers, and the release (`UI/Instance/`) |
| `SecondLaunch`, `NativeMethods` | UI | What a launch that finds a running copy does: asks it in rounds of 2 s for up to 10 s, closes its claim after each ask and claims the name again, logs one line, and returns an exit code, or the new claim when the name became free; with no answer in the budget it shows the not-responding message box with no handle open; `NativeMethods` holds the three `user32.dll` calls `MessageBoxW`, `AllowSetForegroundWindow` and `ShowWindow` (`UI/Instance/`) |
| `Program` | UI | Entry point: paths, logging, the single-instance claim and the second launch, container, `App.Run` with the logger for unhandled exceptions and the claim |
| `App` | UI | Builds the Avalonia app with the exception handler, subscribes dispatcher exceptions, sets Semi's control strings to the UI culture, starts the single-instance listener, creates the main window from the container, refreshes the port list and starts the spectrum store, the trend feed and then the frame distributor when the window opens, and at the first close releases the claim and stops the link, the distributor, the trend feed and the store, in that order, before it closes |
| `MainWindowViewModel` | UI | Exit request, the verbose-logging command, and the panel, connection row, status bar, spectrum and trend view models; it disposes none of them, the container owns them |
| `ConnectionBarViewModel` | UI | The port list, the selection and the Refresh, Connect and Disconnect commands; reports a failed listing to the panel |
| `AppStatusBarViewModel` | UI | The status bar texts and dot tone from `SerialLink.Status`, and the dropped-frames text from the link's and the store's counts; reports each fault and the restored entry to the panel |
| `MessagePanelViewModel` | UI | The bounded, newest-first list of messages the operator reads, the newest entry and the count of the rest, and whether the panel row is shown |
| `MessageSeverity` | UI | Info, Warning or Error on a message; the panel picks the dot class with `ObjectConverters.Equal` against each value |
| `ResultReporting`, `ErrorMessageMapper` | UI | The route from a failed `Result` to the panel and the log |
| `UnhandledErrorObserver` | UI | ReactiveUI's last-resort exception handler and the dispatcher's unhandled-exception handler |

`board-protocol.md` describes the link's read loop, health rules, drain, reopen and channel.

Core holds every algorithm that needs no UI type: the projection and the scale rules under `Core/Spectrum/`, the log
axis ticks under `Core/Chart/`, the store files, database, intensity rules and queries under `Core/Store/`, and the
trend's continuity rule, cache, history plan, navigation, column mapping and overview geometry under `Core/Trend/`.
The UI keeps what touches ScottPlot or Avalonia: `LogTickGenerator`, `ChartPalette`, the chart view models, the
envelope plottable and the failure strip; and what joins the app's lifecycle and message panel: `SpectrumStore`,
`StoreWriter` and the store errors under `UI/Store/`, `TrendFeed` and the trend errors under `UI/Trend/`. The store
benchmark references Core only, so it times the same cache and plan the app runs.

## Startup

`Program.Main` runs these steps in order:

1. `AppDataPaths.Under` resolves the store folder and the fallback log folder from
   `Environment.SpecialFolder.LocalApplicationData`.
2. `AppLogging.Create` picks the log folder and creates the logger there; `Program.Main` sets `Log.Logger` to it
   (Log folder, below).
3. `SingleInstance.Claim` claims the copy's name (Single instance, below). A second copy calls
   `AllowSetForegroundWindow` and runs `SecondLaunch.Run`. It returns that exit code, so it builds no container and
   opens no window, port or store, or it continues as the primary when the name became free. The primary names the
   log folder in its Information line "SemiSpec starting".
4. `AddUi` builds the service provider with the paths; the store's history file is named here, and nothing touches
   the store folder until the window opens.
5. `App.Run` receives a logger from the container's `ILoggerFactory`, which writes to that logger, and the claim; it
   builds the `UnhandledErrorObserver` with the container's `IScheduler` and builds the Avalonia app. In `AfterSetup` it
   subscribes `Dispatcher.UIThread.UnhandledException`, which exists only once the platform is set up, `App.Configure`
   resolves `MainWindowViewModel`, `MessagePanelViewModel`, `SerialLink`, `FrameDistributor`, `TrendFeed`,
   `SpectrumStore` and the app's logger, and the claim starts its listener on the container's `IScheduler`.
   `OnFrameworkInitializationCompleted` shows `MainWindow` with that view model as its `DataContext`.

A failure in step 1 or 2 ends with exit code 1 and shows nothing, because there is no log to write to. A failure after
the logger exists is logged as Fatal and ends with exit code 1.

### Log folder

The log folder is `AppLogging.SiteLogFolder`, `C:\DISTR\Logs\SemiSpec`, the folder where SemiPlot and SemiStep also
log on an operator PC. The installer creates it with modify rights for users (Release). Before the logger exists,
`AppLogging.CanWrite` creates the folder and opens `semispec.log` for append with `FileShare.ReadWrite |
FileShare.Delete`, which the shared file sink of a running copy allows, so a second copy passes the check. When the
check fails with an `IOException` or `UnauthorizedAccessException` (a dev run without the installer, a folder without
rights), or when the logger cannot open the site folder after the check passed, the log goes to
`%LOCALAPPDATA%\SemiSpec\Logs`, and the first log line is a Warning that names both folders. The app shows no notice
on screen for the fallback.
The check is a `Func<string, bool>` parameter of `AppLogging.Create`, so the tests choose its answer and never touch
`C:\DISTR`. A user who may create folders under `C:\` gets the site folder even without the installer. The spectrum
store stays under `%LOCALAPPDATA%` (Spectrum store).

No ReactiveUI object is constructed before `AppBuilder.Setup()`. ReactiveUI takes its exception handler once, on first
use, so a command or property helper built earlier would make the handler that `BuildAvaloniaApp` installs a silent
no-op. The view models come from the container inside `AfterSetup` for this reason.

The window's `Opened` event refreshes the port list once, starts the spectrum store, then the trend feed, then the
frame distributor, so the store's and the feed's subscriptions exist before the first frame is read. The feed's start
read waits for `SpectrumStore.Opened` before it reads the store (Trend, Key change and re-read), so no reader opens a
file the writer may still delete, set aside or create. The window's
`Closing` event is where an operator close (the close button, File, Exit) stops all four. The first `Closing` that
starts the stop releases the single-instance claim before it starts it (Single instance).
`StopBackgroundServicesAsync` awaits `SerialLink.DisconnectAsync()`, then `FrameDistributor.StopAsync()`, then
`TrendFeed.StopAsync()`, then `SpectrumStore.StopAsync(drainToken)`, all inside one 5 s limit of real time; a timeout
logs a Warning. The link stops first, so no frame enters the channel after the distributor has stopped reading it, and
the distributor stops before the store, so no frame reaches the store after its queue has closed. The trend feed
stops in between, so no trend read holds a reader snapshot when the store checkpoints. The trend feed's stop ends
within one 256-row token check of each tracked read, and ends the start read's wait for `Opened` at once, so it adds
milliseconds. Each stop runs in a `finally`
of the one before, so the later services stop even when an earlier stop throws; any failure logs an Error, and the
window closes anyway, because the stop task never faults. The store's drain token fires 1.5 s before the 5 s limit
ends. From the store's stop on it starts no retention pass, and a held lock fails each statement it starts after about
1.15 s (the 1 s busy timeout of the stop plus Microsoft.Data.Sqlite's 150 ms retry step); a statement already running
when the store's stop begins, at most 2 s into the close, keeps up to 2.15 s. After the drain token the store starts
no batch, counts what is still queued as dropped and checkpoints, which never waits. Worst case a batch starts just
before the drain token at 3.5 s and ends at about 4.65 s, so the store's close ends about 0.35 s inside the limit.

- When the stop has completed by the time the first `Closing` checks it, the close goes ahead. That happens only when
  none ran, before `Opened`.
- Otherwise that `Closing` cancels the close, awaits the stop and closes the window again. After `Opened` this is every
  close: the distributor's read task ends on the thread pool, and an active link's `DisconnectAsync` cancels through
  `CancelAsync` and the session finishes there too. A close that arrives while the stop runs stays cancelled and
  issues no second close.
- An OS shutdown closes the window through the same event, but a window cannot prevent it, so the stop is best-effort
  there.

The container's dispose after `App.Run` returns calls `SerialLink.Dispose()`, `FrameDistributor.Dispose()`,
`TrendFeed.Dispose()` and `SpectrumStore.Dispose()`, which cancel what still runs without waiting for it; the store's
writer then closes its file without a checkpoint. After a 5 s timeout the session can outlive the window: `Program`
then calls `Log.CloseAndFlush()` while the session may still write its last lines (the port close, "The link on port
... stopped"), and those lines are not guaranteed to reach the log file. The process exits regardless.

### Single instance

One copy runs per Windows session: the board has one data source and one COM port, and the history file takes one
writer. `SingleInstance.Claim` creates or opens a named `Mutex` in the `Local\` namespace with
`SingleInstance.ProductionName`, a fixed name with a GUID suffix, and reads `createdNew`: true makes the primary, false
a secondary. The claim never waits on the mutex. The OS destroys the mutex when its last handle closes, also after a
crash, so a dead copy never blocks a start. `Local\` is the session namespace: a copy in another Windows session has
its own desktop, where the first copy could not show its window, so it runs as its own primary and meets
`PortBusyError` on the port the first copy holds. A claim the OS refuses (`UnauthorizedAccessException`,
`WaitHandleCannotBeOpenedException`) logs a Warning and runs as an unguarded primary.

Both copies create or open two auto-reset events in the claim, `Local\<name>-activate` (the ask) and
`Local\<name>-activated` (the answer), so their creation order does not matter. The primary's listener is a
`ThreadPool.RegisterWaitForSingleObject` on the ask; each ask schedules the activation on the UI scheduler and sets the
answer after the activation ran there, so an answer shows that the UI thread runs. An activation that throws is logged
at Error and still answered. The listener starts in `AfterSetup`, because `Dispatcher.UIThread` exists only there; an
ask that arrives earlier stays set and runs once the listener starts.

The activation runs on the UI thread. When the main window is minimised, `ShowWindow(handle, SW_RESTORE)` on its Win32
handle brings it back to its state before the minimise, maximised included; a window that is not minimised skips it,
because `SW_RESTORE` would un-maximise it. Then `Window.Activate()` brings it to the front. Windows lets only the
foreground process hand the foreground on, so the secondary, the just-launched process, calls
`AllowSetForegroundWindow(ASFW_ANY)` before it asks.

`SecondLaunch.Run` works in rounds within a 10 s budget. Each round resets the answer, sets the ask and waits 2 s on the
answer; the reset means a late answer to an earlier launch cannot answer this one. After every ask the launch disposes
its claim, the mutex and both events. An answer logs one Information line and exits with code 0. Without an answer the
launch claims the name again: a free name means the first copy closed or died, so the launch logs one Information line
and `Program.Main` continues the normal start-up as the primary with that claim; a held name starts the next round.
The rounds cover three cases: a first copy on a cold start that has not reached `AfterSetup` yet, a first copy whose
close has begun, and two launches at once that lose an answer, because their asks may join into one signal, one
launch's reset can clear the other's answer, and one answer releases one waiter. When the budget runs out the launch
logs one Warning, shows `SingleInstanceNotResponding` in a Win32 `MessageBoxW` with the caption `SemiSpec`, since no
Avalonia window opens, and exits with code 1; a hung primary therefore shows the message after about 10 s. The launch
holds no handle while the message is up: the claim is the existence of a handle, so a held handle would keep the name
taken after the operator kills the hung copy, and the restart the message asks for would fail again. The three
`user32.dll` calls
are classic `DllImport` declarations in `NativeMethods` with an explicit `CharSet` and
`DefaultDllImportSearchPaths(System32)`; `LibraryImport` would need `AllowUnsafeBlocks` for the string parameter.

The first `Closing` that starts the stop calls `SingleInstance.Release()` first: it unregisters the listener and
disposes both events and the mutex handle, so a launch from then on claims the name as a new primary instead of
activating a window that is about to close. The release comes before the stop, not after it, because until the release
the listener still answers: a launch during the stop would activate the closing window, exit with code 0 and leave the
operator without a running copy. The cost is an overlap of up to the 5 s stop limit. The closing copy may hold the
port for that long; Connect in the new copy fails with `PortBusyError` and the link reopens the port every second until
the old copy releases it. The new copy's store open may meet `spectra.db` held by the old copy's last batch or closing
checkpoint; the open retries (Spectrum store, Files). `Release` is idempotent; `Program` disposes the claim again at
exit.

## Main window

`SemiSpec/SemiSpec.UI/MainWindow/MainWindow.axaml` has five rows, top to bottom:

| Row | Height | Content |
| --- | --- | --- |
| Menu | `Auto` | `AppMenuBar`: File, Exit; View, Message panel, Verbose logging |
| Connection row | `Auto` | `ConnectionBar`: label "Port", a 240 px port list, Refresh, Connect, Disconnect; then a 1 px separator, Back to live and Follow live |
| Content | `*` | `ContentArea`, columns `*,Auto` and rows `3*,2*,Auto`: `SpectrumPane` with `SpectrumChartView` and the 312 px `SignalPane` with `SpectrumPanel` in the first row; `TrendPane` with `TrendChartView` and `OverviewPane` with `OverviewView` across both columns in the second and third rows |
| Message panel | `Auto` | `MessagePanelView`, `MainWindow.MessagePanelHeight` 93 px high, shown while `MessagePanelViewModel.IsVisible` is true |
| Status bar | `Auto` | `AppStatusBar`: dot and link text, port, ATP-2000, ATP-8000, lost spectra, dropped frames |

The window opens centred at `MainWindow.OpeningWidth` 1280 by 800. On a screen whose work area is smaller,
`OnOpened` shrinks the window into the work area, frame included, never below the minimum size, and centres it there.
Exit is view work: the view model emits on
`ExitRequests`, and the window subscribes in `OnLoaded`, unsubscribes in `OnUnloaded`, and closes itself.

`MainWindow.MinimumWidth` is 1256 px, the window's `MinWidth`. It is the widest of the rows with their longest texts and
the content row's floor, rounded up to the 8 px grid: the Russian status bar with `COM256`, six-digit lost and dropped
counts and "Порт недоступен, переподключение" measures 1252.2 px (English 1085.2 px), the connection row with Back to
live and Follow live 1049.3 px in Russian (English 835.4 px), and the readout pane plus a 400 px chart floor 712 px. At
400 px an ATP-8000 spectrum keeps at least four labelled wavelength ticks; the measured axis shows eight, 1000 to
2400 nm. At the minimum width the spectrum chart gets 944 px (1256 minus the 312 px pane) and the trend the full
1256 px, where 10 min of trend keep at least four labelled time ticks. The
headless text stub gives no real widths, so the `MainWindowTextFit*Tests` add the realised row chrome to each text
measured through SkiaSharp at 14 px in the widest of Inter and every installed family of Semi's font chain; on Windows the app
resolves that chain to Microsoft YaHei. The same test holds `MinimumWidth` at or under `OpeningWidth` and checks that
the realised window opens `OpeningWidth` wide, so the window opens with every row whole. Both rows stay left-aligned at
any width above the minimum, and no link state changes the set of rows or a row's height.

`MainWindow.MinimumHeight` is 688 px, the window's `MinHeight`: a maximised window on a 1366 x 768 screen gives a
client height of 689 px, and every row fits there with the message panel shown. The row floors are
`SpectrumRowMinHeight` 192 px and `TrendRowMinHeight` 208 px, each a 120 px data area under the plot's axis margins,
the 24 px top margin of the state line included; the data floor is 120 px because two 200 px floors would need 703 px
of client height before the message panel. Above their floors the spectrum and trend rows split the content row
3 : 2; the spectrum takes the larger share because it is the primary live view. `MainWindowChartHeightTests` measures
the rows and prints them:

| Client size | Message panel | Spectrum row (data) | Trend row (data) |
| --- | --- | --- | --- |
| 1280 x 800 | hidden | 389 (313.8) | 260 (183.8) |
| 1280 x 800 | shown | 334 (258.8) | 222 (145.8) |
| 1366 x 689 | hidden | 323 (247.8) | 215 (138.8) |
| 1366 x 689 | shown | 237 (161.8) | 208 (131.8) |
| 1280 x 1024 | hidden | 524 (448.8) | 349 (272.8) |
| 1280 x 1024 | shown | 468 (392.8) | 312 (235.8) |
| 1280 x 688 (minimum) | shown | 236 (160.8) | 208 (131.8) |

The Verbose logging item reads `IsVerboseLogging` one-way and writes only through `ToggleVerboseLoggingCommand`, so the
command is the level's single writer. `LogLevelControl` raises no notification; the command raises
`IsVerboseLogging` after it flips the switch. The level resets to Information at every start. Each toggle writes
an Information line with the new minimum level and a Debug line; the Debug line reaches the log file only when the
toggle turned verbose logging on, so the operator sees the switch take effect in the file. The Message panel item is
a check item over `MessagePanelViewModel.IsVisible` and writes only through its `ToggleCommand` (Error route).

### Content row

The charts are the elements that stretch. `SpectrumPane` and `TrendPane` hold their charts edge to edge with no
padding, because ScottPlot draws its own axis margins over the panel background. `TrendPane` and `OverviewPane` each
carry a 1 px top border in `AppBorderBrush`; the overview is a 36 px strip, so its row is 37 px. The trend and the
overview span both columns, so a wide window gives its extra width to the spectrum chart, the trend and the overview,
and the readout pane keeps its width.

`SignalPane` is a `Border` with a 1 px left border and `Padding="16,12"`, `SpectrumPanel.PaneWidth` wide, beside the
spectrum chart only. Inside it a `ScrollViewer` (`SidePaneScroll`) scrolls vertically only, so the pane scrolls
instead of clipping when the spectrum row is at its floor. `SpectrumPanel` is one form grid: a label column, a 12 px
gap and a field column of at least 120 px, the number width, with 8 px between rows. It holds the seven label and value
rows of the readout (The readout pane) and no header and no control: the chart's options sit in the axis flyouts (The
axis flyouts).

Every text in the pane stays on one line: a pane-scoped style sets `NoWrap` on every `TextBlock`. `PaneWidth` is
312 px: the 1 px border, 16 padding, the label column, 12, the field column, 16 padding and 24 px slack, rounded up to
the 8 px grid. Russian sets it: labels 117.5 px for "Окно измерения", values 92.1 px, the field column at its 120 px
floor, 306.5 px in all (English 91.0 and 92.0 px, 280 px). The longest values ("-1,23457E-38", "65535 nm", "±255 nm",
"23:59:59.999") fit the 120 px floor. The `MainWindowTextFit*Tests` measure them the same way as the status bar, in the
labels' face, which every label and value shares.

The message panel row takes its height from the content row: the charts and the pane lose that height and keep their
width, and the window keeps its size. No spectrum or trend state changes the window size or a pane's bounds.

### Connection row

The port list and Connect are enabled only while the link is Disconnected; Connect also needs a selected port.
Disconnect is enabled in every other state, and Refresh in every state. `IsIdle` follows
`SerialLink.StatusUpdates(uiScheduler)`, which starts with `SerialLink.Status` and moves every later change to the UI
scheduler, so it is right from construction. Refresh reads the port list again and keeps the selection when its port is
still listed; a listing that fails shows an empty list and reports a `PortListError` to the message panel. With no ports the list shows the placeholder "No ports found"; the placeholder is empty while ports are
listed. Connect and Refresh are synchronous commands; Disconnect awaits `DisconnectAsync`.

Back to live and Follow live follow the connection row after a 1 px separator, the place SemiPlot gives its live
navigation in the top bar. Back to live is `SpectrumViewModel.BackToLiveCommand`, enabled only while a past moment is
shown (A past moment); Follow live is `TrendViewModel.FollowLiveCommand`, enabled only while the trend is not
following (Navigation). Both stay in the row in every state, so the row never changes width.

### Status bar

`AppStatusBarViewModel` follows `SerialLink.StatusUpdates(uiScheduler)` the same way as the connection row and holds the
latest `LinkStatus`; every text derives from it, and the dropped count adds the store's count to it:

| Segment | Text |
| --- | --- |
| Link | One text per `LinkState`: "Not connected", "Connecting", "Port unavailable, retrying", "Waiting for data", "Receiving data", "Board not responding", "Other device" |
| Port | The port name; "-" while disconnected |
| ATP-2000, ATP-8000 | While Receiving: "connected", "not connected" or "measurement failed" (connected and failed bits set) from the latest frame's `state`; "no data" in every other state |
| Lost spectra | The total since Connect while Receiving; "-" in every other state |
| Dropped frames | "Dropped frames: {0}", the frames the link's channel dropped plus the frames the store lost, since the start; a number in every state, also while disconnected |

The link keeps the last `state` byte and the lost count after it leaves Receiving, so the spectrometer and lost
segments read them only while Receiving; outside it they would show a board that no longer answers as connected.

Lost spectra are board spectra that never arrived, counted from `seq`. Dropped frames arrived but are missing from the
stored history, so the count covers the run, not the connection. `DroppedFramesText` is an
`ObservableAsPropertyHelper` over `CombineLatest` of `DroppedFrames` of the `Status` property, through `WhenAnyValue`,
and the count each `SpectrumStore.DroppedFramesChanged` emission carries, moved to the UI scheduler and started with
`SpectrumStore.DroppedFrames` at construction, summed, with repeats dropped, and formatted with `Resources.Culture`.

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
   `Volatile.Write`; then it calls each subscriber's `OnNext` inside its own try/catch (distributor task). The
   subscribers are `LiveSpectrumSource`, the spectrum store and the trend feed.
3. `LiveSpectrumSource` passes spectrum frames on as `CurrentChanged`, and `SwitchingSpectrumSource` passes them on unchanged
   (distributor task).
4. `SpectrumViewModel` moves the signal to the UI scheduler, reads `Current`, drops a repeat of the same measurement,
   calls `Chart.Show` and sets `SignalInfo.Spectrum` (UI thread).
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

`SpectrumViewModel` subscribes in its constructor with `CurrentChanged.ObserveOn(uiScheduler).StartWith(...)`, then a read of
`source.Current`, `WhereNotNull` and `DistinctUntilChanged` on the measurement by reference, then `Show`. Signals queued
while the UI thread is busy each read the same latest measurement, so the chart applies the latest spectrum once without
comparing pixel arrays (record equality on `SpectrumMeasurement` compares every pixel). A spectrum present at
construction shows at once. A throw from the `Current` read or from `Show` goes to `TryReportFailure` and skips that
spectrum only; the next signal reads and shows again. Only a failure of `CurrentChanged` itself ends the subscription, through
the same report. The Invert and Logarithmic subscription reports a failure the same way. The view model is a container
singleton, so the subscriptions live for the process.

### A past moment

`SwitchingSpectrumSource` wraps the live source and is the source the spectrum and trend view models read.
`Current` is the historical spectrum while one is shown, the same record on every read, and the live `Current`
otherwise. `CurrentChanged` merges the live `CurrentChanged` with a private subject that emits on each mode change.
`ShowMoment(measurement, receivedAt)` and `ReturnToLive()` run on the UI thread and set `ShownMoment`, a
`DateTimeOffset?` that raises its change. `ShowMoment` copies the measurement with `with { }`, so every shown moment
carries a new `SpectrumMeasurement` around the same immutable pixel array, and `DistinctUntilChanged` by reference
shows every click, also a second click on the same moment. While a moment is shown, live signals read the same
historical record and `DistinctUntilChanged` drops them. `ReturnToLive` while live does nothing.

While a moment is shown, the chart's state line starts with "Spectrum from HH:mm:ss" in local time (The view).
`BackToLiveCommand` calls `ReturnToLive` and is enabled only while `ShownMoment` is set; its button sits in the
connection row. The Updated row of the readout pane shows the shown spectrum's receive time, so it shows the moment's
time too. The trend reads the same `ShownMoment` and draws a line at it (Trend).

### The chart

`SpectrumChartViewModel` builds one `SignalXY` per spectrometer, with the device's wavelengths from
`SpectrometerCalibration.WavelengthOf` as `xs` and its own `ys` buffer; only the shown device's line is visible. The
window is a `HorizontalSpan` from `WavelengthNm - Delta` to `WavelengthNm + Delta`, and a `VerticalLine` marks
`WavelengthNm`. When that window does not overlap the device's wavelength range, both are hidden, and with `Delta` 0
only the centre line is drawn; the readout pane still shows the wavelength and the window. The axis labels come from
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
| Before the first spectrum | 185.9 .. 2464.3 nm, the first ATP-2000 pixel to the last ATP-8000 pixel | `0 .. 1` | `0 .. 1` |
| Following | `xs[0] .. xs[P - 1]` of the shown device | `min - 5 % .. max + 5 %` of the reference's span | the same, in decades |
| Following, flat reference (`min == max`) | same | `v - 1 .. v + 1` | `v - 0.5 .. v + 0.5` |
| Following, From zero | same | `0 .. max x 1.05`; `0 .. 1` when `max <= 0` | `0 .. max x 1.05` in decades; `0 .. 1` when `max <= 0` |
| Frozen | unchanged | unchanged | unchanged |

`max` is the largest displayed value of the reference, after inversion and the log step. `FromZeroRange` (Core/Chart)
holds the From zero rule, and the trend and the overview share it. From zero is a mode of the following scale, off by
default: every spectrum sets the limits again while it is on, and a zoom or a pan still freezes the scale.

The reference is the spectrum the Y limits come from: the shown spectrum, or, while a dark frame is shown, the last
non-dark measurement, projected again in the current inversion and axis mode. A dark frame is one whose raw pixels are
all zero. The chart keeps the last non-dark measurement of each device, and a dark frame takes its own device's. With
no earlier non-dark frame of that device the dark frame is its own reference and takes the flat range: the other
device's intensities say nothing about this one. So a dark frame while following keeps the Y limits of the spectrum
before it, From zero on a dark frame takes that spectrum's maximum, and a log or inversion toggle during a dark frame
shows that spectrum's limits in the new mode; X follows the dark frame's device in every case. On the log axis the
dark line lies at 0 decades, below a range kept from a spectrum whose minimum is above 1, so the data area is empty for
that frame.

Only a live spectrum writes the last non-dark measurement, also when a toggle or Autoscale projects a historical one
again, so a moment from the past never becomes the dark-frame reference; a historical dark spectrum takes the live
reference of its device, or the flat range without one. Each change that ends with the scale frozen records the shown
spectrum's device as the frozen device. A historical spectrum, and the first live spectrum after it, keep the frozen
limits when their device is the frozen device and return the scale to following otherwise, so a moment of the other
device never leaves the plot blank, and the operator compares moments of one device at one zoom.

The scale follows from the start, after Autoscale returns it to following and after a log, inversion or From zero
toggle. Once a spectrum is shown it freezes on a wheel step over the plot, on a pointer move with a button held 5 px or
more from the press, on Autoscale while following, and on typed bounds in an axis flyout. Before the first spectrum
none of these inputs freezes it, and typed bounds change nothing (`HasSpectrum` guards `TakeManualScale`,
`ToggleAutoscale`, `SetVerticalRange` and `SetHorizontalRange`), so the first spectrum sets the limits. Tunnel handlers for `PointerWheelChanged`,
`PointerPressed`, `PointerMoved` and `PointerReleased` on `AvaPlot` record the press and forward to the chart: a wheel
step calls `TakeManualScale()`, and a move calls `OnHeldMove(press, current, isButtonHeld)`, which decides the freeze
from the held button and the larger of the X and Y distances. The handlers run before ScottPlot's class handler, which
marks the wheel event handled. The 5 px is ScottPlot 5.1.59's own floor: `MouseDragPan` and `MouseDragZoomRectangle`
ignore a held move shorter than that, so a shorter move would freeze limits ScottPlot never moved. ScottPlot still
applies that short pan on release, and `MouseDragZoom` (right button) has no floor, so a right drag under 5 px zooms a
little; while following, the next spectrum sets the limits back in both cases. ScottPlot's `MouseDragPan` also
restores the press-time limits on release before it applies the drag, so a press held across a frame ends on limits
panned from the press-time view, not from the last frame. A bare click on the data area and a key press keep the scale
following. While frozen, a live frame from the other device keeps the frozen limits, and its line can lie outside them
until Autoscale. `IsFollowing` raises its change after `Plot.Sync` is released, so no subscriber runs under the plot
lock.

`AutoscaleCommand` toggles: while following it freezes the limits where they stand, and while frozen it returns to
following in the current mode. Its toggle button in both spectrum flyouts is checked while the scale follows, so the
button shows the state.

ScottPlot's input processor stays on with five responses: `MouseInteractWithPlottables`, `MouseDragZoomRectangle`,
`MouseDragPan`, `MouseDragZoom` and `MouseWheelZoom`. The view removes `DoubleClickBenchmark`, `SingleClickAutoscale`,
`SingleClickContextMenu`, `KeyboardAutoscale` and `KeyboardPanAndZoom`: the app is a read-only viewer, ScottPlot's
autoscaler never sets the limits, and keyboard input never moves them. A left press on an axis band opens that axis's
flyout and is marked handled, so it never reaches ScottPlot's pan or zoom (Axis flyouts).

### The view

`SpectrumChartView` holds `AvaPlot` alone inside the `PlotLayer` decorator, whose `CacheMode` is a `BitmapCache`. The
cache never goes on `AvaPlot` itself: there Avalonia 12 freezes the plot on its first frame. The view's
`DataContext` is the `SpectrumViewModel`; the view binds its chart with `PlotControl.Reset(chart.Plot)` when the
`DataContext` changes, subscribes `RedrawRequested` and attaches the two axis forms to their flyouts; disposing the
chart completes that stream and clears a pending frame. `AvaPlot.Reset` disposes the plot it replaces, so the view
skips it when the control already holds the chart's plot and leaves the control alone on a null `DataContext`: the same
chart coming back keeps its plottables. A switch to another chart still disposes the previous chart's plot, because
ScottPlot has no public way to detach a plot; the app builds one chart for the process. Each request asks
`TopLevel.RequestAnimationFrame` once per pending frame and invalidates the plot in the callback, so every request
before the next display frame joins one redraw; `ChartFrames` holds this pacing and the palette repaint for both chart
views. Nothing calls `AvaPlot.Refresh()`; ScottPlot redraws on its own only
while the operator drags or zooms. The view repaints the plot from the palette on `OnLoaded` and on the application's
`ActualThemeVariantChanged`, and removes that handler in `OnUnloaded` (`ui-theme.md`). One hold of `Plot.Sync` paints
the surfaces and the series colours, so no render sees one without the other.

Until the first spectrum a line over the plot reads "No spectrum yet. Connect to the board in the row above." After a
Disconnect the last spectrum stays; the state line and its update time tell its age, and a new Connect does not clear
it.

### The state line

Both charts reserve a 24 px top margin (`ChartStateLine.TopMargin`, the top axis's minimum size) for one line of state
text over the data area. `ChartStateLine.Place` sizes the line's band to the data area's width and the margin's height
after each served frame, and the spectrum view also after a size change, so the line starts at the data area's left
edge. The line is one `TextBlock` in `AppSecondaryForegroundBrush`, `NoWrap` with an ellipsis, hidden while its text is
empty, so no state changes the chart's layout.
At the minimum width the longest spectrum line, a data-age text with all four modes, measures 561.9 px in Russian
(English 462.8 px) over 878.2 px of data area, and the longest trend line 429.7 px (335.7 px) over 1190.2 px
(`MainWindowTextFitChartTests`), so neither line trims.

`SpectrumViewModel.StateText` joins its parts with " · " (`ChartStateSeparator`):

1. "Spectrum from HH:mm:ss" while a moment is shown, or else "No new data since HH:mm:ss" while the live spectrum is
   stale, with the receive time of the last live spectrum.
2. "Log", "Inverted" and "From zero" for each mode that is on.
3. "Scale frozen" while a spectrum is shown and the scale does not follow.

`SpectrumStaleness.IsStale` holds the stale rule: a live spectrum is stale while the link is not Receiving, or when it
is more than `LinkTiming.SilenceLimit`, 10 s, old. Before the first live spectrum nothing is stale, so the empty chart
carries no state. The text is an `ObservableAsPropertyHelper` over the modes, the scale state, `ShownMoment`, the link
status on the UI scheduler and a 1 s tick, so a spectrum turns stale on screen within a second of the 10 s.

### The readout pane

`SignalInfoViewModel` holds the shown `DisplayedSpectrum` and raises its seven texts from that one setter, which
compares by reference: spectrometer, wavelength (`{0} nm`), window (`±{0} nm`), amplitude, SNR, Q factor and the update
time. Amplitude, SNR and Q are the board's values in `G6` under the current culture; a NaN or infinite value reads "-".
SNR has no unit, because the board sends a ratio. The update time is `ReceivedAt` in the `TimeProvider`'s local zone as
`HH:mm:ss.fff`. Before the first spectrum every row reads "-", so every row exists in every state.

## Trend

### Data flow

1. The distributor hands every frame to the store's queue and to `TrendFeed` (distributor task).
2. `TrendFeed` calls `IntensityCache.Add`: a point, a break, a failure mark, or a key change that raises
   `ReReadRequested`.
3. `IntensityCache.Changed`, sampled every 100 ms on the UI scheduler, refreshes `TrendChartViewModel`; while
   following, the window's right edge is now, refreshed at least once a second. The overview places the window after
   each refresh.
4. A key change, throttled at 500 ms, snapshots the visible window on the UI thread and starts one re-read on the
   thread pool: the plan's ranges through `SpectrumHistory.IntensityPointsAsync`, each chunk landed in the cache. A
   newer key change cancels it.
5. At start, once `SpectrumStore.Opened` completes true, the feed reads the last stored spectrum before the cache's
   `RunStart` and asks the cache for the stored day at its key; that request takes the same throttled read as a key
   change.
6. A click on the trend reads the spectrum of that moment, from the cache's recent spectra or the store, and shows it
   through `SwitchingSpectrumSource`.

The trend's intensity of a spectrum is the mean of its pixels in the `SpectrometerCalibration.Window(nm, delta)` of its
device, dark frames included, the value the store's series computes. The key is the `(WavelengthNm, Delta)` of the
latest spectrum frame.

### The intensity cache

`IntensityCache` holds, under one lock, the points `(UnixMs, Value)` in time order, one per spectrum, NaN for a
spectrum whose device does not cover the wavelength; the run's breaks `(UnixMs, Lost)` and failure marks
`(UnixMs, State)`; the key and its `QueryWindows`; a `FrameContinuity`; and the recent spectra, the spectrum frames of
the last 120 s by millisecond receive time, held by reference, since the pixel arrays are immutable.

`Add(ReceivedFrame)` passes every frame to the continuity rule and keeps its breaks and marks, then for a spectrum:

- the first spectrum sets the key and asks for nothing, unless the stored day request already set a key (Key change
  and re-read);
- a spectrum at the key adds one point: `QueryWindows.MeanOf` over the frame's window when the device covers the
  wavelength, NaN otherwise;
- a spectrum with another wavelength or `delta` changes the key (below).

After each add, after each landed chunk and before each `FoldCurrent`, it drops the points, breaks and marks older
than 24 h before `TimeProvider.GetUtcNow()`, the store's retention rule, so loaded history leaves the chart after a
day also while no frame arrives. `Changed` signals after every added frame and every landed
chunk; `HistoryLanded` signals after a landed chunk, just before its `Changed`. They and `ReReadRequested` are private
subjects exposed with `AsObservable()`. Every emission happens after the lock is released, one at a time under a
second lock. `Dispose` sets a flag under the first lock, so a frame or a chunk that arrives after it changes nothing,
and disposes the subjects under the second lock, so a frame or a chunk in flight at the dispose raises nothing.

`Fold(IntensityQuery)` finds the first point, break and mark at or after `From` by binary search and feeds the range
into `IntensityBuckets`, so the cache and the store fold one way on the same epoch grid, and a 10 min fold costs the
points of 10 min. Every fold the trend and the overview take sits on that grid, so two folds of one width share their
full columns. `FoldCurrent(queryFor)` reads the extent (the key, the oldest and the newest point), builds the query
from it and folds, all under one hold of the lock, so a fold never pairs with the extent of another frame; the chart
and the overview fold this way, and `Extent` alone serves the overview's window placement. `SpectrumAt(moment)` answers
from the recent spectra: the spectrum at or before the moment, its measurement shared with the frame, or null before
the oldest recent spectrum; `SwitchingSpectrumSource.ShowMoment` makes the new measurement. At 2 spectra per second a
day of
points takes about 3 MB.

### Continuity

Two 10 s rules use the link's silence limit, `LinkTiming.SilenceLimit`, and they measure different silences: the
`seq` rule of `FrameContinuity` measures the silence between frames of any kind, and the NoSpectra rule of the fold
measures the gap between points.

`FrameContinuity` sees every frame in receive order, spectra and heartbeats, and owns a `SequenceTracker`:

| Case | Result |
| --- | --- |
| the first frame | the `seq` baseline; nothing |
| more than 10 s after the previous frame of any kind | `SequenceTracker.Reset()`; this frame is the new baseline, so the `seq` step across the silence is no loss |
| up to 10 s after the previous frame, the tracker counts k > 0 lost | a Loss break at this frame with k lost |
| the measuring device's measurement-failed bit in the frame's `state` | a failure mark at this frame with only that bit |

Every spectrum, live or stored, is one point, NaN for an uncovered device, so `TrendTimeline.Fold` takes the NoSpectra
rule from the points. It compares each point in range with the point before it, also the one just before the range,
and adds a break with 0 lost at a point more than 10 s after its predecessor, whatever heartbeats came between;
exactly 10 s is no break. NaN points count as spectra. One rule covers a live silence, the time the app was closed and
the closed periods inside the stored day. While a re-read's chunks land, an hour not read yet shows as a break until
it lands. Frames the store dropped (a full queue, a failed batch) are absent from the file, so in loaded history a
dropped stretch of more than 10 s folds as a NoSpectra break although the board sent spectra; in the live run the
same stretch draws as a line.

The measuring device is the device of this spectrum, or of the last spectrum before a heartbeat; before the first
spectrum no device has measured and no bit marks. The board measures with one spectrometer, chosen by the wavelength
(below 904 nm the ATP-2000, from 904 nm the ATP-8000), and keeps the idle device's failed bit until that device
measures again, so the idle bit rides every frame of the other device. The store keeps each frame's `state`
unchanged; only the trend's marks drop the idle bit.

A reconnect, an unplug, an emulator restart after 10 s and a stopped board all fall under the reset. A board restart
inside 10 s shows as a Loss break with the link's count (`board-protocol.md`, Sequence counter), and a frame the link's
channel dropped leaves a `seq` gap in the distributor stream, so it breaks the line too. One spectrum after a
heartbeat-only stretch can carry both a NoSpectra and a Loss break. A break's kind is its lost count: 0 ends a stretch
without spectra. The fold counts both kinds into the column's `BreakCount`, sums the lost spectra into `LostCount` and
counts the breaks with none lost into `NoSpectraCount`.

Loss breaks and failure marks come from `seq`, time and `state`, never from the wavelength or `delta`, so they belong
to the run, not to the key: the cache keeps them for its whole life, and a key change recomputes point values only.
The store's points read returns points only, so the stored day of earlier runs brings its NoSpectra breaks through the
fold but none of its Loss breaks or failure marks, although the file keeps `seq` and `state`. The store's heartbeat
table has no reader in the query service.

### Key change and re-read

A spectrum with another key raises the key generation and drops the points older than the recent spectra. The cache
recomputes the recent spectra at the new key at once and raises `ReReadRequested(generation, key, recentStart)`, so
the store is asked only for rows older than `recentStart`, the start of the recent spectra.
The store writes in batches behind its queue, so the newest rows may not be in the file yet; 120 s covers the writer's
normal lag. `recentStart` and the recent spectra share the millisecond stamps, so the store's `received_at <
recentStart` and the recent set meet with no double point. The feed logs each re-read request at Debug with its key
and its generation, which counts the requests. A return to the previous key is a change like any other and re-reads;
the 500 ms throttle joins fast changes into one read.

The stored day of earlier runs takes the same path. The cache records `RunStart`, the `TimeProvider`'s now, at
construction. `App.Configure` resolves the cache before `Opened` starts the distributor, so every live frame is
received after `RunStart`, and every stored row before it belongs to an earlier run. `RequestStoredDay(storedKey)`
does nothing once a key change raised a request (the generation is not 0), since that re-read covers the stored day
up to its own `recentStart`. Otherwise it keeps a live key if a live spectrum set one, else takes the stored key,
raises the generation and raises `ReReadRequested(generation, key, RunStart)`. The plan then runs with `newest =
RunStart` when no live point exists, so it covers `[RunStart - 24 h, RunStart)`, the visible window first. A later key
change cancels it like any re-read. When the app was closed for longer than the 10 min window, the window is empty
and the overview band fills as the hour chunks land, newest first; the chart's empty-state line stays until the first
chunk with points lands.

`LandHistory(generation, chunk)` inserts a chunk's points in time order when its generation is current and ignores it
otherwise. Chunks never overlap each other or the recent spectra, because every planned range ends at `recentStart`.
Frames the store never wrote (a full queue, a failed batch, both counted as dropped) are absent from the history after
a re-read, although the live trend showed them; the dropped-frames count in the status bar already reports them.

`TrendHistoryPlan.For(visibleFrom, visibleTo, newest, recentStart)` returns the ranges in order: the visible window
clipped to `[newest - 24 h, recentStart)` first, then the hour chunks from the window's end to `recentStart`, then the
hour chunks from the window's start back to `newest - 24 h`, each run newest first. A window inside the recent spectra
reads nothing first. The hour chunks let the trend fill progressively while a day of pixel pages is read, and let a
cancel land within one chunk.

### The feed

`TrendFeed` is a singleton over the distributor, the cache, the trend chart, the switching source, two read delegates
(`SpectrumHistory.IntensityPointsAsync` and `SpectrumAtAsync`), the store's `Opened` task, the message panel, the UI
scheduler, a throttle scheduler and its logger.

- `Start()` subscribes the re-read log, the two read pipelines, the start read and Back to live, then
  `FrameDistributor.Frames`
  with `IntensityCache.Add`, and logs a Debug line. `Add` takes a mean over at most 2048 pixels; it never touches
  SQLite and never blocks the distributor.
  `Start` after a start or a stop does nothing.
- The re-read pipeline is `ReReadRequested.Throttle(500 ms)` on the throttle scheduler, `ObserveOn(uiScheduler)`,
  where it snapshots the chart's `From` and `To` into an immutable window, then `Select(ReadHistory).Switch()`.
  `ReadHistory` is `Observable.Create` with a cancellation token that runs the plan's ranges one after another on the
  thread pool and lands each chunk; the read sees only the snapshot, and nothing reads the navigation model off the UI
  thread. A newer request disposes the read in flight, which cancels its token, and `IntensityPointsAsync` checks it
  every 256 rows. A failed range
  reports one `TrendHistoryError` (Warning) through `TryReportFailure` and ends that read; the trend keeps only the
  data since the change. A read that ends after the stop reports nothing. A read that lands every range logs one
  Information line "The trend history read for generation {Generation} landed {Points} points: visible window after
  {VisibleMs} ms, last chunk after {LastChunkMs} ms"; the visible time is that of the first landed chunk. On the
  operator PC this line of the start read is the cold-read measure.
- The start read is one `Observable.Create` with a cancellation token, tracked like every read. It awaits `Opened`
  with that token; on false it logs one Debug line and reads nothing. On true it reads `SpectrumAtAsync(RunStart -
  1 ms)`: no spectrum logs one Debug line, a failed read reports one `TrendHistoryError`, and a spectrum calls
  `RequestStoredDay` at its wavelength and `delta`. Disposing the subscription in `StopAsync` or `Dispose` cancels the
  token, so a stop ends the wait before `Opened` as well as the key read after it.
- The moment pipeline is `Select(ReadMomentFrom)`, each read hopping with `ObserveOn(uiScheduler)`, then `Switch()`,
  so a second click cancels the first read, also one already queued for the UI thread. A moment inside the cache's recent spectra needs no store read; any other moment reads
  `SpectrumAtAsync`. A found spectrum goes to `SwitchingSpectrumSource.ShowMoment`; none (before the first row) changes
  nothing and logs at Debug; a failed read reports one `TrendMomentError` (Warning). Back to live sets
  `ShownMoment` to null, and the feed then pushes a cancel into the same `Switch`, so a moment read in flight shows
  nothing. The feed also keeps the last requested moment on the UI thread, null after Back to live, and drops a read
  of any other moment.
- Every store read is tracked in a set under a lock until it ends. `StopAsync()` disposes the subscriptions, which
  cancels both pipelines, then awaits every tracked read, superseded ones included, because a cancelled read may still
  hold a reader connection, and logs a Debug line. `Dispose()` cancels without waiting, for the container's dispose.

### The chart

`TrendChartViewModel` owns the `Plot`, and `TrendPlotLayers` puts on it one `EnvelopeLine`, one `TrendMarkerStrip`, one
`VerticalLine` for the shown moment, ScottPlot's `DateTimeAutomatic` on the bottom axis over local-time OADate (`LocalTimeAxis`), and a
`TrendNavigationModel`. Every member runs on the UI thread. Before the first point the window is the 10 min before the
`TimeProvider`'s current time, and the Y limits are `0 .. 1`, so an empty chart renders finite axes.

A refresh runs on every sampled cache change, on a 1 s tick while following, and on every navigation call. While
following it moves the window's right edge to the `TimeProvider`'s now, so a silence shows as an empty span on the
right within a second. It folds the cache over `[From, To + 1 ms)` (the window can end exactly at the newest point,
and the range is half-open) at one column per pixel of the data area; the extent, the window move and the fold run
under one hold of the cache's lock (`FoldCurrent`). It maps the columns with `EnvelopeColumn.Map` and replaces the envelope's
columns, the strip's spans, the X limits, the Y limits and the Y label under `lock (Plot.Sync)`; then it requests one
redraw. A failed refresh is reported and skipped; after the chart's dispose a refresh does nothing. The column width
is `ceil(span / clamp(dataAreaPixels, 256, 2048))` in whole milliseconds, at least 1 ms. The view reports the data
area's width after each ScottPlot render that changed it, posted to the UI thread; before the
first report the chart folds to 2048 columns. The first point opens the window at now; a later change of the first
point (retention, a landed chunk) moves only the pan floor.

| Column | Envelope output |
| --- | --- |
| values, no break | one column at the column's centre: `Min`, `Max`, `Last` |
| values and a break | a gap at the column's start, then the value column |
| no values, a break or an uncovered spectrum | a gap at the column's start |
| no values, nothing else | none |

An empty column with nothing else is left out, so the path connects across columns narrower than the frame period,
and a stretch of more than 10 s without spectra breaks only through its NoSpectra break. A gap column has NaN values,
and `EnvelopePath` breaks the line there; a break in a column that also holds values cuts the connector into that
column at every zoom, so at the 24 h width one lost spectrum shows as a one-column cut. The envelope line is 1 px in
`AppAccentBrush`. A value column between two gaps with one value is a lone vertex, which a stroke cannot draw, so
`EnvelopeLine` fills a 3 px square there in the same colour; spectra more than 10 s apart, as the angle and zone
trigger modes send them, show as a row of squares. A column with a failure mark draws the strip, 4 px high at the
bottom of the data area in `AppSeverityErrorBrush` and at least 1 px wide, so one failed spectrum shows at 24 h;
failed columns less than 5 s apart join into one span, so a steady failure draws one bar instead of dashes. The readout names the failure, so
colour never carries it alone.

The Y limits follow the drawn values in one of two modes. From zero, on by default, runs Y from 0 to the drawn
maximum plus 5 % (`FromZeroRange`), so the trend shows the signal's level against zero. With From zero off, the limits
pad the drawn values by 5 % of their span; a span under 2 counts as flat and takes `c - 1 .. c + 1` around its centre
`c`, so float noise on a steady signal does not shrink the axis to fractions of a unit. The limits stay unchanged when
nothing is drawn. A range typed in the intensity flyout (`ManualVerticalRange`) holds Y there in either mode until
Autoscale or a From zero toggle clears it; `IsVerticalAutoscaled` is true while no range is held. Autoscale while Y
follows holds the range drawn now. The Y tick labels print the fraction digits each tick needs in the current culture
(`18 775,5` in Russian); ScottPlot's default rounds values above 1000 to whole numbers, which repeats labels on ticks
less than 1 apart. The Y label names the key on two lines, "Intensity" over "650 nm ±10 nm", so no pane row repeats
it: on one line the widest Russian key overruns the room the plot gives a label at the trend's floor. The time ticks
carry the time only, `HH:mm:ss` for a window of up to 20 min and `HH:mm` above, and the X label carries the date in
the culture's short date format: "Time, {date}" (`TrendAxisTimeFormat`), or "Time, {first} – {last}"
(`TrendAxisTimeRangeFormat`) for a window across midnight, in local time.

`TrendViewModel.StateText` is the trend's state line (The state line): the window width, a preset's label or hours,
minutes and seconds, then "Not following the data" while not following and "Scale frozen" while a Y range is held,
joined by " · ". While a moment is shown, the shown-moment line stands at it in `AppMarkerLineBrush`.

### Navigation

The window's width runs from 10 s to 24 h, the history the cache keeps, and opens at 10 min, following.

- While following, each refresh sets `To` to now and `From = now - width` with no clamp, so a run shorter than the
  window leaves an empty span on the left.
- A pan and a zoom place the window through the one clamp, which keeps `From` at or after the cache's first point.
- A pan or a wheel zoom that leaves now outside the window stops following; one that keeps it inside keeps
  following, so a zoom-in keeps the time under the cursor instead of snapping back. `TrendNavigationModel` owns this
  rule. While following, the drawn window ends up to 1 s before now, so `TrendChartViewModel` first moves it onto now,
  and the zoom anchor with it; a zoom-in at the right edge or a short drag toward now keeps following.
- Zoom widths snap to the ladder `10 s x 1.25^n`. A preset in the time flyout sets its width exactly, keeps the right
  edge (while not following, the first-point clamp can move it) and keeps following as it was; a typed width does the
  same.
- Typed start and end in the time flyout show exactly that window, its width clamped to 10 s .. 24 h and held at the
  pan floor, and stop following (`TrendNavigationModel.SetWindow`).
- Follow live jumps to now and follows again; it is enabled only while not following, and its button sits in the
  connection row.

`TrendChartView` turns ScottPlot's input off (`UserInputProcessor.Disable()`, `HandleMouseWheelEvent = false`) and
handles the pointer itself. A wheel notch zooms around the cursor time by 0.8 (wheel up) or 1.25 (wheel down);
Shift + wheel pans by 10 % of the width, wheel down toward newer data. A left press on the intensity axis band opens
the intensity flyout at once. Any other left press captures the pointer; a move past 4 px from the press starts a drag
that pans by the time under the pointer; a release within 4 px is a click. A click that pressed on the time axis band
opens the time flyout, and any other click passes the press time to `TrendViewModel.ShowMoment`. A lost capture ends the drag. The view serves redraw requests
once per display frame with the same `BitmapCache` layout and palette repaint as the spectrum chart view.

The pointer maths runs in device-independent pixels at any Windows scale. ScottPlot.Avalonia 5.1.59 (decompiled)
hands ScottPlot `PointerEventArgs.GetPosition`, which is in DIPs, renders into a rect of the control's DIP bounds on
the Skia canvas Avalonia has already scaled, and keeps `DisplayScale` at 1; the app leaves `Plot.ScaleFactor` at 1. So
`Plot.GetCoordinates` of a DIP position is the hover time, the zoom anchor and the click moment, and the overlay's
division by `ScaleFactor` is a division by 1. With a `ScaleFactor` s, ScottPlot's layout (`LastRender.DataRect`, the
axis panels) runs in DIPs divided by s, while `GetCoordinates` and `GetPixel` take and return DIPs; `ChartAxisRegion`
divides a DIP position by s before it compares it with the layout, and `ChartStateLine` multiplies the layout by s,
which `ChartAxisRegionTests` checks at s = 1 and s = 2. `TrendCursorOverlay` compares `GetPixel`'s DIPs with the
layout directly, which holds only at s = 1.

Until the first point a line over the plot reads "No trend yet. It appears with the first spectrum."

### The readout

An Avalonia `Canvas` over the cached plot holds the hover line, 1 px in `AppSecondaryForegroundBrush`, and the readout,
a `Border` in `AppPanelBackgroundBrush` with a 1 px `AppBorderBrush` border and 12 px text. Both follow the pointer
without a plot redraw, hide while dragging, outside the data area and on pointer exit, and move again after each served
redraw. The readout sits right of the line, and left of it when it would run past the view's right edge.

The text comes from the column under the pointer in the last fold (`TrendReadout`): the column's start in local time
as `HH:mm:ss`, then one value or "min – max" in `G6` under the current culture with "Spectra: {n}", or the gap reason,
then one "Measurement failed: {device}" line per failed device, the measuring device only (Continuity). The gap reasons are "No data", "{n} spectra lost",
"No spectra" and "Spectrometer does not cover {nm} nm". An empty column between drawn columns reads as the line there:
the gap reason of the column that ends the gap, or else the values of the column the line comes from, with the
failures of the whole empty run, so a NoSpectra gap names a failure a heartbeat inside it carried. A value column
with a break shows the break reason under its values. A column that ends a stretch without spectra and also counts
lost spectra reads "No spectra", then "{n} spectra lost". Outside the window or before the first point there is no
readout.

### The overview

`OverviewView` is a 36 px strip in `AppContentBackgroundBrush`. `OverviewViewModel.IsFromZero` mirrors the trend's
From zero, and the band then runs from zero to its maximum plus 5 %, as the trend does
(`OverviewGeometry.BandFigures`). `OverviewBand` draws the band in `AppAccentBrush` at
opacity 0.35, with its centre line at full opacity; a column between two breaks draws at least a 3 px square; the window highlight is `AppAccentFillBrush` with an `AppAccentBrush` border and at least 6 px wide; a
hover line in `AppSecondaryForegroundBrush` follows the pointer. A left press, and a move with the button held, centre
the trend window on the time under the pointer, clamped at the first point, and stop following.

`OverviewViewModel` folds the whole cache to 250 columns: the column width is `ceil((newest - first) / 250)` in whole
milliseconds over `[first, newest + 1 ms)`, on the epoch grid, so the first column may start before the oldest point
and the strip's left bound is that column's start. The band takes the trend's gap rule (`OverviewGeometry.Connected`),
so it breaks where the trend line breaks, not at every empty column of a short extent. It refolds at start, after each
landed chunk, after each key change, and on a cadence of `clamp(extent / 1000, 2 s, 60 s)`. It reads the cache only,
so it needs no store read and no cancellation of its own. The strip's right bound is the newest point of the last fold,
so both bounds move only when the band moves and the band always reaches the right edge. The window's place along the
strip is computed again against those bounds on each `TrendChartViewModel.RedrawRequested`, which follows every
refresh; the first redraw that finds a spectrum before any fold refolds at once.

### Lifecycle

The trend's parts are container singletons. `Opened` starts the store, the trend feed, then the distributor, so both
subscriptions exist before the first frame; the close chain stops the feed after the distributor and before the store
(Startup). The chart and overview subscriptions to the cache live for the process. After its dispose the cache ignores
frames and chunks, and the chart ignores a refresh and a posted data area width, so a read or a width that outlives
the close changes nothing.

## Axis flyouts

A left press on an axis band opens that axis's flyout at the pointer, the interaction SemiPlot uses; the forms are
ported from SemiPlot's axis scale panel and time window panel. `ChartAxisRegion` takes the left and bottom axis bands
from the last render's layout (`PanelSizes`, `PanelOffsets`); before the first render, or on a hidden axis, there is
no band. Each flyout lives in its chart view's resources:

| Flyout | Width | Rows, top to bottom |
| --- | --- | --- |
| Spectrum intensity | 300 px | the note; Maximum; Minimum; Scale type: Logarithmic; From zero; Invert spectrum; Autoscale; the message line |
| Spectrum wavelength | 300 px | the note; Start, nm; End, nm; Autoscale; the message line |
| Trend intensity | 300 px | the note; Maximum; Minimum; From zero; Autoscale; the message line |
| Trend time | 380 px | the note; Start time and date; End time and date; Width and the Presets drop-down (1 min, 10 min, 1 h, 6 h, 24 h); the message line |

The number fields are 140 px wide, the time fields 110 px. The checkboxes and Autoscale run their commands at once
(The scale; Trend, The chart).

- Opening seeds the fields from the axis as it is drawn at that moment, and the note on the first row, "Values as of
  opening", says so. The fields do not follow the live limits while the flyout is open: the spectrum redraws 2 to 3
  times a second and a following trend once a second, and a refresh would overwrite the field the operator types in.
  A mode toggle and every commit seed the fields again.
- Enter commits and keeps the flyout open; a close by a click outside commits too; Escape closes it without a commit.
  A field equal to its seed at the shown precision counts as unchanged, so an untouched flyout writes nothing, and
  an edited bound is written with the other bound as it stood at the opening.
- `AxisRangeForm` shows numbers as `0.###` in the current culture and reads them in the current culture first,
  then with a point or a comma as the decimal separator, so "1.5" and "1,5" both read as 1.5 under Russian. A pair is written only when both fields read as
  finite numbers, the minimum is below the maximum and the axis's own rule accepts it: on the log scale the spectrum
  intensity axis needs a minimum above 0, which `SpectrumChartViewModel.VerticalRangeProblem` names and
  `SetVerticalRange` checks again. The first rule broken shows on the message line, and the typed text stays.
- `TimeWindowPanelViewModel` reads times as `hh:mm:ss` in local time, dates from a `CalendarDatePicker` in
  `yyyy-MM-dd` and the width as `hh:mm:ss`. Changed edges take precedence over a changed width; an end not after the
  start, or a field that does not read, shows its message and changes nothing.
- The message line is a reserved 40 px row that wraps and ends in an ellipsis, so validation never resizes a flyout.
  The longest message takes two lines of 18.6 px in Russian at the theme font; the note takes 214.5 px of the 300 px
  row in Russian (`MainWindowTextFitFlyoutTests`).

`AxisFlyout` binds the form of the current `DataContext` and hides its flyout on the form's close request. A chart view
hides its flyouts on unload, on a `DataContext` change (before it detaches the old form, so pending fields commit as
on unload) and on the window's `Closing`, which it subscribes in
`OnLoaded` through `AxisFlyout.HideOnClosing` and leaves in `OnUnloaded`. `Hide` closes the calendars and the preset
list inside the flyout before the flyout: a window that closes with a calendar drop-down open inside a flyout makes
Avalonia 12 throw `ArgumentOutOfRangeException` from the logical-tree detach, and the app would crash on exit.

## Spectrum store

### Files

The store folder is `%LOCALAPPDATA%\SemiSpec\Spectra`, and the history file is `spectra.db` there. The file stays
across restarts, and retention keeps it at about one day of data. Before the open, `StoreFiles.Prepare` creates the folder and deletes the per-process files of older builds: each
`spectra-<digits>.db` with its `-wal` and `-shm` sidecars, and the legacy sidecars whose `.db` is gone. A name with
anything else between `spectra-` and `.db`, such as `spectra-1234 - Copy.db`, stays, and so do `spectra.db`,
`spectra.db.damaged` and `spectra.broken-*.db`, which carry no digits after `spectra-`. SQLite on Windows opens a file
without delete sharing, so the legacy file of a running older build fails its delete and stays. `Prepare` also deletes
a damage marker whose `spectra.db` is gone, so a file deleted by hand does not fail every later open. A failure to
create or list the folder fails `Prepare`; a failure on one file only keeps that file. The result is the count of
legacy files deleted, which the writer logs at Information when it is not 0. The cleanup runs at every start; after
the first it finds nothing.

A clean exit checkpoints and closes the file. A crash leaves the WAL, which SQLite replays at the next open.

The open may meet the file held by another connection: a copy of the app whose close has begun keeps it for up to its
5 s close limit (Startup, Single instance). Each open attempt waits out the 2 s busy timeout. An attempt that then fails
with `SQLITE_BUSY` (5) or `SQLITE_LOCKED` (6) is retried every 0.5 s for 4 s after the first failure, 6 s from the
start of the open, and then fails as any other open failure does. `StoreOpening` never sets aside or deletes a held
file. The stop's drain token ends the retries early.

A damaged file is set aside, never repaired. `StoreDamage.IsDamage` is true for a `SqliteException` with code 11
(`SQLITE_CORRUPT`) or 26 (`SQLITE_NOTADB`) anywhere in the cause chain; any other failure (a held lock, a file that
cannot be opened, an I/O error) leaves the file in place, because the file may be intact. The open reads only the
header and the schema page, so damage there shows at the open (Writes), and damage deeper in the file shows later, as
a failed batch, retention pass, checkpoint or read. On such a later failure the writer or `SpectrumHistory` calls
`StoreFiles.MarkDamaged`, which creates the empty file `spectra.db.damaged` and swallows its own `IOException` and
`UnauthorizedAccessException`. The running copy keeps its connection and still appends to the intact pages: a rename
under an open connection fails on Windows, the reader and the writer run on different threads with their own
connections, and only the open has the file to itself. The next open finds the marker before it opens the file and
sets the file aside as an unreadable one.

`StoreFiles.SetAside(path, localNow)` renames the closed file to `spectra.broken-<yyyyMMdd-HHmmss>.db` in local time,
the clock the operator reads, moves its `-wal` beside it as `<new name>-wal`, deletes the `-shm` (SQLite rebuilds it
from the WAL on any open) and the marker, then deletes the set-aside files beyond the newest 2, ordered by name, since
the stamp sorts. Two slots keep the latest damage and show whether it repeats, and cap the extra disk at about 1.5 GB.
No `PRAGMA quick_check` runs at the open: it reads every page, about 765 MB on a full day, before the trend can read.
`synchronous=NORMAL` in WAL mode loses at most the last transactions on a power cut and leaves the file intact, so a
damaged file means a disk fault or a foreign write, not a routine crash.

### Schema and connections

The schema is version 1, kept in the file's `PRAGMA user_version`:

```sql
CREATE TABLE spectrum (
    id INTEGER PRIMARY KEY,
    received_at INTEGER NOT NULL,   -- Unix milliseconds, UTC
    device INTEGER NOT NULL,
    wavelength_nm INTEGER NOT NULL,
    delta INTEGER NOT NULL,
    amplitude REAL, snr REAL, q REAL,   -- NULL stores NaN
    seq INTEGER NOT NULL,
    state INTEGER NOT NULL,
    pixels BLOB NOT NULL);          -- 2 x P bytes, little-endian u16, pixel 0 first
CREATE INDEX spectrum_time ON spectrum (received_at);
CREATE TABLE heartbeat (
    id INTEGER PRIMARY KEY,
    received_at INTEGER NOT NULL,
    seq INTEGER NOT NULL,
    state INTEGER NOT NULL);
CREATE INDEX heartbeat_time ON heartbeat (received_at);
```

`SpectrumDatabase.Open` reads `user_version` and the table count from `sqlite_master` in one `BEGIN IMMEDIATE`
transaction:

| `user_version` | Tables | Open |
| --- | --- | --- |
| 0 | none | creates the schema and sets `user_version = 1` in that transaction |
| 1 | any | uses the file |
| 0 | some | fails with `StoreSchemaMismatchError(found: 0)` |
| other | any | fails with `StoreSchemaMismatchError(found)` |

On a mismatch the writer deletes the file with its sidecars and its damage marker, opens again, and logs one Warning
naming both versions. No panel entry follows, since an app update causes it and the operator can do nothing about it.
The writer deletes the file instead of setting it aside: the rows are disposable 24 h history, and no build reads
another version's file. An older build with a lower `SchemaVersion` deletes a newer file the same way. A schema change
raises `SchemaVersion`; no migration code exists.

Every spectrum keeps its full pixels, because SCADA can change the wavelength mid-run and a series at any wavelength is
read from the pixels. `received_at` is not unique: the frames of one decode pass share a stamp, so `id` orders rows
with equal stamps. SQLite stores a NaN double as NULL, so the float columns are nullable and read NULL back as NaN.
`pixels` is the last column, so a read of the narrow columns never touches the blob's overflow pages. Heartbeats go to
their own table, so the series scans spectra only; a heartbeat carries the measuring spectrometer's own failure bit and
marks 3 s without a spectrum (`board-protocol.md`). No read of the query service touches the heartbeat table: the trend
takes its Loss breaks and marks from the live frames and its NoSpectra breaks from the points (Trend, Continuity), and
only retention reads the table's newest `received_at`. The store keeps writing it, for the diagnosis of a copied file.

`StoreConnection.Open` uses `Data Source=<file>;Mode=ReadWriteCreate;Pooling=False;Default Timeout=2` and runs
`PRAGMA journal_mode=WAL`, `PRAGMA synchronous=NORMAL` and `PRAGMA journal_size_limit=67108864` on every connection.
WAL lets the query service read while the writer writes. `synchronous=NORMAL` skips the fsync per commit: a power loss
can lose the last transactions but not corrupt the file, and the data is disposable. The journal limit truncates the
WAL back to 64 MB after a checkpoint. `Default Timeout=2` bounds Microsoft.Data.Sqlite's retry on a busy file to 2 s.
`OpenReader` uses `Mode=ReadWrite` instead, so a read of a missing file fails and creates nothing, and runs only
`PRAGMA query_only=ON`: the file keeps its WAL mode, and a WAL reader needs write access to the `-shm` file.

### Writes

`SpectrumDatabase` owns the writer connection. `Open` applies the version gate (Schema and connections); a
`SqliteException` with a damage code during the open, at the pragmas, the version read or the schema read, becomes
`StoreFileUnreadableError` carrying the exception, and any other `SqliteException` or `IOException` stays a plain
failure. `Write` writes a batch in one
`BEGIN IMMEDIATE` transaction with one reused parameterised command per table; `BEGIN IMMEDIATE` takes the write lock
at the start, so a held lock fails the batch there after the busy timeout and never halfway through it. A parameter
takes a `byte[]`, so each frame's pixels are copied into a new array. `ShortenBusyTimeout` drops the busy timeout of
every later statement to 1 s, the shortest Microsoft.Data.Sqlite counts, since it counts whole seconds and waits
forever on 0. `DeleteBefore(cutoff)` reads `max(received_at)` of both tables, one index seek each, inside one
`BEGIN IMMEDIATE` transaction. When both are below the cutoff or null, it runs `DELETE FROM spectrum; DELETE FROM
heartbeat;` without `WHERE`: the tables have no triggers, so SQLite takes its truncate optimisation and frees whole
pages. Otherwise it deletes the rows received before the cutoff from both tables, which visits every such row and its
index entry. On a fully expired day of 762.4 MB the fast path took a median of 77.9 ms against 908.5 ms row by row
(dev laptop, warm OS cache, 2026-11-06). The freed pages stay in the file and are reused. `CheckpointAndClose` runs
`PRAGMA wal_checkpoint(TRUNCATE)` and closes the connection whatever the outcome; it returns `WalCheckpoint.Busy` when
a reader's snapshot kept the WAL, because that checkpoint calls no busy handler and returns at once. Each operation
returns a FluentResults `Result`, with a `SqliteException` or `IOException` as an `ExceptionalError`.

### The writer service

`SpectrumStore` is a singleton over the distributor, the `StoreFile`, the message panel, the UI scheduler, a
`TimeProvider` and its logger.

- `Start()` subscribes `FrameDistributor.Frames` with an observer whose `OnNext` is one `TryWrite` to the store's
  queue, `Channel.CreateBounded<ReceivedFrame>(1024)` with `FullMode = Wait`, one writer and one reader. A full queue
  counts the frame as dropped. The subscriber never touches SQLite, never waits and never throws. 1024 frames hold
  about 6 min of ATP-2000 spectra at the port's cap of about 2.8 per second. Then `Start` runs the writer task.
  `Start` after a start or a stop does nothing. The writer task is a `StoreWriter`, which `Start` creates over the
  queue's reader and the store's count of dropped frames, and which logs through the store's logger.
- The writer task opens the file on the thread pool through `StoreOpening`, so the UI thread does no file I/O; frames
  that arrive meanwhile wait in the queue. `StoreOpening` runs `Prepare`; when the damage marker exists it sets the
  file aside before it opens it; otherwise it runs `SpectrumDatabase.Open`. A `StoreSchemaMismatchError` deletes the
  file and opens again (Schema and connections). A `StoreFileUnreadableError` or the marker sets the file aside (Files),
  opens a fresh file, logs one Warning naming both paths and the cause, and reports one `StoreFileReplacedError`
  (Warning) through `TryReportFailure`, whose line adds the panel text with the SQLite stack. A set-aside or a reopen
  that fails ends in `StoreOpenError`. Then one retention pass runs, so a file closed for days loses its expired rows
  before the trend reads it; the log line "Spectrum store file <path>" names the opened file.
- `Opened` is a `Task<bool>` from a `TaskCompletionSource<bool>` with `RunContinuationsAsynchronously`. The writer
  completes it true after the open and the retention at open, and false when the open fails or a cancel ends the
  writer before or during the open; a `StopAsync` before `Start` completes it false. The trend's start read waits for
  it.
- The loop waits for the queue or the next retention time, whichever comes first, takes up to 256 frames and writes
  them as one batch. A failed batch counts its frames as dropped, and a batch that fails with a damage code marks the
  file (Files). A failure incident runs from the first failed batch
  to the next successful one. Its first failure reports one `StoreWriteError` through `TryReportFailure`, which writes
  the one Warning line with the cause's stack; each later failure logs one Debug line without the stack and reaches no
  panel, so the panel gets one entry per incident. The next successful batch logs one Information line "Spectrum store
  writes recovered after N failed batches, M frames dropped". At 2 to 3 frames per second a Warning with a stack per
  batch would fill the 25 MB rolling log within 1 to 2 hours and roll the incident's first line away.
- Any other failure ends history for the run. A failed prepare or open, or any exception before the file is open,
  reports one `StoreOpenError`; any exception after the open, outside a failed batch, reports one `StoreStoppedError`.
  The report writes the failure's one log line, at Error, with the original exception. The writer closes the file without a checkpoint, keeps the subscription, and from
  then on counts every frame still queued and every later frame as dropped, so "Dropped frames" keeps growing while
  nothing is stored.
- Retention runs at the open and then once a minute, also while no frame arrives: `StoreRetention` reads `now` once
  and runs `DeleteBefore(now - 24 h)`, where the wait is `Task.Delay` on the `TimeProvider`, raced with
  `WaitToReadAsync`, and `now` is `TimeProvider.GetUtcNow()`, the same UTC wall clock as `ReceivedAt`. No `VACUUM` runs: the freed pages are
  reused, so the file stays at about one day of data. A failed retention pass reaches no panel,
  because no data is lost; it follows the batch incident rule: the first failure of a run logs a Warning with the
  stack, each repeat one Debug line without it, and the next successful pass one Information line "Spectrum store
  retention recovered after N failed passes". A failed checkpoint logs a Warning. A retention pass or a checkpoint that
  fails with a damage code marks the file.
- `DroppedFrames` is a count written by the distributor task and the writer task. `DroppedFramesChanged` is a private
  subject exposed with `AsObservable()` that carries the new count; the add and the emission share one lock, so the
  counts of the two threads arrive in order.
- The writer task alone owns the `SpectrumDatabase`: it opens it and closes it, with `CheckpointAndClose` after a
  normal end and a plain close after a failure or a cancellation. No other thread closes the connection, so no close
  lands inside a write, a retention pass or a checkpoint. The writer's last line, at Debug, names the stored and
  dropped counts and the checkpoint, kept as the `Result<WalCheckpoint>` of `CheckpointAndClose` or none: `Truncated`,
  `Busy` (a reader's snapshot kept the WAL, which the next open replays), `Failed` or `Skipped`.
- `StopAsync(drainToken)` disposes the subscription, completes the queue and awaits the writer, which writes what is
  still queued and checks the token between batches, never inside one. From the stop on, no retention pass starts,
  and the writer calls `ShortenBusyTimeout` right before each batch and the checkpoint, so a statement it starts
  against a held lock fails after 1 s instead of 2 s. When the token fires, the frames still queued count as dropped,
  and the writer checkpoints and closes. `StopAsync` completes at once when nothing
  started; a later call awaits the writer of the first. `Dispose()` disposes the subscription and cancels the writer
  without waiting, for the container's synchronous dispose, also while a stop runs; the writer counts the frames still
  queued as dropped and closes the file without a checkpoint. The writer disposes its cancellation sources when it
  ends, under the lock `Dispose` and the drain token take, so no cancel meets a disposed source.

The link's channel drops are the other half of the dropped count (`board-protocol.md`, Frames out).

### Queries

`SpectrumHistory(path)` opens a reader connection per call inside `Task.Run`. A `SqliteException` returns a failed
`Result`, and one with a damage code also marks the file (Files); a cancellation throws `OperationCanceledException` and never returns a failed result.

- `SpectrumAtAsync(moment)` returns the spectrum with the latest `received_at` at or before the moment, ties to the
  larger `id`, as a `StoredSpectrum` that rebuilds a new `SpectrumMeasurement` with `ReceivedAt`, `seq` and `state`;
  null before the first row. A corrupt row (a `device` that is neither ATP-2000 nor ATP-8000, or a `pixels` blob that
  is not `2 x P` bytes of its device) returns a failed `Result` that names the row's `id`.
- `IntensitySeriesAsync(query)` takes an `IntensityQuery` (wavelength, `delta`, `[From, To)`, column width). For each
  spectrum in the range it takes the mean of the pixels in `SpectrometerCalibration.Window(nm, delta)` of the row's
  device, dark frames included, and folds it into the column of its time. A row whose device does not cover the
  wavelength (the firmware table range `FirstNm..LastNm`) adds to the column's `UncoveredCount` only, and so does a
  corrupt row, which the series skips instead of failing. The series adds no break and no failure mark.
- `IntensityPointsAsync(wavelength, delta, from, to)` returns an `IntensityChunk` of `[from, to)` with one point per
  spectrum in receive order (`received_at`, then `id`): its Unix milliseconds and its window mean, NaN for an
  uncovered device or a corrupt row. The trend's re-read lands these chunks in the cache.
- `StoredRangeAsync()` returns the receive times of the first and last spectrum, or null when none is stored.

`IntensityQuery.Create` truncates `From` and `To` to whole milliseconds, the unit of `received_at`, and fails for an
empty range, a column width that is not a whole number of milliseconds of at least 1, or more than 40 000 columns. The
columns sit on a grid anchored at the Unix epoch: a column starts at a multiple of the width, so the column of a time
`t` starts at `floor(t / width) x width`. The series holds every grid column that intersects `[From, To)`, so the first
and last column may start before `From` or end after `To`, and carries `From`, `To`, `ColumnWidth` and each column's
`Start`. Two queries of one width share the boundaries and the content of every column both hold in full, so a live
window that moves forward, or a pan, keeps its full columns and recomputes only the edges. A column with no covered
row has NaN `Min`, `Max` and `Last`, the gap a chart draws; `UncoveredCount > 0` there says the gap comes from the
other spectrometer or from corrupt rows. A column also carries `Last`, the value with the latest receive time, and,
from the trend cache's fold only, `BreakCount`, `LostCount` and `Failed`, the OR of the failure bits.

The series and the points share one statement over the range and one row loop. The statement runs in
`received_at, id` order, which the `spectrum_time` index gives without a sort: a `CASE` cuts each row's window out of
its pixels with `substr(pixels, 2 x First + 1, 2 x count)`, whatever wavelength the row was stored at, and yields NULL
for a corrupt row. The length check sits inside the `CASE`, because `GetBytes` of Microsoft.Data.Sqlite on an
expression column crashes the process when the result holds a second expression column. The row loop is a lazy
iterator of points that checks the token every 256 rows; the series folds it into buckets and counts a NaN point as
uncovered, and the points read collects it into the chunk. Every read loads the pixel page of every row in the range:
the cost scales with the rows on disk, so a 24 h read touches about a day of pages, and a cold 24 h read, with the OS
file cache empty, runs at the speed of the disk.

`substr` is the kept read. A per-row `SqliteBlob` read of only the window's bytes was no faster on a day-long churned
file, so the tool keeps `substr` only, and a precomputed per-row intensity column does not halve the read once the
wavelength changed inside the range, so the schema holds no such column. A 24 h read beside the running writer grows
the WAL far below its 64 MB limit, so the series is not sliced by time. The store benchmark measures the production
read again; run on the operator PC, it measures the read against that PC's disk and memory:

```powershell
dotnet run -c Release --project SemiSpec/SemiSpec.Tools.StoreBenchmark
```

With the defaults it fills a day of ATP-2000 spectra at 2 per second, every row at 650 nm, churns the file through
24 rounds of retention and refill, and prints one table of inserts, file size, series reads, `SpectrumHistory` calls,
retention and WAL growth, fresh and churned. Every series reads the rows' pixels, so the stored wavelength changes no
read the benchmark measures. A series runs in columns of the range over 1 000 truncated to whole milliseconds, so it
has 1 000 columns or one more. Four rows time the trend over the file's frames: an `IntensityCache` fold of the whole
day and of 10 min to 2048 columns, and a re-read at another wavelength through `TrendHistoryPlan` and
`IntensityPointsAsync`, timed to the landing of the visible 10 min and to the last chunk of the day. The tool does not
drop the OS file cache, which needs administrator rights, so a first run can be warm too. A run deletes its file and
its temp folder unless `--keep` is given.

| Option | Default | Range | Effect |
| --- | --- | --- | --- |
| `--rows <n>` | 172800 | 1200 and up | Spectra to insert, 500 ms apart, ending at the start time; 1200 is the 10 min the short series needs |
| `--device 2000\|8000` | 2000 | 2000, 8000 | The device of every row: ATP-2000 at 650 nm or ATP-8000 at 1550 nm |
| `--churn-rounds <n>` | 24 | 0 to 24 | Rounds of "delete the oldest 1/24 of the span, insert it again one span later" after the fill; at 172800 rows a round is one hour; 0 skips the churn |
| `--folder <path>` | a new folder under `%TEMP%` | any path | Where the file goes |
| `--keep` | off | flag | Keeps the file after the run |

## Error route

Every failure the operator should see goes to `MessagePanelViewModel` through one of the two `ResultReporting`
extensions:

- `ReportFailure(IError, ILogger)` maps the error with `ErrorMessageMapper`, adds it to the panel, and writes one log
  line at the level of its severity: Error, Warning or Information. The line carries the deepest exception in the
  error's reason chain, so a wrapped failure keeps its stack. The log line is written even when the panel throws.
- `TryReportFailure(IError, ILogger, IScheduler)` writes the log line at once on the calling thread, at the level of
  the severity, and moves only the panel entry to the UI scheduler, which writes no second line. It never throws, for
  callers whose exception would end an Rx stream or a dispatcher job. The log line comes first because a failure late
  in the close chain can find the dispatcher already stopped, and its scheduled job never runs. A repeat that
  coalesces into the newest entry still logs at its severity here, since the caller's thread cannot read the panel.

`ErrorMessageMapper.Map` turns an `IError` into a `MessageView` (title, detail, remedy, severity):

| Error | Severity |
| --- | --- |
| `PortBusyError`, `PortNotFoundError` | Error: the operator acts |
| `PortLostError`, `BoardNotRespondingError`, `UnrecognisedDataError` | Warning: the link retries and the operator watches |
| `PortListError` | Warning: the operator presses Refresh |
| `StoreOpenError` | Error: nothing is stored for the run |
| `StoreWriteError` | Warning: the lost frames count as dropped; after a failed batch the store keeps trying |
| `StoreStoppedError` | Error: nothing more is stored for the run, and every later frame counts as dropped |
| `StoreFileReplacedError` | Warning: the damaged file is set aside and a new one started; the operator acts only when it repeats |
| `TrendHistoryError` | Warning: the trend shows only the data since the change; no remedy is owed |
| `TrendMomentError` | Warning: the operator clicks another moment |
| an exception error | Error; the detail keeps the exception type and message |
| any other error | Error; the detail keeps the error's own message |

A link error's view never names the port, so a repeat on another port coalesces; a store error's view leaves the file
path out the same way. No error maps to Info: the status
bar's restored entry is the one Info entry, and it goes to the panel as a `MessageView` with its own Information log
line. The entry's dot takes `AppSeverityErrorBrush`, `AppSeverityWarningBrush` or `AppSeverityInfoBrush` through a
severity class.

The panel holds at most 200 entries, newest first. A message equal to the newest entry, severity included, does not add
a row: it raises that entry's repeat count and last-seen time, and the log line drops to Debug. Only the newest entry
is compared, so a lost port, a restored link and a second lost port are three rows.

The panel row is compact and shows the newest entry only. Its first line holds the severity dot, the last-seen time
`HH:mm:ss`, the bold title, the repeat count and the detail, which ends in an ellipsis and carries the full text as a
tooltip; on the right, "{n} more" opens a 640 px flyout with every entry, scrolling past 360 px, and Clear empties the
list. The remedy takes the second line, wraps to at most two lines and ends in an ellipsis past them; an entry with an
empty remedy shows no remedy line. With no entries the row reads "No messages". The row is
`MainWindow.MessagePanelHeight` 93 px high in every state: a two-line remedy, the 1 px top border and the 8 px
vertical padding. At the minimum width `BoardNotRespondingRemedy` fits one line in both languages and `UnrecognisedDataRemedy`
two.

`MessagePanelViewModel.IsVisible` decides whether the row shows, and the view model is its one writer. It starts false;
each new entry sets it, so nothing new lands off screen, while a repeat that coalesces into the newest entry does not,
so a panel the operator closed during an outage stays closed. View, Message panel toggles it through `ToggleCommand`,
and Clear keeps the row shown.

`UnhandledErrorObserver` is the handler `BuildAvaloniaApp` passes to `UseReactiveUI(... WithExceptionHandler ...)`.
`App.Run` builds it once with the logger `Program` hands over, and it lives for the process, because ReactiveUI takes
the handler once, before any window exists. On each exception it reads the panel `App.Configure` resolved through
`App.ResolveMessagePanel`; before `Configure` it only logs.

The same observer handles `Dispatcher.UIThread.UnhandledException`, for an exception that escapes a dispatcher job
outside ReactiveUI, for example in a code-behind event handler. With a panel it reports an `ExceptionalError` through
`TryReportFailure` and sets `Handled`, so the app keeps running. Before `Configure` there is no panel: the exception
stays unhandled, ends the dispatcher loop, and `Program` logs it as Fatal and exits with code 1; the handler does not
log it a second time.

## Release

### Publish

The `Version` element of `SemiSpec/Directory.Build.props` is the one version source. The exe's file version
comes from it. The installer's version is the tag's, passed as `/DAppVersion`, and the release workflow fails unless
the tag equals `Version`. In Release the props emit no symbols (`DebugType None`). During `dotnet publish`
(`_IsPublishing`) they set `RuntimeIdentifier win-x64`, `SelfContained` and `PublishReadyToRun`, so
`dotnet publish SemiSpec/SemiSpec.UI/SemiSpec.UI.csproj -c Release` writes a folder that runs without an installed
.NET to `SemiSpec/Artifacts/publish/SemiSpec.UI/release_win-x64/`. The UI project sets `AssemblyName` to `SemiSpec`,
so the exe is `SemiSpec.exe`; its `RootNamespace` stays `SemiSpec.UI`. There is no single-file publish.

### The installer script

`Installer/SemiSpec.iss` packs that folder. It refuses to compile without `/DAppVersion=X.Y.Z` and resolves
its paths relative to `Installer/`; the compiled `SemiSpecInstaller.exe` goes to `Installer/Output/`.

- `AppId` is the one of the 2.x installer, which shipped as SpectrumVisualizer, so setup upgrades a 2.x or 3.0
  install in place and Settings, Apps keeps one entry, `SemiSpec X.Y.Z` (`AppVerName`).
- `DisableDirPage=yes` hides the folder page. A fresh install goes to `DefaultDirName`, `{autopf}\SemiSpec`;
  an upgrade reuses the folder the previous install recorded (`UsePreviousAppDir`), so an upgraded machine keeps
  `{autopf}\SpectrumVisualizer`. The wizard gives the operator no way to pick another folder.
- `[InstallDelete]` empties the program folder before the copy, so no 2.x file outlives the upgrade. This is safe
  because the folder is the app's own and the app keeps nothing in it at run time: the spectra live under
  `%LOCALAPPDATA%\SemiSpec` and the logs under `C:\DISTR\Logs\SemiSpec`. A 2.x install that an operator once put into a shared folder loses the
  other files there.
- `[InstallDelete]` also deletes the Start menu and desktop shortcuts under both names, `SemiSpec.lnk` and
  `SpectrumVisualizer.lnk` of the 2.x and 3.0 installers, which point at a removed exe. `[Icons]` creates the Start
  menu entry and, when the operator ticks the unchecked desktop task, a desktop shortcut.
- `[Dirs]` creates `C:\DISTR\Logs\SemiSpec` (`LogRoot`) with `Permissions: users-modify`, so the operator, not the
  installing administrator, can write the log, and `Flags: uninsneveruninstall`, so uninstall keeps the logs. The
  entry is SemiPlot's. After an upgrade over a folder that already existed, check the rights by hand on the target
  PC: `icacls C:\DISTR\Logs\SemiSpec` lists `BUILTIN\Users` with modify rights (`(M)`); without them the app logs to
  the fallback folder.
- `[Files]` copies the publish folder without `*.pdb` and the `SemiSpec.xml` and `SemiSpec.*.xml` doc files.
- `CloseApplications=yes` asks to close a running copy before the files change, the 3.0 `SpectrumVisualizer.UI.exe`
  and the 2.x `SpectrumVisualizer.Uart.exe` included, since both sit under the `{app}\*` that the wipe covers.
- `PrivilegesRequired=admin`, because setup writes to Program Files and `C:\DISTR`. The post-install launch carries
  `runasoriginaluser`, so the app runs as the operator and writes the operator's `%LOCALAPPDATA%`, not the
  administrator's.
- The wizard offers Russian and English and preselects the Windows language. `UsePreviousLanguage=no` keeps it from
  reusing the English that the 2.x installer recorded.
- Uninstall removes the program folder and the shortcuts and leaves `%LOCALAPPDATA%\SemiSpec` and
  `C:\DISTR\Logs\SemiSpec`.
- Nothing migrates or deletes `%LOCALAPPDATA%\SpectrumVisualizer`, the data folder of the builds named
  SpectrumVisualizer. The app starts every run with an empty store, so it reads nothing from that folder; the folder
  stays on disk until the operator deletes it.

### The release workflow

`.github/workflows/release.yml` runs on a pushed tag that matches `vX.Y.Z`, on one Windows runner. Its first step is
the gate: it fails unless the tag without `v` equals the props `Version`, read with `sed`, and unless `git cat-file
-t` of the tag prints `tag`, because the release body is the tag annotation and a lightweight tag has none. The tag
must be created with `git tag -a --cleanup=verbatim`, so the `#` of the Markdown headings survives in the
annotation. The workflow then restores, builds, runs the unit tests, publishes, installs Inno Setup 7.1.1 with
winget, compiles the script with the tag's version, and creates the release "SemiSpec X.Y.Z" with
`softprops/action-gh-release`, marked latest, with `SemiSpecInstaller.exe` attached. A failed step stops
the run before the release exists.
