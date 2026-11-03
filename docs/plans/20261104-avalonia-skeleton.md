# Avalonia skeleton

## Overview
- Create the new SpectrumVisualizer application shell beside the old WinForms app: a solution under `SpectrumVisualizer/`
  on the stack and conventions of SemiPlot (Avalonia 12, ReactiveUI, Semi theme, FluentResults, Serilog, xunit.v3).
- The shell starts, shows the main window with its rows (menu, content placeholder, message panel, status bar), logs to
  a fixed folder, routes every failure to the message panel, and lets the operator raise the log level at run time.
- It carries no serial, protocol, chart or storage code; later slices fill the content row.
- The old app (`SpectrumVisualizer.Uart`, `SpectrumVisualizer.Tests`) stays at the repo root and keeps building and
  passing its tests. The vendor-SDK project `SprctrumVisualizer` and the `ComPortPopulator` tool are deleted.
- Scope guard: no serial, protocol, chart or storage code; no change to the old app's sources; no Tests.Integration
  project; no launch keys, no config files.

## Context (from discovery)
- Roadmap: docs/plans/roadmaps/20261104-avalonia-rewrite-roadmap.md — slice avalonia-skeleton
- Stacking base: `master` at `210ff5c`; work on the local branch `avalonia-skeleton`; local commits only.
- Old solution `SpectrumVisualizer.sln` lists four projects: `SprctrumVisualizer\SpectrumVisualizer.csproj`
  (SpectrumVisualizer.sln:6), `SpectrumVisualizer.Uart` (:8), `SpectrumVisualizer.Tests` (:10), `ComPortPopulator` (:12).
- The old app targets `net9.0-windows` (SpectrumVisualizer.Uart/SpectrumVisualizer.Uart.csproj:5) and has no
  `Directory.Build.props` above it; the repo root carries a 623-line `.editorconfig` (`.editorconfig:1-5`: utf-8-bom,
  crlf, 4-space indent) that the old projects rely on.
- Verified 2026-11-03: `dotnet test SpectrumVisualizer.Tests` passes 13 of 13 with 15 nullable warnings.
- SemiPlot reference, read this session:
  - `global.json:1-6` SDK 10.0.100, `rollForward: latestFeature`; `nuget.config:1-7` nuget.org only.
  - `SemiPlot/Directory.Build.props:3-39`: `ArtifactsPath`, net10.0, LangVersion 14, `TreatWarningsAsErrors`,
    `WarningsNotAsErrors` NU1900-NU1904, `EnforceCodeStyleInBuild`, `AnalysisMode=Recommended`,
    `GenerateDocumentationFile` with `CS1591` off (IDE0005 unlock), Version 0.0.0, Release `DebugType=None`, publish
    win-x64 self-contained ReadyToRun.
  - `SemiPlot/Directory.Packages.props:4-42`: central package management and the pins this slice reuses.
  - `.githooks/pre-commit:1-35`: refuses master/main, runs `dotnet terse` and `dotnet format --verify-no-changes` on
    staged `.cs` files; `.config/dotnet-tools.json:1-13` pins terse 1.1.0.
  - `.github/workflows/ci.yml:1-60`: path filters, `unit-windows` job restoring, building and testing the slnx.
  - `SemiPlot.UI/SemiPlot.UI.csproj:3-47`: WinExe, `StartupObject`, `NeutralLanguage=en`, resx source generation
    (`:12`), `InternalsVisibleTo` tests (`:16`).
  - `SemiPlot.UI/Program.cs:17-65` entry point and `:97-122` Serilog configuration (console + file, 5 MB rolling, 5 files,
    shared, ProcessId enricher, invariant culture).
  - `SemiPlot.UI/App.axaml:1-21` include manifest (Palette, Icons, Semi themes, Forms, Buttons styles).
  - `SemiPlot.UI/App.axaml.cs:36-39` process-lifetime `UnhandledErrorObserver`; `:271-281` `BuildAvaloniaApp` with
    `UseWin32`, `UseHarfBuzz`, `UseReactiveUI(... WithExceptionHandler ...)`, `LogToTrace`.
  - `SemiPlot.UI/MainWindow/MainWindow.axaml:22` rows `Auto,Auto,*,Auto,Auto,Auto`.
  - `SemiPlot.UI/Messages/UnhandledErrorObserver.cs:12-38`, `MessagePanelViewModel.cs:13-15` (bounded to 200 entries),
    `ResultReporting.cs:15-78` (`ReportFailure`, `TryReportFailure`), plus `MessageEntry.cs`, `MessageSeverity.cs`,
    `SeverityMatchConverter.cs`, `MessagePanelView.axaml(.cs)`.
  - `SemiPlot.UI/Styles/Palette.axaml` (185 lines), `Forms.axaml`, `Buttons.axaml`, `Icons.axaml`.
  - `SemiPlot.UI/MainWindow/AppStatusBar.axaml` (43 lines) and `AppStatusBarViewModel.cs` (146 lines).
  - `SemiPlot.Tests.Unit/TestAppBuilder.cs:10-21` headless app builder; `SemiPlot.Tests.Unit.csproj:3-24` test packages.
- Canonical files from the `dev:project-layout` skill: `assets/editorconfig/csharp.editorconfig`,
  `assets/msbuild/Directory.Build.props`.

## Development Approach
- **testing approach**: Regular (code first, then tests in the same task).
- complete each task fully before moving to the next; small, focused changes.
- every task includes new or updated tests for its code; all tests pass before the next task.
- update this plan file when scope changes during implementation.
- the old solution must build and pass its 13 tests after every task.

## Testing Strategy
- **unit tests**: xunit.v3 with AwesomeAssertions in `SpectrumVisualizer.Tests.Unit`; `[AvaloniaFact]` for anything that
  touches ReactiveUI or Avalonia, `[Fact]` for plain logic. Every test class carries `Component`, `Area` and
  `Category=Unit` traits.
- **headless UI tests**: the main window and the message panel are realised headless and asserted on what they show.
- **composition root tests**: resolving every registered service from the container built by the production
  composition root.
- no e2e framework exists; headless tests take that role.

## Acceptance Evidence
Automatable:
1. `dotnet build SpectrumVisualizer.slnx -c Release` exits 0 with 0 warnings.
2. `dotnet test SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/SpectrumVisualizer.Tests.Unit.csproj -c Release` passes
   every test, including the headless main-window test that finds the menu, the content placeholder, the message panel
   and the status bar, and the composition root test.
3. `dotnet format SpectrumVisualizer.slnx --verify-no-changes` exits 0; `dotnet terse` on every new `.cs` file exits 0.
4. `dotnet build SpectrumVisualizer.sln` exits 0 and `dotnet test SpectrumVisualizer.Tests` passes 13 of 13 (old app
   untouched); the solution no longer lists `SprctrumVisualizer` or `ComPortPopulator`.
5. A headless test raises the log level through the view model and asserts that a Debug event reaches a test sink.

Manual smoke checklist:
1. `dotnet run --project SpectrumVisualizer/SpectrumVisualizer.UI` opens the main window titled from the resx text.
2. The window shows, top to bottom: menu, empty content area with its placeholder text, message panel (hidden until a
   message), status bar.
3. On a Russian Windows the text is Russian; with an English UI culture it is English.
4. The log file appears under `%LOCALAPPDATA%\SpectrumVisualizer\Logs`.
5. Switching on verbose logging in the menu makes Debug lines appear in the log file; switching it off stops them.
6. The `dev:desktop-ui` pre-handover checklist passes for the main window (Task 11).

## Progress Tracking
- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update plan if implementation deviates from original scope

## Solution Overview
- **Layout.** `SpectrumVisualizer.slnx` at the repo root lists only the new projects. The new projects live under
  `SpectrumVisualizer/` (`SpectrumVisualizer.Core`, `SpectrumVisualizer.UI`, `SpectrumVisualizer.Tests.Unit`), with
  `Directory.Build.props`, `Directory.Packages.props` and the canonical C# `.editorconfig` (marked `root = true`) inside
  that folder. MSBuild and editorconfig lookups from the old projects at the root never reach these files, so the old app
  keeps its own settings.
- **Startup.** `Program.Main` creates the Serilog logger through `AppLogging`, which returns a `LogLevelControl`
  wrapping a `LoggingLevelSwitch` at Information, writes to
  `%LOCALAPPDATA%\SpectrumVisualizer\Logs\spectrum-visualizer.log`, builds the service provider through
  `AddUi(LogLevelControl)`, and runs the Avalonia app. There are no launch keys and no config files: every site runs the same setup. A start failure is
  logged and ends with exit code 1. When the failure is the log folder itself nothing is shown; this is an accepted
  gap, SemiPlot's startup failure window is not taken.
- **App.** `App.axaml` is an include manifest (Palette, Icons, Semi theme, Forms and Buttons styles copied from
  SemiPlot). `BuildAvaloniaApp` follows SemiPlot: `UseWin32`, `UseSkia`, `UseHarfBuzz`, `UseReactiveUI` with the
  process-lifetime `UnhandledErrorObserver` as exception handler, `LogToTrace`.
- **Errors.** The message panel, entries, severities, `ResultReporting` and `UnhandledErrorObserver` come from SemiPlot.
  SemiPlot's `ArchiveFailureView` is renamed `MessageView`; its archive and configuration mappers are not taken. One
  `ErrorMessageMapper` turns an `IError` into a `MessageView` and keeps the error's own message in `Detail`, so distinct
  failures stay distinct rows when the panel coalesces repeats. Later slices add their typed errors there.
- **Language.** UI text lives in `Localization/Resources.resx` (English, neutral) and `Resources.ru.resx`, read through
  the generated accessor. The UI culture follows the Windows UI culture.
- **No instance launcher.** SemiPlot starts extra windows as extra processes; this app has one window and no such menu.
  A second running copy is allowed; later slices handle the port being taken.
- **Log level switch.** `LogLevelControl` (UI project) wraps the `LoggingLevelSwitch` and exposes `IsVerbose`. It is
  created before the logger, passed into `AddUi(LogLevelControl)` and registered as a singleton; no static holds it. A
  checkable menu item "verbose logging" raises the switch to Debug and lowers it back to Information. It is not
  persisted.
- **Code style.** The canonical `.editorconfig` enables the whole Style category as warnings. SemiPlot's three opt-outs
  (`IDE0045` and `IDE0046` silent, `CA1001` none) are carried into `SpectrumVisualizer/.editorconfig`, because the code
  taken from SemiPlot is written against them.
- **Line endings.** `.gitattributes` pins LF for the new tree only (`SpectrumVisualizer/**`, `*.slnx`, build files),
  matching the new `.editorconfig`; the old projects keep `text=auto`, so no old file is renormalised.
- **Two solution files at the root.** Every `dotnet build|test|format` command in the hook, CI, `CLAUDE.md` and this
  plan names the solution file.

## Technical Details
- Package pins reused from SemiPlot (`SemiPlot/Directory.Packages.props`): Avalonia, Avalonia.Desktop, Avalonia.Win32,
  Avalonia.Skia, Avalonia.HarfBuzz, Avalonia.Headless, Avalonia.Headless.XUnit 12.0.5; Semi.Avalonia 12.0.3;
  ReactiveUI.Avalonia 12.0.3; System.Reactive and Microsoft.Reactive.Testing 6.1.0; FluentResults 4.0.0;
  Microsoft.CodeAnalysis.ResxSourceGenerator 3.12.0-beta1.25218.8; Microsoft.Extensions.DependencyInjection,
  .Abstractions, Logging, Logging.Abstractions 10.0.8; Microsoft.Extensions.Diagnostics.Testing 10.6.0; Serilog 4.3.1;
  Serilog.Extensions.Logging 10.0.0; Serilog.Sinks.File 7.0.0; Microsoft.NET.Test.Sdk 18.6.0; xunit.v3 3.2.2;
  xunit.runner.visualstudio 3.1.5; AwesomeAssertions 9.4.0. Packages for later slices (ScottPlot, SQLite, serial) are
  not pinned yet.
- Log path: `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` + `SpectrumVisualizer\Logs`; the folder
  is created at start. File sink: 5 MB rolling, 5 files, shared, invariant culture, ProcessId enricher, as in SemiPlot.
- Main window rows: `Auto` menu, `*` content placeholder, `Auto` message panel, `Auto` status bar. The status bar shows
  only a fixed "not connected" text in this slice; the serial-link slice binds it.
- Menu: File → Exit; View → Verbose logging (checkable, `IsChecked` one-way, written only through its command).

## What Goes Where
- **Implementation Steps**: build files, projects, code, tests and docs in this repo.
- **Post-Completion**: manual smoke run and the later slices.

## Implementation Steps

### Task 1: Repository build files
**Files:**
- Create: `global.json`, `nuget.config`, `.config/dotnet-tools.json`
- Create: `SpectrumVisualizer/Directory.Build.props`, `SpectrumVisualizer/Directory.Packages.props`,
  `SpectrumVisualizer/.editorconfig`
- Modify: `.gitattributes`, `.gitignore`

- [x] add `global.json` and `nuget.config` matching SemiPlot (`global.json:1-6`, `nuget.config:1-7`)
- [x] add `SpectrumVisualizer/Directory.Build.props` from SemiPlot's (`Directory.Build.props:3-39`) and the canonical
  skill asset, with `ArtifactsPath` inside `SpectrumVisualizer/`
- [x] add `SpectrumVisualizer/Directory.Packages.props` with the pins listed in Technical Details
- [x] add the canonical C# `.editorconfig` under `SpectrumVisualizer/` with `root = true` plus SemiPlot's opt-outs
  `dotnet_diagnostic.IDE0045.severity = silent`, `IDE0046 = silent`, `CA1001 = none`; leave the root `.editorconfig`
- [x] add LF rules to `.gitattributes` for `SpectrumVisualizer/**`, `*.slnx`, `global.json`, `nuget.config`,
  `.config/**`, `.githooks/**`; add `SpectrumVisualizer/Artifacts/` to `.gitignore`
- [x] add `.config/dotnet-tools.json` pinning terse 1.1.0
- [x] verify `dotnet build SpectrumVisualizer.sln` still succeeds and `dotnet test SpectrumVisualizer.Tests` passes 13

### Task 2: Solution, Core project and unit test project
**Files:**
- Create: `SpectrumVisualizer.slnx`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Core/SpectrumVisualizer.Core.csproj`, `AppDataPaths.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/SpectrumVisualizer.Tests.Unit.csproj`,
  `xunit.runner.json`, `Core/AppDataPathsTests.cs`

- [x] create the Core library with FluentResults; add `AppDataPaths` resolving the app folder and the log folder under a
  given local app data root
- [x] create the test project after `SemiPlot.Tests.Unit.csproj:3-24` (`OutputType Exe`, `IsTestProject`, the packages
  that apply, no Npgsql) with `xunit.runner.json` (`failSkips: true`); `InternalsVisibleTo` from Core
- [x] create `SpectrumVisualizer.slnx` listing both projects
- [x] write tests for `AppDataPaths` (folder names, root handling)
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 3: UI project, logging and the log level control
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/SpectrumVisualizer.UI.csproj`, `Program.cs` (stub)
- Create: `SpectrumVisualizer.UI/Logging/AppLogging.cs`, `Logging/LogLevelControl.cs`, `UiServiceCollectionExtensions.cs`
- Create: `SpectrumVisualizer.Tests.Unit/UI/Logging/AppLoggingTests.cs`, `LogLevelControlTests.cs`
- Modify: `SpectrumVisualizer.slnx`

- [x] create the WinExe UI project modelled on `SemiPlot.UI.csproj:3-47` without ScottPlot, ColorPicker, DataGrid or
  YamlDotNet; reference Core; add it to the slnx; a stub `Program.Main` returning 0 keeps it compiling until Task 4
- [x] implement `LogLevelControl` wrapping a `LoggingLevelSwitch` (Information default) with `IsVerbose` get/set
- [x] implement `AppLogging.Create(logFolder)` returning the logger and the `LogLevelControl`, with the file sink
  settings of `Program.cs:97-122`; it creates a missing log folder
- [x] implement `AddUi(LogLevelControl)` registering the control as a singleton
- [x] write tests: Debug filtered at Information and passed at Debug through an in-memory sink; missing folder created;
  `AddUi` resolves the same `LogLevelControl` instance
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 4: App shell, theme, text and entry point
**Files:**
- Create: `SpectrumVisualizer.UI/App.axaml`, `App.axaml.cs`
- Modify: `SpectrumVisualizer.UI/Program.cs`
- Create: `SpectrumVisualizer.UI/MainWindow/MainWindow.axaml(.cs)` (empty window, filled in Task 6)
- Create: `SpectrumVisualizer.UI/Styles/Palette.axaml`, `Icons.axaml`, `Forms.axaml`, `Buttons.axaml`
- Create: `SpectrumVisualizer.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Create: `SpectrumVisualizer.Tests.Unit/TestAppBuilder.cs`, `UI/AppResourcesTests.cs`

- [x] copy the four style files from SemiPlot and the include manifest of `App.axaml:1-21` without the ColorPicker and
  DataGrid themes
- [x] implement `BuildAvaloniaApp` as in `App.axaml.cs:271-281` with plain `UseReactiveUI()` (the exception handler
  arrives in Task 5); `App` shows the empty `MainWindow`
- [x] implement `Program.Main`: logging through `AppLogging`, provider through `AddUi`, run the app, fatal log and exit 1
  on a start failure
- [x] add the resx pair with generated accessor (window title, menu items, placeholder, status text, message panel text)
- [x] add the headless `TestAppBuilder` as in `TestAppBuilder.cs:10-21`
- [x] write tests that every key in `Resources.resx` exists in `Resources.ru.resx` and is non-empty, and a headless test
  that the palette keys used by `Forms.axaml` and `Buttons.axaml` resolve (not the unused DataGrid or ColorPicker keys)
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 5: Message panel and error route
**Files:**
- Create: `SpectrumVisualizer.UI/Messages/MessagePanelViewModel.cs`, `MessagePanelView.axaml(.cs)`, `MessageEntry.cs`,
  `MessageSeverity.cs`, `MessageView.cs`, `SeverityMatchConverter.cs`, `ResultReporting.cs`, `UnhandledErrorObserver.cs`,
  `ErrorMessageMapper.cs`
- Modify: `SpectrumVisualizer.UI/App.axaml.cs`
- Create: `SpectrumVisualizer.Tests.Unit/UI/Messages/MessagePanelViewModelTests.cs`, `ResultReportingTests.cs`,
  `UnhandledErrorObserverTests.cs`, `ErrorMessageMapperTests.cs`

- [x] copy the message panel types from SemiPlot `Messages/`, renaming namespaces, `x:Class` and `clr-namespace` values,
  and `ArchiveFailureView` to `MessageView`
- [x] replace the `ArchiveFailureMapper.Map` call in `ResultReporting` (`ResultReporting.cs:43`) with `ErrorMessageMapper`;
  the mapper keeps the error message in `Detail`
- [x] install the process-lifetime `UnhandledErrorObserver` through `WithExceptionHandler` in `BuildAvaloniaApp`
- [x] write tests for the panel bound (200 entries, newest first), coalescing of identical entries and separate rows for
  different exception messages, and opening on a new entry
- [x] write tests that `ReportFailure` logs and shows, and that `UnhandledErrorObserver` logs only when no panel exists
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 6: Main window with menu, placeholder and status bar
**Files:**
- Modify: `SpectrumVisualizer.UI/MainWindow/MainWindow.axaml(.cs)`
- Create: `SpectrumVisualizer.UI/MainWindow/MainWindowViewModel.cs`, `AppMenuBar.axaml(.cs)`, `AppStatusBar.axaml(.cs)`,
  `AppStatusBarViewModel.cs`
- Create: `SpectrumVisualizer.Tests.Unit/UI/MainWindow/MainWindowViewModelTests.cs`, `MainWindowViewTests.cs`

- [x] load the `dev:desktop-ui` skill before laying out the window; follow its spacing, palette and text rules
- [x] build the window rows `Auto,*,Auto,Auto` (menu, content placeholder, message panel, status bar), sizes from the
  skill's window rules
- [x] add the menu: File → Exit; View → Verbose logging bound one-way to `LogLevelControl.IsVerbose`, written only
  through a command
- [x] add the status bar showing the fixed "not connected" text from the resx
- [x] write view model tests for the verbose-logging command (switch level changes, checked state follows)
- [x] write a headless test that the realised window shows the menu, the placeholder, a hidden message panel and the
  status bar
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 7: Composition root
**Files:**
- Modify: `SpectrumVisualizer.UI/UiServiceCollectionExtensions.cs`, `App.axaml.cs`
- Create: `SpectrumVisualizer.Tests.Unit/UI/Di/CompositionRootTests.cs`, `AppBuilderCompositionTests.cs`

- [x] register the message panel, the time provider and the main window view model in `AddUi(LogLevelControl)`; build
  the main window from the provider in `App`
- [x] write a test that every registered service resolves from the production composition root
- [x] write `AppBuilderCompositionTests` in SemiPlot's shape: `BuildAvaloniaApp` reports `WindowingSubsystemName ==
  "Win32"`, `RenderingSubsystemName == "Skia"`, a non-null text shaping initializer, and restores `Logger.Sink`
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 8: Remove the vendor-SDK project and the COM writer tool
**Files:**
- Delete: `SprctrumVisualizer/`, `ComPortPopulator/`
- Modify: `SpectrumVisualizer.sln`

- [x] remove both projects with `dotnet sln SpectrumVisualizer.sln remove` and delete the folders with `git rm -r`
- [x] verify no file still references `SprctrumVisualizer` or `ComPortPopulator` (readme excepted, fixed in Task 12)
- [x] verify `dotnet build SpectrumVisualizer.sln` succeeds and `dotnet test SpectrumVisualizer.Tests` passes 13

### Task 9: Pre-commit hook and CI
**Files:**
- Create: `.githooks/pre-commit`, `.github/workflows/ci.yml`

- [x] add the pre-commit hook from SemiPlot (`.githooks/pre-commit:1-35`) pointing at `SpectrumVisualizer.slnx`
- [x] add the CI workflow modelled on `ci.yml:1-60`: setup-dotnet with `global-json-file` and `dotnet-version: 9.0.x`;
  restore, build and test `SpectrumVisualizer.slnx`; `dotnet format SpectrumVisualizer.slnx --verify-no-changes` and
  terse; then build `SpectrumVisualizer.sln` and run its tests. Path filters cover the new tree, the old projects,
  `SpectrumVisualizer.sln` and the build files
- [x] run the hook locally on a staged file to confirm it passes and rejects a badly formatted file

### Task 10: Project docs
**Files:**
- Create: `CLAUDE.md`
- Create: `docs/architecture/README.md`, `overview.md`, `testing-strategy.md`, `ui-theme.md`, `ui-text.md`

- [x] write `CLAUDE.md` per the `dev:project-layout` canon: identity, build, test, format, layout, docs pointers; every
  command names its solution file
- [x] write `docs/architecture/README.md` with the index and the Locked decisions table (stack, layout, logging, text,
  tests, serial library and protocol as decided in the roadmap)
- [x] write `overview.md` (purpose, stack, components, data flow), `testing-strategy.md`, `ui-theme.md`, `ui-text.md`
  in present tense, adapted from SemiPlot's documents where they apply

### Task 11: Verify acceptance criteria
- [x] run every command of Acceptance Evidence and record the results: (1) Release build 0 warnings, 0 errors;
  (2) unit tests 61 of 61 pass, including `MainWindowViewTests` and `CompositionRootTests`; (3) `dotnet format
  --verify-no-changes` exit 0, `dotnet terse SpectrumVisualizer` exit 0; (4) old solution builds (15 pre-existing
  nullable warnings), 13 of 13 pass, `SpectrumVisualizer.sln` lists neither removed project; (5)
  `MainWindowViewModelTests.ADebugEventReachesTheSinkOnlyWhileVerbose` passes
- [x] walk through the manual smoke checklist: `timeout 25 dotnet run --project SpectrumVisualizer/SpectrumVisualizer.UI`
  kept the process alive until the timeout (exit 124, no start failure) and appended a "starting" line to
  `%LOCALAPPDATA%\SpectrumVisualizer\Logs\spectrum-visualizer.log` (item 4); rows and verbose logging are covered by
  the headless tests; window title, visual row order, Russian/English text and Debug lines in the real log file
  (items 1, 2, 3, 5) deferred to owner's final check
- [x] run the `dev:desktop-ui` pre-handover checklist for the main window and fix every miss: spacing (fixed the
  message panel's empty text at 4 px under the header, now 8 like the entries); field widths (no fields); minimum
  width (no fixed panes or columns yet, no `MinWidth` set, deferred to the spectrum-view slice); large width (fixed:
  Clear sat at the far right of the header and the repeat count and time at the far right of each entry; Clear now
  follows the title with 8 px, the time leads the entry, the repeat count sits under the title); labels on one line
  (menu, Clear and status texts short in both resx files); states keep the window size (the message panel row
  appears inside the fixed window and is capped at 140 px)
- [x] confirm the old solution builds and passes 13 tests: build exit 0, 13 of 13 pass

### Task 12: [Final] Update documentation
- [ ] update `readme.md`: remove the old-project section, add the new solution's build and run commands
- [ ] update `CLAUDE.md` if new patterns appeared during implementation
- [ ] move this plan to `docs/plans/completed/`

## Post-Completion
**Manual verification:**
- the smoke checklist above, run by the owner during the final assembly check.

**Remaining slices:** frame-decoder, serial-link, spectrum-view, spectrum-store, intensity-trend, firmware-frame-sender,
release-3-0.
