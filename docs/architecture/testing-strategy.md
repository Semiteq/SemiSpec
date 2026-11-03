# Testing strategy

## One test project

`SpectrumVisualizer/SpectrumVisualizer.Tests.Unit` holds every test of the new app. A test's category is decided by the
boundary it crosses: a unit test opens no socket, starts no container and touches no real port. Nothing in the app
crosses a machine boundary today, so there is no integration project. One is added when a test needs Docker or a real
serial port.

The project references `SpectrumVisualizer.Core` and `SpectrumVisualizer.UI`; both name it in `InternalsVisibleTo`. It is
an executable (`OutputType Exe`, as xUnit 3 requires), and `xunit.runner.json` sets `failSkips: true`, so no test may be
skipped.

## Conventions

- Plain logic uses `[Fact]`. Anything that touches ReactiveUI or Avalonia uses `[AvaloniaFact]`, which runs on the
  headless UI thread.
- Every test class carries `[Trait("Component", "Core|UI")]`, `[Trait("Area", ...)]` and
  `[Trait("Category", "Unit")]`. The areas are `Di`, `Logging`, `MainWindow`, `Messages`, `Paths` and `Resources`.
- Assertions use AwesomeAssertions. A test that reads UI text compares it with the generated `Resources` accessor,
  never with a literal, so it passes under either UI culture.
- Test folders mirror the production project: `Core/` for Core, `UI/<folder>/` for each UI folder.

## Headless app

`SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/TestAppBuilder.cs` registers the test application with
`[assembly: AvaloniaTestApplication]`. It configures the real `App` with `UseHeadless` and `UseReactiveUI(_ => { })`, so
`App.axaml` loads the same palette, Semi theme and styles as the running app. It installs no exception handler of its own.

## What each kind of test owns

| Kind | Owns | Example |
| --- | --- | --- |
| Plain unit | Pure logic: folder resolution, log level switching, error mapping | `Core/AppDataPathsTests.cs`, `UI/Logging/LogLevelControlTests.cs` |
| View model | Commands and state without a window: the verbose-logging command, the panel bound and coalescing, the reporting route | `UI/MainWindow/MainWindowViewModelTests.cs`, `UI/Messages/` |
| Realised view | What a headless window shows: the menu, the placeholder, the hidden message panel, the status bar, the panel opening on a report, Exit closing the window | `UI/MainWindow/MainWindowViewTests.cs` |
| Composition root | Every service registered by `AddUi` resolves from the production container; `App` builds the window from it | `UI/Di/CompositionRootTests.cs` |
| App builder | `App.BuildAvaloniaApp` selects Win32, Skia and a text shaper | `UI/Di/AppBuilderCompositionTests.cs` |
| Resources | Every neutral resx key exists in the Russian set and is non-empty; the palette keys the styles read resolve | `UI/AppResourcesTests.cs` |

`LogLevelControlTests` logs through an in-memory sink and asserts which events pass the level switch; `AppLoggingTests`
writes a real file under a temporary folder and reads it back. The message panel takes its clock from an injected
`TimeProvider`, so a test that reads back a timestamp does not depend on the host's zone.

## End to end

No end-to-end framework exists. Headless realised-view tests take that role inside the process.

## Gates

`dotnet build SpectrumVisualizer.slnx` fails on any analyzer or style warning (`TreatWarningsAsErrors`,
`EnforceCodeStyleInBuild`, `AnalysisMode=Recommended`). `dotnet format --verify-no-changes` and `dotnet terse` run in the
pre-commit hook over staged files and in CI over the whole tree.
