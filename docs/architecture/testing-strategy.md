# Testing strategy

## One test project

`SpectrumVisualizer/SpectrumVisualizer.Tests.Unit` holds every test of the new app. A test's category is decided by the
boundary it crosses: a unit test opens no socket, starts no container and touches no real port. The serial link runs
over a fake port in every test, so no test crosses a machine boundary and there is no integration project. One is
added when a test needs Docker or a real serial port.

The project references `SpectrumVisualizer.Core`, `SpectrumVisualizer.UI`, `SpectrumVisualizer.Tools.BoardEmulator` and
`SpectrumVisualizer.Tools.StoreBenchmark`;
the UI project names it in `InternalsVisibleTo`. It is an executable (`OutputType Exe`, as xUnit 3 requires), and
`xunit.runner.json` sets `failSkips: true`, so no test may be skipped.
Two exceptions break "no visibility widened for a test". `RjcpSerialPortProvider.SortPortNames` and `MapOpenFailure`
are `internal` pure functions in front of a hardware boundary that no test reaches without a port.
`SpectrumChartView.ServedFrameCount` is an `internal` count of the animation-frame callbacks that served a redraw; the
headless surface shows no other trace of frame pacing, so the frame-joining tests read it.

## Conventions

- Plain logic uses `[Fact]`. Anything that touches ReactiveUI or Avalonia uses `[AvaloniaFact]`, which runs on the
  headless UI thread.
- Every test class carries `[Trait("Component", "Core|UI|Tools")]`, `[Trait("Area", ...)]` and
  `[Trait("Category", "Unit")]`. The areas are `Calibration`, `Chart`, `Di`, `Emulator`, `Link`, `Logging`,
  `MainWindow`, `Messages`, `Paths`, `Protocol`, `Resources`, `Spectrum` and `Store`.
- Assertions use AwesomeAssertions. A test that reads UI text compares it with the generated `Resources` accessor,
  never with a literal, so it passes under either UI culture.
- Test folders mirror the production project: `Core/` and its subfolders for Core, `UI/<folder>/` for each UI folder,
  `Tools/` for both console tools.
- Board frames for any test come from `Core/Protocol/ProtocolSamples.cs`; its spectrum builder is `SpectrumFrame`,
  because a member named `Spectrum` would collide with the `SpectrumVisualizer.Tests.Unit.UI.Spectrum` namespace in
  every test under `UI/`.
- A class that writes process-global state joins `[Collection(ProcessGlobalStateCollection.Name)]`
  (`ProcessGlobalStateCollection.cs`), which runs with parallelization off. Its members are
  `AppBuilderCompositionTests`, because `LogToTrace()` writes Avalonia's `Logger.Sink`, and `DispatcherExceptionTests`,
  because it subscribes `Dispatcher.UIThread.UnhandledException`.

## Headless app

`SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/TestAppBuilder.cs` registers the test application with
`[assembly: AvaloniaTestApplication]`. It configures the real `App` with `UseHeadless` and `UseReactiveUI(_ => { })`, so
`App.axaml` loads the same palette and Semi theme as the running app. It installs no exception handler of its own.

## What each kind of test owns

| Kind | Owns | Example |
| --- | --- | --- |
| Plain unit | Pure logic: folder resolution, log level switching, error mapping | `Core/AppDataPathsTests.cs`, `UI/Logging/LogLevelControlTests.cs` |
| Protocol | Frames encoded and decoded whole, split at every byte boundary, and fed through a `Pipe` in random chunks from a fixed seed; resynchronisation after a bad candidate; `DecodeBuffered` across writes and after completion; the `seq` rules | `Core/Protocol/` |
| Calibration | `PixelFor` against the firmware pixel tables copied into `FirmwarePixelTables.cs`; clamping and the window | `Core/Calibration/` |
| Emulator | The frame schedule, the `state` bits, the dark spectrum and the synthetic spectrum against a `FakeTimeProvider`; option parsing and the cross-option rule through `BuildCommand(...).Parse`; the port write is not tested | `Tools/` |
| Link | The health rules as a table; the session over `FakeSerialPortProvider` (editable port list, a port list that fails, a count of port list reads, queued open results, an open with none queued throws) and `FakePortStream`, whose `ReadAsync` returns scripted steps in order: a chunk of bytes, a read timeout, a 0-byte read, an exception; its `DisposeAsync` can be held until the test releases it. Time is a `TestScheduler` that counts the timers the link arms, so a retry that publishes no new status is still observable: a test advances virtual time and pushes a timeout step, so the link judges its health at a known virtual time. Every wait for a status has a 5 s real-time limit (`Waits` in `UI/`), so a hung loop fails instead of blocking the run. The session tests share `SerialLinkTestBase` and split into stream and health, port loss and reopen, and session end and channel. The port name ordering and open-failure mapping of `RjcpSerialPortProvider`, without hardware. `FrameDistributor` over a real `Channel.CreateBounded<ReceivedFrame>` with the link's options: every frame reaches every subscriber in order, a late subscriber sees only later frames, the latest spectrum skips heartbeats, a throwing subscriber leaves the frame to the others and adds one Error entry to a real panel, an unsubscribed observer receives nothing more, the start and stop rules, where a counting reader shows that a `Start` after a stop begins no read within a bounded wait, and a failing read logged at Error while `StopAsync` still completes. `SerialLinkDropTests`: a reader that does not read sees the channel's drops in `LinkStatus.DroppedFrames` after the next publish, the count survives a reconnect, the Disconnected status carries it, and a channel drop never counts as lost | `UI/Link/` |
| Store | Real SQLite files in a fresh `TempFolder` (test project root, shared with the UI tests) under `Path.GetTempPath()` per test, deleted in `Dispose` after the connections are disposed and `SqliteConnection.ClearAllPools()` runs. `StoreFiles`: the instance file name, the own-file wipe, a dead instance's file deleted, a live instance's file kept while a second `SqliteConnection` holds it, orphan sidecars, a missing folder, a name outside `spectra-<digits>.db` kept. `SpectrumDatabase`: the pragmas read back, every field round-tripped, NaN stored as NULL, heartbeats in their table, a batch as one transaction, retention over both tables, the truncated WAL, a write against a lock held by a second connection's `BEGIN IMMEDIATE` failing after the 2 s timeout, and after 1 s once `ShortenBusyTimeout` ran, and a checkpoint beside an open reader returning Busy at once. `QueryWindows` and `IntensityBuckets` as table tests. `SpectrumHistory`: the nearest row at or before a moment, the rebuilt measurement, window means, uncovered columns, rows stored at another wavelength, dark frames, cancellation before the call and mid-read (a per-connection `substr` that cancels at row 300), the stored range, NaN read back as NaN, a row inside the millisecond of `To`, a missing file read as a failure that creates no file, a read beside an open write transaction, and a writer commit beside an open read snapshot, which only WAL allows. `SpectrumStore` over a real distributor and file, in write, stop and failure classes over `SpectrumStoreTestBase`, with `RetentionClock`, a `FakeTimeProvider` that also counts its reads and can throw from them, whose timers fire on `Advance`: every frame stored in order, the subscriber only enqueues while a second connection holds the writer, a full queue and failed batches counted as dropped with one panel entry per incident, retention without frames, retention keeping a row 23 h 59 min old and running nothing before the minute, the drain budget, a stop against a lock held through it ending inside the close limit with each later batch giving up after 1 s, a stop during a retention pass ending inside the close limit and starting no later retention pass, the WAL left at 0 bytes while an idle connection holds it across the stop, dispose counting the queued frames as dropped, a failed open, an open that throws, and a failure after the open, each reporting once and counting every later frame as dropped. The benchmark tool's option parsing and a 2 000-row run whose two pixel reads agree column for column; the 24 h run stays out of the suite | `Core/Store/`, `UI/Store/`, `Tools/` |
| Chart | `LogAxis` ticks copied from SemiPlot (tick families, minors, the bounds that return nothing), in Core; `LogTickGenerator` labels under `CultureScope("en")` and `CultureScope("ru")`, fractional majors with each culture's decimal separator, a repeated label turning minor, the cache returning the same array and regenerating after a culture change; `ChartPalette` on a real `Plot` under both variants, the translucent band at alpha 64, a missing key naming the key | `Core/Chart/`, `UI/Chart/` |
| Spectrum | `SpectrumProjection` and `SpectrumScale` as table tests in Core, with finite limits for an all-zero spectrum in every `SpectrumDisplay`. The live source over a distributor on a test channel. `SpectrumChartViewModel` with `SpectrumSamples` (an ATP-2000 and an ATP-8000 peak, a dark spectrum per device), split into what it draws (the X range per device, the band and centre line, the axis type, the untouched pixel array, the palette under the plot lock) and the scale (the follow and frozen scale, a log or inversion toggle against `SpectrumScale.Following` in the new mode, the dark-frame reference per device: kept limits, Stick to zero, a toggle during a dark frame, the device's own last peak after a frame of the other device, and the flat range for a dark first frame after a device switch; no freeze before the first spectrum; `OnHeldMove` against the 5 px floor on either axis and without a held button). Render tests call `Plot.RenderInMemory(640, 480)` after a dark frame on both axis types and check four finite limits and the kept limits. `SignalInfoViewModel` texts under both cultures with a `TimeProvider` in a fixed zone. `SpectrumViewModel` over `FakeDisplayedSpectrumSource` (a settable `Current`, a `Signal()` method and `HasSubscribers`): queued signals apply once, the same measurement applies once, a throwing `Show` or `Current` reports once and the next spectrum shows, dispose ends the subscription, the commands' enabled state. `SpectrumChartView` realised headless over `SpectrumChartViewTestBase`: the cache layout, a burst of redraw requests joining one frame, the empty state, the variant repaint and its removal on unload, a null `DataContext` and back keeping the plottables, a `DataContext` switch ending the old redraw subscription, a disposed chart leaving no frame; and in `SpectrumChartViewInputTests` the input responses and headless wheel, press, move and release events against `IsFollowing`, with the 5 px drag floor | `Core/Spectrum/`, `UI/Spectrum/` |
| View model | Commands and state without a window: the verbose-logging command, the connection row's commands and port list, the status bar text per link state, its fault and restored entries, the panel bound and coalescing, the severity and its log level, the reporting route, the store errors' mapping. The status bar and connection row tests drive a real `SerialLink` over the fakes; the dropped segment sums the link's and a real store's counts, shows 0 on a fresh app, keeps its count after a disconnect, and moves a store drop counted on the writer thread to the UI scheduler. View tests that only read the count use `UnstartedStore`, a store that is never started | `UI/MainWindow/`, `UI/Messages/` |
| Realised view | What a headless window shows: the row order, the menu, the connection row, the chart pane, the hidden message panel, every status segment, the verbose-logging item, Exit closing the window; the signal pane with every row and control and a checkbox reaching the chart; the message panel opening on a report, a realised entry row (time, title, repeat line, remedy, severity dot class), Clear hiding the row | `UI/MainWindow/MainWindowViewTests.cs`, `MainWindowSignalPaneTests.cs`, `MainWindowMessagePanelTests.cs` |
| Layout | The window's minimum width holds the connection row and the longest status texts, the dropped segment included, in English and Russian, and stays at or under the opening width, at which the realised window opens; both rows stay left-aligned at a large width. The signal pane's insets read off the spacing table on the arranged grid cells; every pane label, value and control fits on one line within `SpectrumPanel.PaneWidth` in both languages; at the minimum width the pane shows every row and the chart keeps its 400 px floor, where an ATP-8000 spectrum keeps at least four labelled wavelength ticks; at a large width only the chart grows; no spectrum state changes the window or the pane bounds. A row's chrome comes from an unbounded measure, because Avalonia clamps `DesiredSize` to the space offered and a clamped size never shows an overflow. Text widths come from `RenderedText`, which measures through SkiaSharp in Inter and the installed families of the control's font, regular or bold, because the headless text stub gives no real width; `CultureScope` switches the UI culture. `MainWindowLayoutTestBase` holds the shown window, the pane's part names and the inset constants every layout class reads | `UI/MainWindow/MainWindowLayoutTests.cs` (the rows), `MainWindowSignalPaneTests.cs` (the pane), `MainWindowTextFitTests.cs` |
| Composition root | Every service registered by `AddUi` resolves from the production container, built over a per-test `TempFolder` root, `IScheduler` as `AvaloniaScheduler.Instance`; the distributor and the live source are one instance each; the store is one instance over the instance file under the store folder; and the window shows the container's spectrum view model; the verbose toggle writes through the application logger at Information and Debug; `App` builds the window from the container and refuses to before `Configure` | `UI/Di/CompositionRootTests.cs` |
| App lifecycle | Opening the window fills the port list and starts the distributor, so a frame pushed through `FakePortStream` reaches a distributor subscriber, and starts the store, so the frame lands in the instance file; the store's stop line follows the distributor's, and after the close the store file's `-wal`, held open by an idle connection, is 0 bytes; each test awaits every store's `StopAsync` before it deletes its root; a window close waits until the link reports Disconnected, with the port's dispose held so the first close is cancelled every run, and also during a reopen wait; with an idle link the window closes once the distributor stops; the distributor's stop line follows the link's; after the close a reconnected link writes a frame that the test reads back from `SerialLink.Frames`, so the stopped distributor never forwarded it; a throwing dispatcher job adds one Error entry and the dispatcher runs the next job, and without a panel the exception stays unhandled | `UI/Di/AppLifecycleTests.cs`, `UI/Di/DispatcherExceptionTests.cs` |
| App builder | `App.BuildAvaloniaApp` selects Win32, Skia and a text shaper | `UI/Di/AppBuilderCompositionTests.cs` |
| Resources | Every neutral resx key exists in the Russian set and is non-empty; the `App*` palette keys the views read resolve | `UI/AppResourcesTests.cs` |

`LogLevelControlTests` logs through an in-memory sink and asserts which events pass the level switch; `AppLoggingTests`
writes a real file under a temporary folder and reads it back. The message panel takes its clock from an injected
`TimeProvider`, so a test that reads back a timestamp does not depend on the host's zone. Every test clock is a
`FakeTimeProvider` from Microsoft.Extensions.TimeProvider.Testing: the panel's steps 1 s per read through
`AutoAdvanceAmount`, the signal pane's sets its zone with `SetLocalTimeZone`, and the store's subclass adds only what
the package lacks.

## Known gaps

- The 5 s stop limit of `App.OnMainWindowClosing` has no test. Reaching it needs a link that ignores cancellation for
  5 s of real time, since the limit takes no `TimeProvider`; the project adds no test-only seam for that one branch.
- The 0-byte read and the vanished-port tests prove the backstops of the read loop, not the real unplug: the fake
  cancels and fails at once, where a driver may block. The real unplug path is covered by the manual checklist of the
  serial link plan.
- `RjcpSerialPortProviderTests` opens a port name this machine does not have through the real `SerialPortStream`, so
  the suite runs on Windows only, like the app.
- No test observes that `Opened` starts the store before the distributor. The distributor reads on a thread-pool task,
  so with the two starts swapped the store's subscription still wins the race on most runs; only a test-only hook
  could make the lost frame certain.

## End to end

No end-to-end framework exists. Headless realised-view tests take that role inside the process.

## Gates

`dotnet build SpectrumVisualizer.slnx` fails on any analyzer or style warning (`TreatWarningsAsErrors`,
`EnforceCodeStyleInBuild`, `AnalysisMode=Recommended`). `dotnet format --verify-no-changes` and `dotnet terse` run in
the pre-commit hook over the staged `.cs` files under `SpectrumVisualizer/`. In CI, `dotnet format` checks the projects
of `SpectrumVisualizer.slnx` and `dotnet terse` checks every tracked `.cs` file under `SpectrumVisualizer/`; the old
app's sources are not gated.
