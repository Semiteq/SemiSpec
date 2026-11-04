# Testing strategy

## One test project

`SpectrumVisualizer/SpectrumVisualizer.Tests.Unit` holds every test of the new app. A test's category is decided by the
boundary it crosses: a unit test opens no socket, starts no container and touches no real port. The serial link runs
over a fake port in every test, so no test crosses a machine boundary and there is no integration project. One is
added when a test needs Docker or a real serial port.

The project references `SpectrumVisualizer.Core`, `SpectrumVisualizer.UI` and `SpectrumVisualizer.Tools.BoardEmulator`;
the UI project names it in `InternalsVisibleTo`. It is an executable (`OutputType Exe`, as xUnit 3 requires), and
`xunit.runner.json` sets `failSkips: true`, so no test may be skipped.
`RjcpSerialPortProvider.SortPortNames` and `MapOpenFailure` are the one exception to "no visibility widened for a test":
they are `internal` pure functions in front of a hardware boundary that no test reaches without a port.

## Conventions

- Plain logic uses `[Fact]`. Anything that touches ReactiveUI or Avalonia uses `[AvaloniaFact]`, which runs on the
  headless UI thread.
- Every test class carries `[Trait("Component", "Core|UI|Tools")]`, `[Trait("Area", ...)]` and
  `[Trait("Category", "Unit")]`. The areas are `Calibration`, `Di`, `Emulator`, `Link`, `Logging`, `MainWindow`,
  `Messages`, `Paths`, `Protocol` and `Resources`.
- Assertions use AwesomeAssertions. A test that reads UI text compares it with the generated `Resources` accessor,
  never with a literal, so it passes under either UI culture.
- Test folders mirror the production project: `Core/` and its subfolders for Core, `UI/<folder>/` for each UI folder,
  `Tools/` for the board emulator.
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
| Emulator | The frame schedule, the `state` bits, the dark spectrum and the synthetic spectrum against a test `TimeProvider`; option parsing and the cross-option rule through `BuildCommand(...).Parse`; the port write is not tested | `Tools/` |
| Link | The health rules as a table; the session over `FakeSerialPortProvider` (editable port list, a port list that fails, a count of port list reads, queued open results, an open with none queued throws) and `FakePortStream`, whose `ReadAsync` returns scripted steps in order: a chunk of bytes, a read timeout, a 0-byte read, an exception; its `DisposeAsync` can be held until the test releases it. Time is a `TestScheduler` that counts the timers the link arms, so a retry that publishes no new status is still observable: a test advances virtual time and pushes a timeout step, so the link judges its health at a known virtual time. Every wait for a status has a 5 s real-time limit (`Waits` in `UI/`), so a hung loop fails instead of blocking the run. The session tests share `SerialLinkTestBase` and split into stream and health, port loss and reopen, and session end and channel. The port name ordering and open-failure mapping of `RjcpSerialPortProvider`, without hardware | `UI/Link/` |
| View model | Commands and state without a window: the verbose-logging command, the connection row's commands and port list, the status bar text per link state, its fault and restored entries, the panel bound and coalescing, the severity and its log level, the reporting route. The status bar and connection row tests drive a real `SerialLink` over the fakes | `UI/MainWindow/`, `UI/Messages/` |
| Realised view | What a headless window shows: the row order, the menu, the connection row, the placeholder, the hidden message panel, every status segment, the panel opening on a report, a realised entry row (time, title, repeat line, remedy, severity dot class), Clear hiding the row, Exit closing the window | `UI/MainWindow/MainWindowViewTests.cs` |
| Layout | The window's minimum width holds the connection row and the longest status texts in English and Russian; both rows stay left-aligned at a large width. A row's chrome comes from an unbounded measure, because Avalonia clamps `DesiredSize` to the space offered and a clamped size never shows an overflow. Text widths come from `RenderedText`, which measures through SkiaSharp in Inter and the installed families of the control's font, because the headless text stub gives no real width; `CultureScope` switches the UI culture | `UI/MainWindow/MainWindowLayoutTests.cs`, `MainWindowTextFitTests.cs` |
| Composition root | Every service registered by `AddUi` resolves from the production container, `IScheduler` as `AvaloniaScheduler.Instance`; the verbose toggle writes through the application logger at Information and Debug; `App` builds the window from the container and refuses to before `Configure` | `UI/Di/CompositionRootTests.cs` |
| App lifecycle | Opening the window fills the port list; a window close waits until the link reports Disconnected, with the port's dispose held so the first close is cancelled every run, and also during a reopen wait; with an idle link the window closes at once; a throwing dispatcher job adds one Error entry and the dispatcher runs the next job, and without a panel the exception stays unhandled | `UI/Di/AppLifecycleTests.cs`, `UI/Di/DispatcherExceptionTests.cs` |
| App builder | `App.BuildAvaloniaApp` selects Win32, Skia and a text shaper | `UI/Di/AppBuilderCompositionTests.cs` |
| Resources | Every neutral resx key exists in the Russian set and is non-empty; the `App*` palette keys the views read resolve | `UI/AppResourcesTests.cs` |

`LogLevelControlTests` logs through an in-memory sink and asserts which events pass the level switch; `AppLoggingTests`
writes a real file under a temporary folder and reads it back. The message panel takes its clock from an injected
`TimeProvider`, so a test that reads back a timestamp does not depend on the host's zone.

## Known gaps

- The 5 s stop limit of `App.OnMainWindowClosing` has no test. Reaching it needs a link that ignores cancellation for
  5 s of real time; the project adds no clock package and no test-only seam for that one branch.
- The 0-byte read and the vanished-port tests prove the backstops of the read loop, not the real unplug: the fake
  cancels and fails at once, where a driver may block. The real unplug path is covered by the manual checklist of the
  serial link plan.
- `RjcpSerialPortProviderTests` opens a port name this machine does not have through the real `SerialPortStream`, so
  the suite runs on Windows only, like the app.

## End to end

No end-to-end framework exists. Headless realised-view tests take that role inside the process.

## Gates

`dotnet build SpectrumVisualizer.slnx` fails on any analyzer or style warning (`TreatWarningsAsErrors`,
`EnforceCodeStyleInBuild`, `AnalysisMode=Recommended`). `dotnet format --verify-no-changes` and `dotnet terse` run in
the pre-commit hook over the staged `.cs` files under `SpectrumVisualizer/`. In CI, `dotnet format` checks the projects
of `SpectrumVisualizer.slnx` and `dotnet terse` checks every tracked `.cs` file under `SpectrumVisualizer/`; the old
app's sources are not gated.
