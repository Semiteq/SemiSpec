# Serial link

## Overview
- Carry the board's bytes from a COM port to decoded frames in the running app: a serial link on
  `RJCP.SerialPortStream` 3.0.5 with one cancellable read loop, a `System.IO.Pipelines` pipe into `FrameReader`, and a
  bounded `Channel<BoardFrame>` that the later views and store read.
- The operator picks a port, connects and disconnects in a new connection row. After an unplug the link reopens the
  selected port every second until the operator disconnects.
- The status bar shows the link state (OptoskyProj#20: "board not responding" after 10 s with no valid frame and no
  bytes, "other device" after 10 s of bytes with no valid frame), the port, both spectrometers' connected and
  failed bits from the latest frame, and the spectra lost since Connect.
- Link failures reach the message panel as typed errors with a severity; a recovery adds one Info entry.
- The skeleton seams close: the UI `IScheduler` comes from `AddUi`, Info and Warning severity return to the message
  panel, the link stops before the dispatcher ends, `Dispatcher.UIThread.UnhandledException` reaches the panel, the
  status bar is a `ReactiveObject`, no volatile data enters `MessageView.Detail`.
- The board emulator gains a dark frame every N spectra, the measurement-failed bits and a both-spectrometers state, so
  every status bar segment can be checked without a board.
- Scope guard: no charts, no storage, no consumer of the frame channel, no change to the old app, no launch keys or
  config files, nothing sent to the board.

## Context (from discovery)
- Roadmap: docs/plans/roadmaps/20261104-avalonia-rewrite-roadmap.md — slice serial-link
- Stacking base: local branch `frame-decoder` at `2237e5e`; this slice works on the local branch `serial-link` created
  from it. Local commits only.
- Verified 2026-11-04 at `2237e5e`: `dotnet test SpectrumVisualizer.slnx` passes 172 of 172.
- Protocol contract: Semiteq/OptoskyProj#20, section "Что показывает приложение": 10 s with no valid frame and no data
  means "board not responding"; data arriving with no valid frame for 10 s means "other device"; `seq` growing by k > 1
  means k - 1 lost spectra; state bit 0 or 1 clear means a spectrometer is not connected; bit 2 or 3 set means its last
  measurement failed. The board sends a heartbeat when nothing was sent for 3 s.
- Core protocol code this slice consumes:
  - `FrameReader.ReadAllAsync(PipeReader, CancellationToken)` (`SpectrumVisualizer.Core/Protocol/FrameReader.cs:16-44`)
    yields frames until the writer completes; on completion it drops one byte at each pending candidate and decodes
    again (`:59-67`), so completing the writer yields the frames behind a stalled candidate. `DiscardedBytes` (`:13`)
    is per reader.
  - `SequenceTracker.Observe(BoardFrame)` (`SequenceTracker.cs:12`) and `Reset()` (`:31-34`).
  - `BoardFrame(BoardState State, byte Seq, SpectrumMeasurement? Measurement)` and the `[Flags] BoardState` bits
    (`BoardFrame.cs:6-16`). `FrameLayout.FrameSize` (`FrameLayout.cs:29-32`) gives 4120 bytes for the largest frame.
  - `board-protocol.md:98-112` documents the stall and still says the serial-link slice "drops the decoder's buffer";
    this slice drains instead and the docs task rewrites that paragraph.
- UI seams this slice closes:
  - `App.Run` builds the `UnhandledErrorObserver` with `AvaloniaScheduler.Instance` (`App.axaml.cs:67-78`, the
    scheduler at `:72`); `Configure` resolves only the main window view model and the panel (`:80-84`);
    `OnFrameworkInitializationCompleted` shows the window (`:45-53`); `CreateMainWindow` sets `DataContext` (`:55-64`).
  - `AddUi` registers logging, the level control, `TimeProvider.System`, the panel, the status bar and the main window
    view model (`UiServiceCollectionExtensions.cs:13-24`); no `IScheduler`.
  - `Program.Main` disposes the container after `App.Run` returns (`Program.cs:40-45`, `using var serviceProvider`).
  - `MessageView(Title, Detail, Remedy)` has no severity (`MessageView.cs:7-10`); `ResultReporting.ReportFailure` logs
    at Error or Debug (`ResultReporting.cs:24-31`); `TryReportFailure(error, logger, uiScheduler)` exists
    (`:39-43`); the entry dot always uses `AppSeverityErrorBrush` (`MessagePanelView.axaml:44-49`).
  - `AppStatusBarViewModel` is a plain class with a fixed `ConnectionText` (`AppStatusBarViewModel.cs:5-9`) bound by one
    `TextBlock` (`AppStatusBar.axaml:12-14`).
  - Main window rows `Auto,*,Auto,Auto` (`MainWindow.axaml:18`); the status bar row has `Padding="16,8"` (`:44-51`).
    `MainWindowViewTests` builds the view model with `new AppStatusBarViewModel()` (`MainWindowViewTests.cs:35-42`).
  - `MainWindowViewModel(MessagePanelViewModel, AppStatusBarViewModel, LogLevelControl, ILogger<>)`
    (`MainWindowViewModel.cs:23-37`).
  - Palette `App*` keys: `Palette.axaml:64-68` (Light) and `:123-127` (Dark); only `AppSeverityErrorBrush` of the
    severity set exists.
  - `overview.md:100-101` says every entry is an error; `overview.md:113-115` and `docs/architecture/README.md:28` say
    an exception escaping a dispatcher job ends the app; `README.md:33` says no code reads a port.
- Packages: `RJCP.SerialPortStream` 3.0.5 is already pinned (`SpectrumVisualizer/Directory.Packages.props:24`) and
  referenced only by the emulator; the UI project gains a `PackageReference`. `Microsoft.Reactive.Testing` 6.1.0 is
  pinned (`:22`) and referenced by the test project. `System.IO.Pipelines` and `System.Threading.Channels` are in the
  shared framework. `SpectrumVisualizer.Core.csproj:3-5` references FluentResults only and stays that way.
- RJCP 3.0.5 API (`lib/net10.0/RJCP.SerialPortStream.xml`): `GetPortNames()` (`:16436`); `ThrowOnReadError`, default
  false, makes a read throw `TimeoutException` on a read timeout and `InvalidOperationException` on a closed port
  (`:16530-16540`); `ReadTimeout` accepts `Timeout.Infinite` (`:16516-16528`); a read throws `IOException` on device
  removal (`:16626`); `ReadAsync(Memory<byte>, CancellationToken)` honours the token and throws `TimeoutException` on
  a read timeout when `ThrowOnReadError` is set (`:16684-16705`).
- Avalonia 12.0.5 API: `Dispatcher.UnhandledException` with `DispatcherUnhandledExceptionEventArgs.Handled`
  (`Avalonia.Base.xml:38100-38108`, `:39021`). `ShutdownRequested` is raised for an OS session end, and an uncancelled
  shutdown closes every window through `Window.Closing`, which can cancel it (`Avalonia.Controls.xml:167-182`).
- SemiPlot patterns borrowed (`C:/Users/admin/projects/SemiPlot`, `bd8f83f`):
  - `MessageSeverity` (`SemiPlot.UI/Messages/MessageSeverity.cs:6-16`), `SeverityMatchConverter`
    (`SeverityMatchConverter.cs:12-24`), severity classes on the dot (`MessagePanelView.axaml:15-21`, `:68-70`), log
    level from severity (`ResultReporting.cs:109-115`).
  - Palette values: `AppSeverityWarningBrush`, `AppSeverityInfoBrush`, `AppConnectionOkBrush`,
    `AppConnectionFaultBrush` at `SemiPlot.UI/Styles/Palette.axaml:88-92` (Light) and `:172-176` (Dark),
    `AppSubtleLineBrush` at `:80` and `:164`.
  - The status bar as state only, subscribed before its source starts, reporting a fault from the state and one Info
    entry on recovery (`SemiPlot.UI/MainWindow/AppStatusBarViewModel.cs:17-21`, `:84-120`, `:133-140`;
    `TrendWindow.cs:109-114`); the separator between status segments, a 1 px `Border` with `Margin="8,4"`
    (`SemiPlot.UI/MainWindow/AppStatusBar.axaml:31-34`).
  - A scheduler-driven wait: a `TaskCompletionSource` completed by `scheduler.Schedule(interval, ...)`
    (`SemiPlot.UI/Bridge/ArchiveReadLoop.cs:94-104`).
- Old app parity: it checks the port list every 1 s (`SpectrumVisualizer.Uart/mainForm.cs:34`) and opens the port at
  115200 baud (`SpectrumVisualizer.Uart/Device/DeviceManager.cs:23`).
- Emulator: `EmulatorOptions(Port, Device, Rate, WavelengthNm, Delta, BadCrcEvery)` (`EmulatorOptions.cs:6-12`); options
  declared at `EmulatorCommand.cs:17-54`, assembled in `Build` (`:58-66`) and `From` (`:68-78`); `FrameSchedule`
  derives the state from the device alone (`FrameSchedule.cs:12-13`) and picks the frame in `Next()` (`:26-58`); the
  tool writes at 115200 baud (`Program.cs:7`, `:18`).
- Test traits: `Component` (`Core|UI|Tools`), `Area`, `Category=Unit` (`CLAUDE.md:40-41`,
  `testing-strategy.md:18-20`). This slice adds the Area `Link`.

## Development Approach
- **testing approach**: Regular (code first, then tests in the same task).
- complete each task fully before moving to the next; small, focused changes.
- every task includes new or updated tests for its code; all tests pass before the next task.
- update this plan when scope changes during implementation.
- the old solution keeps building and passing its 13 tests; this slice does not touch it.

## Testing Strategy
- **unit tests** in `SpectrumVisualizer.Tests.Unit`: plain `[Fact]` for the health evaluator and the link, which hold no
  ReactiveUI object; `[AvaloniaFact]` for view models and realised views. Traits per class: `Component=UI`,
  `Area=Link` for `UI/Link/`; `Area=MainWindow` for the status bar and the connection row; `Area=Messages` for
  severity; `Area=Di` for the composition and lifecycle; `Component=Tools`, `Area=Emulator` for the emulator.
- **link tests** run over `FakeSerialPortProvider` and `FakePortStream` (test folder `UI/Link/`): the test pushes
  scripted steps (a chunk of bytes, a read timeout, an `IOException`) and the stream's `ReadAsync` returns them in
  order. Time is a `TestScheduler`: the test advances it, then pushes a timeout step, so the link evaluates its health
  at a known virtual time. The reconnect wait completes when the test advances the scheduler by 1 s. Every async test
  awaits a status with a 5 s real-time limit, so a hung loop fails instead of blocking the run.
- **headless tests** realise the main window with the connection row and the status bar, and drive the window's
  close and a throwing dispatcher job.
- no e2e framework exists; headless tests take that role. The port-pair run is manual.

## Acceptance Evidence
Automatable (baseline 172 of 172 at `2237e5e`):
1. `dotnet test SpectrumVisualizer.slnx --filter "Area=Link"` passes, including:
   - `LinkHealthTests`: no frame yet and under 10 s since open is Waiting; a frame under 10 s ago is Receiving; 10 s
     with no frame and no byte is NotResponding; 10 s with no frame and a byte under 10 s ago is OtherDevice; the
     10 s boundary is inclusive; a drain is due on entry to OtherDevice and again every 10 s while it lasts.
   - `SerialLinkTests.FramesInRandomChunksReachTheChannel`: an ATP-2000 frame, an ATP-8000 frame and a heartbeat,
     written in random chunks from a fixed seed, come out of `Frames` in order.
   - `SerialLinkTests.SilenceForTenSecondsReportsBoardNotResponding`.
   - `SerialLinkTests.BytesWithoutAValidFrameForTenSecondsReportOtherDevice` (frames with a corrupted CRC).
   - `SerialLinkTests.AStalledCandidateIsDrainedAndTheFramesBehindItArrive`: a false `AA 55` with `len` 4114, then
     heartbeats every 3 s of virtual time; after 10 s the link drains, the heartbeats reach the channel, and the state
     goes back to Receiving without ever publishing OtherDevice.
   - `SerialLinkTests.AnIOExceptionReportsPortLostAndReopensAfterOneSecond` and
     `SerialLinkTests.ReopeningResetsTheSequenceBaseline` (a frame with `seq` 57 after a reopen reports no loss).
   - `SerialLinkTests.APortHeldByAnotherProgramReportsPortBusyAndRetries`,
     `SerialLinkTests.AMissingPortReportsPortNotFoundAndRetries`.
   - `SerialLinkTests.DisconnectCancelsAPendingReadAndStopsRetrying`.
   - `SerialLinkTests.LostSpectraAccumulateAcrossReopensAndResetOnConnect`.
   - `SerialLinkTests.AFullChannelDropsTheOldestFrame`: 65 frames with no reader leave frames 2 to 65 and
     `DroppedFrames` 1.
   - `SerialLinkTests.EachFaultIsPublishedOnceAndRetriesStayQuiet`: 5 failed reopens publish one `PortLostError`.
2. `dotnet test SpectrumVisualizer.slnx --filter "Area=Messages"` passes, including the severity tests: Info, Warning
   and Error entries carry their dot class and log at Information, Warning and Error; each link error maps to its own
   title and severity; no link error's detail contains its port name.
3. `dotnet test SpectrumVisualizer.slnx --filter "Area=MainWindow"` passes, including `AppStatusBarViewModelTests`
   (text per link state, spectrometer texts from the state bits, a fault reported once, one Info entry on recovery,
   none after the first connect), `ConnectionBarViewModelTests` (refresh keeps a still-present selection; Connect
   needs a selected port and an idle link; Disconnect needs an active link) and `MainWindowViewTests` (rows top to
   bottom: menu, connection row, content, message panel, status bar; every status segment realised).
4. `dotnet test SpectrumVisualizer.slnx --filter "Area=Di"` passes, including: every registered service resolves,
   `IScheduler` resolves to the UI scheduler, the window's close waits for the link to stop, and an exception thrown
   in a dispatcher job adds a panel entry and leaves the app running.
5. `dotnet test SpectrumVisualizer.slnx --filter "Area=Emulator"` passes, including the dark spectrum every N spectra,
   the failed bit of the emulated device in heartbeats only, the both-connected state, and the rejected
   `--failed 2000 --device 8000` without `--both-connected`.
6. `dotnet test SpectrumVisualizer.slnx` passes 172 plus every test this slice adds, with none skipped.
7. `dotnet build SpectrumVisualizer.slnx -c Release` 0 warnings; `dotnet format SpectrumVisualizer.slnx
   --verify-no-changes` and `dotnet terse SpectrumVisualizer` exit 0; `dotnet build SpectrumVisualizer.sln` and
   `dotnet test SpectrumVisualizer.Tests` pass 13 of 13.
8. The two `git grep` commands of `ui-theme.md:61-63` and `:74-76` print nothing.

Manual smoke checklist (owner; com0com pair `COM10`-`COM11`, the emulator on `COM10`, the app on `COM11`;
`emu` stands for `dotnet run --project SpectrumVisualizer/SpectrumVisualizer.Tools.BoardEmulator --`):
1. Start the app. The status bar reads "Not connected"; the port list holds `COM11`; Connect is disabled until a port
   is selected.
2. Run `emu --port COM10`, select `COM11`, Connect. Within 1 s the status bar reads "Receiving data", ATP-8000
   connected, ATP-2000 not connected, lost spectra 0. The port list and Connect are disabled; Disconnect is enabled.
3. Restart the emulator with `--both-connected --failed 2000`. ATP-2000 reads "measurement failed", ATP-8000
   "connected".
4. Restart it with `--rate 0`. The status stays "Receiving data" on heartbeats alone.
5. Silence: stop the emulator with Ctrl+C. 10 s later the status reads "Board not responding" and the panel shows one
   Warning entry. Start it again: the status returns to "Receiving data" and one Info entry says data is restored.
6. Garbage: run `emu --port COM10 --bad-crc-every 1`. 10 s later the status reads "Other device" with one Warning
   entry. A wrong baud rate needs a real USB-UART pair (ASSUMPTION: com0com delivers bytes whatever the baud setting,
   so it cannot produce baud garbage); with that pair, set the sender's terminal to 9600 baud and expect the same.
7. Lost spectra: run `emu --port COM10 --bad-crc-every 5`. The lost count rises by 1 for each corrupted spectrum.
8. Dark frames: run `emu --port COM10 --dark-every 3`. The status stays "Receiving data"; the lost count stays 0.
9. Unplug (a real USB-UART pair; com0com cannot emulate a removal): unplug the app-side adapter. The status reads "Port
   unavailable, retrying" and the panel shows one "connection lost" Warning. Plug it back: the status returns to
   "Receiving data" with one Info entry. Unplug and replug again: the existing entries count repeats instead of
   adding rows.
10. Port busy: open `COM11` in a serial terminal, then Connect in the app. The panel shows one "port is busy" Error and
    the status reads "Port unavailable, retrying". Close the terminal: the app connects within 1 s.
11. Disconnect while retrying: the status reads "Not connected" and the log shows no further open attempts.
12. Close the window while connected: the process ends within 1 s and the log holds the link's stop line.
13. With a Russian Windows UI every new text is Russian; with English, English. The connection row and the status bar
    fit at the window's opening width in both languages.

## Progress Tracking
- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update plan if implementation deviates from original scope

## Solution Overview
- **Where the link lives.** `SpectrumVisualizer.UI/Link/` holds the link, its port seam and its errors. Core stays free
  of serial port code and keeps its single FluentResults reference; the UI project references `RJCP.SerialPortStream`.
- **Port seam.** `ISerialPortProvider` (`IReadOnlyList<string> PortNames()`, `Result<Stream> Open(string portName)`) is
  the process boundary; the interface exists so the link runs in tests without hardware. `RjcpSerialPortProvider` is
  the one production implementation: `SerialPortStream.GetPortNames()` sorted by the number after `COM`, a port at
  115200 baud, 8N1, `ReadTimeout = 1000` ms, `ThrowOnReadError = true`. `Open` maps `UnauthorizedAccessException` to
  `PortBusyError` and `IOException` or a name missing from `GetPortNames()` to `PortNotFoundError`.
- **One owner per resource.** `SerialLink` runs one session task per Connect. Inside it the read loop is the only code
  that touches the port stream and the current `PipeWriter`; the decode loop is the only code that reads the
  `PipeReader`, feeds `SequenceTracker` and writes the channel; only the read loop publishes the status. The decode
  loop hands its results to the read loop through three fields written with `Interlocked`/`Volatile` (last valid frame
  time, latest `BoardState`, lost-spectra total), each commented with its two threads.
- **Read loop.** `GetMemory(4096)`, `ReadAsync(memory, token)`, `Advance`, `FlushAsync`. A read of zero bytes or an
  `IOException` or `InvalidOperationException` means the port is lost. A `TimeoutException` is a 1 s tick with no
  bytes. After each read or tick the loop records the time, evaluates the health, drains when due, then publishes the
  status. One loop gives a status at least once a second with no timer and no second writer.
- **Pipe.** `new PipeOptions(pauseWriterThreshold: 65536, resumeWriterThreshold: 32768,
  useSynchronizationContext: false)`: the pause threshold is above 2 x 4120 bytes, so a writer never waits on a reader
  that is stalled on a whole-frame candidate.
- **Health.** `LinkHealth.Evaluate(now, openedAt, lastByteAt, lastFrameAt)` is a pure function. The reference time is
  the last valid frame, or the port open when no frame has come. Under 10 s since the reference: Waiting before the
  first frame, Receiving after it. From 10 s: OtherDevice when a byte arrived in the last 10 s, NotResponding when none
  did. The 10 s clock starts at port open, so a dead port shows "board not responding" 10 s after Connect.
- **Drain.** A drain is due when the evaluation is OtherDevice and 10 s have passed since the later of the reference
  and the previous drain. The read loop completes the writer, awaits the decode loop (which yields the frames behind a
  stalled candidate through `FrameReader`'s completion path and ends), creates a fresh `Pipe` and `FrameReader`,
  restarts the decode loop, evaluates again, and only then publishes. A stall that hid real frames therefore never
  shows OtherDevice; data that holds no frame shows it right after the drain.
- **Reopen.** A lost port or a failed open moves the state to PortUnavailable and the session waits 1 s (the old
  app's 1 s cadence) on the link's scheduler, then opens again, until the operator disconnects. Every successful open
  calls `SequenceTracker.Reset()` and starts a fresh pipe and reader, so the gap during an unplug is not counted as
  lost spectra. Each open attempt logs at Debug with the port name.
- **Status.** `LinkStatus(LinkState State, string? PortName, BoardState? Board, long LostSpectra, IError? Fault)`;
  `LinkState` is `Disconnected`, `Connecting`, `PortUnavailable`, `Waiting`, `Receiving`, `NotResponding`,
  `OtherDevice`. `SerialLink` raises `StatusChanged` only when the record differs from the last one. `Fault` is set
  only on the status that enters a fault: the first failed open or port loss of a run, the entry to NotResponding,
  the entry to OtherDevice. Retries that fail the same way carry no fault, so the panel gets one entry per incident.
  An unexpected exception in the session ends it with state Disconnected and an `ExceptionalError` fault.
- **Typed errors** (`UI/Link/LinkErrors.cs`, one closed family): `PortBusyError` and `PortNotFoundError` (Error: the
  operator acts), `PortLostError`, `BoardNotRespondingError` and `UnrecognisedDataError` (Warning: the link retries
  and the operator watches). Each carries the port name in its FluentResults metadata and its log message, never in
  the mapped `MessageView`, so a repeat on another port still coalesces. `ErrorMessageMapper` gains one arm per type.
- **Frames out.** `Frames` is a `ChannelReader<BoardFrame>` over `Channel.CreateBounded<BoardFrame>(new
  BoundedChannelOptions(64) { FullMode = DropOldest, SingleWriter = true, SingleReader = true }, itemDropped)`. 64
  frames are about 50 s of ATP-8000 spectra at 1.3 per second and at most 64 x 4120 bytes of frame data. `Wait` is
  ruled out: a reader that falls behind would block the decode loop, stall the pipe and trip the 10 s status on a
  healthy board. `itemDropped` increments `DroppedFrames` and logs at Debug. The channel has one reader; the
  spectrum-view slice adds it and hands each frame to every later consumer, the store included. Until then the channel
  fills and drops the oldest frame, which is expected.
- **Lost spectra.** The status bar shows the total since the operator's Connect, kept across automatic reopens. Each
  loss logs at Information with the `seq` values.
- **Clock.** The link takes an `IScheduler`: `scheduler.Now` stamps every time it compares, and the reopen wait is a
  `TaskCompletionSource` completed by `scheduler.Schedule(1 s, ...)`, as in `ArchiveReadLoop.cs:94-104`. `AddUi`
  registers the UI scheduler as the unkeyed `IScheduler` and builds `SerialLink` with `DefaultScheduler.Instance`
  through a factory lambda.
- **Stopping.** `DisconnectAsync()` cancels the session token, awaits the session task, closes the port, completes the
  pipe and publishes Disconnected. `ReadAsync` honours the token (RJCP XML `:16684-16705`); the 1 s read timeout bounds
  the wait even if it did not. `Dispose()` cancels without waiting, for the container's synchronous dispose
  (`Program.cs:40`).
- **App lifecycle.** `Configure` resolves the link, the connection row and the status bar along with the panel. The
  window's `Opened` refreshes the port list once: the start point after the window exists. The window's `Closing` is
  the async stop: the first `Closing` cancels the close, awaits `SerialLink.DisconnectAsync()` for at most 5 s, sets a
  flag and closes the window again. `Closing` covers the close button, File, Exit and an OS shutdown, which closes
  every window through `Closing` (`Avalonia.Controls.xml:180-182`). The handler is `async void` with its body in
  try/catch.
- **Dispatcher exceptions.** `App.Run` subscribes `Dispatcher.UIThread.UnhandledException` once per process, beside
  the `UnhandledErrorObserver`. The handler reads the panel through `ResolveMessagePanel` (`App.axaml.cs:98-102`),
  reports an `ExceptionalError` through `TryReportFailure` with the UI scheduler and sets `Handled = true`, so the app
  keeps running as it does for ReactiveUI's unhandled exceptions. Before `Configure` there is no panel: the handler
  leaves `Handled` false, so the exception still ends the app and is logged as Fatal.
- **Severity.** `MessageView` gains `MessageSeverity Severity` (`Info`, `Warning`, `Error`, from SemiPlot);
  `ResultReporting` logs at the severity's level and at Debug for a coalesced repeat; the dot takes
  `AppSeverityErrorBrush`, `AppSeverityWarningBrush` or `AppSeverityInfoBrush` through SemiPlot's
  `SeverityMatchConverter`. The existing exception and unknown-error mappings are Error. Record equality still
  coalesces identical entries.
- **Connection row.** A new `Auto` row under the menu: label "Port", a 240 px combo box of port names (choice width),
  Refresh, Connect, Disconnect. The combo box and Connect are enabled only while the link is Disconnected; Connect also
  needs a selected port; Disconnect is enabled in every other state. With no ports the combo box shows the placeholder
  "No ports found". Refresh keeps the selection when the port is still listed. The row is the window's connection
  control, so the spectrum-view slice keeps it as it is.
- **Status bar.** `AppStatusBarViewModel` becomes a `ReactiveObject` that follows `SerialLink.StatusChanged` through
  `Observable.FromEventPattern(...).ObserveOn(uiScheduler)`, subscribed in its constructor before any Connect. It shows,
  left to right with a 1 px separator between segments: a dot and the link text, the port name, "ATP-2000: ...",
  "ATP-8000: ...", "Lost spectra: N". The dot is `AppConnectionOkBrush` in Receiving, `AppConnectionFaultBrush` in
  PortUnavailable, NotResponding and OtherDevice, `AppSecondaryForegroundBrush` otherwise; the text always names the
  state, so colour never carries it alone. A spectrometer reads "connected", "not connected", "measurement failed"
  (connected bit and failed bit set) or "no data" before the first frame. The port and the lost count read "-" while
  disconnected. A status with a fault reports it through `ReportFailure`; the first Receiving after a fault adds one
  Info entry "data from the board restored", and the first Receiving after Connect adds none. A throw inside the
  handler goes to `TryReportFailure`.
- **Dark frames** pass through the channel like any spectrum and change nothing in the status bar; `--dark-every`
  serves the spectrum-view slice's log axis.
- **Emulator.** `--dark-every <n>` (0 never, default 0) sends every nth spectrum with all pixels, `amplitude` and `snr`
  zero (`board-protocol.md:57`); it consumes its `seq` like any spectrum. `--both-connected` sets both connected bits.
  `--failed 2000|8000` sets that spectrometer's failed bit; the emulated spectrometer's own failed bit appears in
  heartbeats only, because the board clears it before sending that spectrometer's frame (`board-protocol.md:41-43`).
  `--failed` naming the other spectrometer requires `--both-connected`, or the command fails with a line naming both
  options.

## Technical Details
Link states and what the operator reads (resx keys in brackets):

| State | Enters when | Status text (en) | Dot | Panel entry |
| --- | --- | --- | --- | --- |
| Disconnected | start; Disconnect; unexpected exception | Not connected (`StatusNotConnected`) | neutral | none; Error for an unexpected exception |
| Connecting | Connect, before the first open returns | Connecting (`StatusConnecting`) | neutral | none |
| PortUnavailable | open failed or port lost; retry every 1 s | Port unavailable, retrying (`StatusPortUnavailable`) | fault | first of the run: `PortBusyError`, `PortNotFoundError` or `PortLostError` |
| Waiting | port open, no frame yet, under 10 s | Waiting for data (`StatusWaiting`) | neutral | none |
| Receiving | a valid frame under 10 s ago | Receiving data (`StatusReceiving`) | ok | Info "data from the board restored" after a fault |
| NotResponding | 10 s no frame, no byte in the last 10 s | Board not responding (`StatusNotResponding`) | fault | `BoardNotRespondingError` on entry |
| OtherDevice | 10 s no frame, bytes in the last 10 s, after a drain | Other device (`StatusOtherDevice`) | fault | `UnrecognisedDataError` on entry |

New resx keys (en and ru): `ConnectionPortLabel`, `ConnectionPortPlaceholder`, `ConnectionRefresh`,
`ConnectionConnect`, `ConnectionDisconnect`; `StatusConnecting`, `StatusPortUnavailable`, `StatusWaiting`,
`StatusReceiving`, `StatusNotResponding`, `StatusOtherDevice`, `StatusSpectrometerFormat` (`{0}: {1}`),
`StatusSpectrometerConnected`, `StatusSpectrometerNotConnected`, `StatusSpectrometerFailed`,
`StatusSpectrometerNoData`, `StatusLostSpectraFormat`, `StatusNoValue`; for each link error a `...Title`,
`...Detail` and `...Remedy`; `LinkRestoredTitle`, `LinkRestoredDetail`. Device names `ATP-2000` and `ATP-8000` stay
literal in both sets.

Timing constants, all in `SerialLink` or `LinkHealth`: health window 10 s (OptoskyProj#20), drain interval 10 s, read
timeout 1 s, reopen interval 1 s, stop limit 5 s in `App`.

Session flow:
1. Connect(port): reset the lost total, publish Connecting, start the session task.
2. Open. On failure: publish PortUnavailable (fault on the first failure of the run), wait 1 s, go to 2.
3. Reset the tracker, create the pipe and reader, start the decode loop, `openedAt = now`, publish Waiting.
4. Read or tick, evaluate, drain when due, publish. On port loss: complete the pipe, await the decode loop, dispose the
   stream, publish PortUnavailable (fault on the first loss of the run), wait 1 s, go to 2.
5. On cancellation: complete the pipe, await the decode loop, dispose the stream, publish Disconnected.

A "run" is the time between two Receiving states: the first failure after Receiving carries a fault, later failures in
the same run do not.

## What Goes Where
- **Implementation Steps**: link code, UI rows, severity, lifecycle, emulator options, tests and docs in this repo.
- **Post-Completion**: the port-pair and USB-UART smoke run.

## Implementation Steps

### Task 1: Message severity
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Messages/MessageSeverity.cs`, `SeverityMatchConverter.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Messages/MessageView.cs`, `ErrorMessageMapper.cs`,
  `ResultReporting.cs`, `MessagePanelView.axaml`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Styles/Palette.axaml`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Messages/ErrorMessageMapperTests.cs`,
  `ResultReportingTests.cs`, `MessagePanelViewModelTests.cs`, `UI/AppResourcesTests.cs`

- [ ] copy `MessageSeverity` and `SeverityMatchConverter` from SemiPlot (`MessageSeverity.cs:6-16`,
  `SeverityMatchConverter.cs:12-24`), renaming the namespace
- [ ] add `Severity` as the fourth member of `MessageView` (`MessageView.cs:7-10`); both existing mapper arms
  (`ErrorMessageMapper.cs:22-36`) return `Error`
- [ ] log at the severity's level in `ReportFailure` (`ResultReporting.cs:24-31`), Debug for a coalesced repeat, as in
  SemiPlot `ResultReporting.cs:109-115`
- [ ] give the entry dot the three severity classes (`MessagePanelView.axaml:44-49`, SemiPlot
  `MessagePanelView.axaml:15-21`, `:68-70`); add `AppSeverityWarningBrush`, `AppSeverityInfoBrush`,
  `AppConnectionOkBrush`, `AppConnectionFaultBrush` and `AppSubtleLineBrush` to both variants of `Palette.axaml`
  (after `:68` and `:127`) with SemiPlot's values
- [ ] write tests: a Warning and an Info entry log at Warning and Information; a coalesced repeat logs at Debug; the
  realised dot carries the class of its severity; entries that differ only in severity are two rows
- [ ] extend `AppResourcesTests` with the five new palette keys under both variants
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 2: UI scheduler in the container and dispatcher exceptions to the panel
**Files:**
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/UiServiceCollectionExtensions.cs`, `App.axaml.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Di/CompositionRootTests.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Di/DispatcherExceptionTests.cs`

- [ ] register `AvaloniaScheduler.Instance` as the unkeyed `IScheduler` in `AddUi` (`UiServiceCollectionExtensions.cs:13-24`)
- [ ] build the `UnhandledErrorObserver` in `App.Run` with the container's `IScheduler` instead of
  `AvaloniaScheduler.Instance` (`App.axaml.cs:70-72`)
- [ ] subscribe `Dispatcher.UIThread.UnhandledException` once per process in `App.Run` (`App.axaml.cs:67-78`) with a
  handler that reads the panel through `ResolveMessagePanel` (`:98-102`): with a panel, report through
  `TryReportFailure` with the UI scheduler and set `Handled = true`; without one, leave `Handled` false; the handler
  catches its own failure and logs it
- [ ] write tests: `IScheduler` resolves to `AvaloniaScheduler.Instance` from the production container; a headless
  `Dispatcher.UIThread.Post` of a throwing action adds one Error entry naming the exception type, and the dispatcher
  keeps running the next job; `DispatcherExceptionTests` joins `[Collection(ProcessGlobalStateCollection.Name)]`,
  because `Dispatcher.UIThread` is process-global (`testing-strategy.md:25-27`)
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 3: Serial port seam
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Link/ISerialPortProvider.cs`, `RjcpSerialPortProvider.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Link/LinkErrors.cs` (`PortBusyError`, `PortNotFoundError`,
  `PortLostError`, `BoardNotRespondingError`, `UnrecognisedDataError`)
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/SpectrumVisualizer.UI.csproj` (`PackageReference` to
  `RJCP.SerialPortStream`; the version stays in `Directory.Packages.props:24`)
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Link/FakeSerialPortProvider.cs`, `FakePortStream.cs`,
  `RjcpSerialPortProviderTests.cs`

- [ ] declare `ISerialPortProvider` with `PortNames()` and `Open(string portName)` returning `Result<Stream>`
- [ ] implement `RjcpSerialPortProvider` with the settings of Solution Overview; sort names by the number after `COM`,
  others after them by ordinal; map open exceptions to `PortBusyError` and `PortNotFoundError` with the port name in
  metadata
- [ ] declare the five link errors in one file as FluentResults errors, each with an English message naming the port
- [ ] write the test doubles: `FakePortStream.ReadAsync` returns the next scripted step (bytes, timeout as
  `TimeoutException`, `IOException`) and honours its token; `FakeSerialPortProvider` hands out queued open results and
  counts opens
- [ ] write `RjcpSerialPortProviderTests` for the name ordering (`COM2` before `COM10`) and for opening a name that
  `GetPortNames()` does not list, which returns `PortNotFoundError` without touching hardware
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 4: Link health evaluator
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Link/LinkHealth.cs`, `LinkState.cs` (with `LinkStatus`)
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Link/LinkHealthTests.cs`

- [ ] declare `LinkState` and the `LinkStatus` record of Solution Overview
- [ ] implement `LinkHealth.Evaluate(now, openedAt, lastByteAt, lastFrameAt)` and `LinkHealth.IsDrainDue(now,
  reference, lastDrainAt)` as pure static functions with the 10 s window
- [ ] write table tests for every row of Acceptance Evidence item 1, `LinkHealthTests`, including the inclusive 10 s
  boundary and a byte older than 10 s giving NotResponding
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 5: Serial link data path
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Link/SerialLink.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Link/SerialLinkTests.cs`

- [ ] implement `SerialLink(ISerialPortProvider, IScheduler, ILogger<SerialLink>)` with `Frames`, `Status`,
  `StatusChanged`, `DroppedFrames`, `Connect(string)`, `DisconnectAsync()`, `Dispose()`
- [ ] implement the read loop and the decode loop over a `Pipe` with the options of Solution Overview; the decode loop
  runs `FrameReader.ReadAllAsync`, feeds `SequenceTracker.Observe`, writes the channel and updates the three shared
  fields
- [ ] implement health evaluation after each read or tick and the drain of Solution Overview; publish only changed
  statuses
- [ ] create the bounded channel (64, `DropOldest`, single reader and writer) with the dropped-frame counter
- [ ] write tests: `FramesInRandomChunksReachTheChannel`, `SilenceForTenSecondsReportsBoardNotResponding`,
  `BytesWithoutAValidFrameForTenSecondsReportOtherDevice`, `AStalledCandidateIsDrainedAndTheFramesBehindItArrive`,
  `AFullChannelDropsTheOldestFrame`
- [ ] write tests: a heartbeat-only stream stays Receiving; the lost total follows `SequenceTracker`; a status equal
  to the last one is not raised again
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 6: Reopen, disconnect and link errors in the panel
**Files:**
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Link/SerialLink.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Messages/ErrorMessageMapper.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Link/SerialLinkTests.cs`,
  `UI/Messages/ErrorMessageMapperTests.cs`

- [ ] implement the open-with-retry and port-loss paths of Technical Details: 1 s wait on the link's scheduler,
  `SequenceTracker.Reset()` and a fresh pipe and reader on each open, a fault only on the first failure of a run,
  Debug log lines with the port name for every attempt
- [ ] implement `DisconnectAsync` and `Dispose` as in Solution Overview, and the end of the session on an unexpected
  exception with an `ExceptionalError` fault
- [ ] add one `ErrorMessageMapper` arm per link error with the severities of Solution Overview, and the resx keys for
  their titles, details, remedies and the restored entry, in English and Russian
- [ ] write tests: `AnIOExceptionReportsPortLostAndReopensAfterOneSecond`, `ReopeningResetsTheSequenceBaseline`,
  `APortHeldByAnotherProgramReportsPortBusyAndRetries`, `AMissingPortReportsPortNotFoundAndRetries`,
  `DisconnectCancelsAPendingReadAndStopsRetrying`, `LostSpectraAccumulateAcrossReopensAndResetOnConnect`,
  `EachFaultIsPublishedOnceAndRetriesStayQuiet`
- [ ] write mapper tests: each link error gives its title, remedy and severity; no mapped detail contains the port
  name; two `PortLostError`s on different ports map to equal views
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 7: Link composition and the window's start and stop
**Files:**
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/UiServiceCollectionExtensions.cs`, `App.axaml.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Di/CompositionRootTests.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Di/AppLifecycleTests.cs`

- [ ] register `RjcpSerialPortProvider` as `ISerialPortProvider` and `SerialLink` through a factory lambda with
  `DefaultScheduler.Instance` (singletons)
- [ ] resolve the link in `Configure`; in `CreateMainWindow` (`App.axaml.cs:55-64`) attach the `Closing` handler of
  Solution Overview: cancel the first close, await `DisconnectAsync` with a 5 s limit (a timeout logs a Warning), set
  the flag, close again
- [ ] write tests: the link and the port provider resolve; a headless window close with an active link (fake
  provider) finishes only after the link reports Disconnected; a second close passes at once
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 8: Connection row
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/MainWindow/ConnectionBar.axaml`, `ConnectionBar.axaml.cs`,
  `ConnectionBarViewModel.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/MainWindow/MainWindow.axaml`, `MainWindowViewModel.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/UiServiceCollectionExtensions.cs`, `App.axaml.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/MainWindow/ConnectionBarViewModelTests.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/MainWindow/MainWindowViewTests.cs`,
  `MainWindowViewModelTests.cs` (both construct `MainWindowViewModel`, whose constructor gains the connection row)

- [ ] load the `dev:desktop-ui` skill before writing the AXAML; take every inset and width from its tables
- [ ] implement `ConnectionBarViewModel(ISerialPortProvider, SerialLink, IScheduler uiScheduler)`: `Ports`,
  `SelectedPort`, `RefreshCommand`, `ConnectCommand`, `DisconnectCommand` (`CreateFromTask`), `IsIdle` from
  `StatusChanged` through `ToProperty`; `canExecute` observables from `IsIdle` and `SelectedPort`
- [ ] lay out the row of Solution Overview: `Border` with `Padding="16,8"` (as the status bar row,
  `MainWindow.axaml:49`), a bottom border in `AppBorderBrush`, a horizontal `StackPanel` with `Spacing="8"`, the label
  with `Margin="0,0,4,0"` so label to field is 12, the combo box `Width="240"` with the placeholder
- [ ] add the row to `MainWindow.axaml` (`:18` becomes `Auto,Auto,*,Auto,Auto`); expose the view model on
  `MainWindowViewModel`; register it in `AddUi`; refresh the port list from the window's `Opened` in `App`
- [ ] write view model tests: refresh fills the list and keeps a still-present selection; Connect is disabled with no
  selection and while the link is active; Disconnect is enabled in every state but Disconnected; Connect and
  Disconnect reach the link
- [ ] update `MainWindowViewTests` (`:35-42` construction, row order) and add a realised-row test: label, combo box
  with its placeholder when empty, three buttons
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 9: Status bar bound to the link
**Files:**
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/MainWindow/AppStatusBarViewModel.cs`, `AppStatusBar.axaml`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/UiServiceCollectionExtensions.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/MainWindow/AppStatusBarViewModelTests.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/MainWindow/MainWindowViewTests.cs`,
  `UI/Di/CompositionRootTests.cs`

- [ ] rewrite `AppStatusBarViewModel` (`AppStatusBarViewModel.cs:5-9`) as an `IDisposable` `ReactiveObject` taking
  `SerialLink`, `MessagePanelViewModel`, `IScheduler uiScheduler` and a logger; one `Apply(LinkStatus)` sets
  `LinkText`, `LinkTone`, `PortText`, `Atp2000Text`, `Atp8000Text`, `LostSpectraText`; computed texts notify from the
  setter of their input
- [ ] report faults and the restored entry as in Solution Overview; a throw in `Apply` goes to `TryReportFailure`
- [ ] lay out `AppStatusBar.axaml` (`:12-14`) as a left-aligned `Grid` of `Auto` columns: an 8 px dot with
  `Margin="0,0,8,0"` and the link text, then each segment after a 1 px `AppSubtleLineBrush` separator with
  `Margin="8,4"`; dot colour through three classes bound to `LinkTone`
- [ ] write view model tests over a `TestScheduler`: the text for every state of Technical Details; the four
  spectrometer texts from the state bits; "-" for port and lost count while disconnected; one fault entry per faulted
  status; one Info entry on Receiving after a fault and none after the first connect; a throwing panel goes to the log
- [ ] update `MainWindowViewTests` to build the bar from a link over the fake provider and assert every segment is
  realised; extend `CompositionRootTests`
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 10: Desktop UI pre-handover checklist
**Files:**
- Modify (only to fix a miss): `SpectrumVisualizer/SpectrumVisualizer.UI/MainWindow/ConnectionBar.axaml`,
  `AppStatusBar.axaml`, `MainWindow.axaml`, `Messages/MessagePanelView.axaml`, the resx pair

- [ ] every gap and inset in the connection row and the status bar reads off the spacing table; the `ui-theme.md:61-63`
  grep prints nothing
- [ ] every field width reads off the width table (the port combo box 240) or carries a stated reason
- [ ] at the window's minimum width the connection row and every status segment show; record the measured width
- [ ] at a large width no gap grows between related items: the connection row and the status bar stay left-aligned
- [ ] every label, button and status text fits on one line in English and Russian, measured in the realised headless
  window with the longest state text
- [ ] every link state keeps the window size: the status bar and the connection row change text and colour, never their
  set of rows; the `ui-theme.md:74-76` grep prints nothing

### Task 11: Emulator dark frames, failure bits and both spectrometers
**Files:**
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tools.BoardEmulator/EmulatorOptions.cs`, `EmulatorCommand.cs`,
  `FrameSchedule.cs`, `SpectrumGenerator.cs`, `Program.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Tools/FrameScheduleTests.cs`, `EmulatorCommandTests.cs`,
  `SpectrumGeneratorTests.cs`

- [ ] add `DarkEvery`, `BothConnected` and `Failed` (a nullable `Spectrometer`) to `EmulatorOptions` (`:6-12`); add
  `--dark-every`, `--both-connected` and `--failed` beside the options at `EmulatorCommand.cs:17-54`, list them in
  `Build` (`:58-66`) and `From` (`:68-78`), and add the cross-option rule of Solution Overview as a command validator
- [ ] add `SpectrumGenerator.Dark(device, wavelengthNm, delta)`: all-zero pixels, amplitude 0, snr 0
- [ ] derive the state in `FrameSchedule` (`:12-13`) from the options; send the dark spectrum as every nth spectrum;
  clear the emulated spectrometer's failed bit in its spectrum frames; print the new options in the start line
  (`Program.cs`)
- [ ] write tests: every third spectrum is dark with `--dark-every 3` and `seq` keeps counting; `--failed 8000` with
  `--device 8000` sets bit 3 in heartbeats only; `--failed 2000 --both-connected` sets bit 2 in every frame;
  `--both-connected` sets bits 0 and 1; the rejected combination prints its error and exits 1; the defaults leave the
  frames unchanged
- [ ] run `dotnet test SpectrumVisualizer.slnx` and the emulator `--help` - must pass before next task

### Task 12: Verify acceptance criteria
- [ ] run every command of Acceptance Evidence and record each result with its count
- [ ] verify every Overview item: the link reads frames, the status bar shows every state, faults reach the panel
  once, the seams of the skeleton are closed
- [ ] verify edge cases: a stall drained without OtherDevice, a port lost mid-frame, Disconnect during the reopen wait,
  a window close during a reopen wait
- [ ] run the full suite: `dotnet test SpectrumVisualizer.slnx`; confirm the old solution builds and passes 13 of 13
- [ ] start the app with `dotnet run --project SpectrumVisualizer/SpectrumVisualizer.UI` and confirm the log gains its
  start line; leave the port-pair checklist to the owner

### Task 13: [Final] Update documentation
- [ ] `docs/architecture/board-protocol.md`: replace the recovery sentence (`:109-112`) with the drain; add a link
  section (read loop, pipe options, health rules, drain, reopen, channel capacity and full mode, one reader); add the
  new emulator options to the emulator section
- [ ] `docs/architecture/overview.md`: purpose paragraph, components table (the link classes, connection row, status
  bar), startup (the `Opened` refresh and the `Closing` stop), main window rows (`:71-78`), error route with severities
  (`:100-101`) and dispatcher exceptions (`:113-115`)
- [ ] `docs/architecture/README.md`: Errors row (`:28`) and Serial library row (`:33`), plus a Serial link row for the
  channel and the reopen rule
- [ ] `docs/architecture/testing-strategy.md`: the Area list (`:18-20`) gains `Link`; a Link row in the table of
  test kinds (fake port stream, `TestScheduler`)
- [ ] `docs/architecture/ui-theme.md`: the five new keys in the key table (`:44-50`); `docs/architecture/ui-text.md`:
  the new key owners in the table (`:39-45`)
- [ ] `readme.md`: replace the "shell for now" paragraph (`:13-15`) and describe connecting to the emulator over a port
  pair
- [ ] `CLAUDE.md`: add `Link` to the Area list (`:40-41`); add a line if a new pattern appeared during implementation

## Post-Completion
**Manual verification:**
- the smoke checklist of Acceptance Evidence: steps 1 to 8 and 10 to 13 over a com0com pair, step 9 and the wrong-baud
  part of step 6 over a real USB-UART pair, during the owner's final assembly check.
- the real board after the firmware-frame-sender slice: the same checklist against the board instead of the emulator.

**Remaining slices:**
- spectrum-view: the live spectrum screen with parity to the old app, reading the frame channel through a
  displayed-spectrum source.
- spectrum-store: SQLite storage of every received spectrum with 24 h retention and the intensity-series query.
- intensity-trend: the intensity-over-time chart at the SCADA wavelength with SemiPlot trend behavior and a historical
  spectrum on click.
- firmware-frame-sender: the OptoskyProj#20 frame sender in the board firmware, verified on the board against the app.
- release-3-0: the 3.0.0 installer over the self-contained publish folder, the release workflow, and removal of the
  old app.
