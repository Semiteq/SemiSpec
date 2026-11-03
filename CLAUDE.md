# SpectrumVisualizer

SpectrumVisualizer is the operator's desktop viewer for the spectrometer board: it reads the board's frames over a
serial port and shows the spectrum. The application under `SpectrumVisualizer/` is a .NET 10 (C# 14) Windows desktop
app on Avalonia 12, ReactiveUI and Semi.Avalonia; today it is the shell only (window, logging, error route), and its
content area shows a placeholder. The repo root also holds the old WinForms app (`SpectrumVisualizer.Uart`) and its
MSTest project (`SpectrumVisualizer.Tests`) on net9.0-windows.

Two solution files sit at the root, so every `dotnet` command names one:

- `SpectrumVisualizer.slnx` lists the new projects only. Their build gates (`SpectrumVisualizer/Directory.Build.props`,
  `SpectrumVisualizer/Directory.Packages.props`, `SpectrumVisualizer/.editorconfig` with `root = true`) sit inside
  `SpectrumVisualizer/` and never reach the old projects.
- `SpectrumVisualizer.sln` lists the old app and its tests, built with the root `.editorconfig` and no shared props.

All commands run from the repo root.

## Build

```powershell
dotnet build SpectrumVisualizer.slnx -c Release       # new app; warnings are errors
dotnet run --project SpectrumVisualizer/SpectrumVisualizer.UI
dotnet build SpectrumVisualizer.sln                   # old app
```

`global.json` pins SDK 10.0.100 (`latestFeature`); `nuget.config` clears inherited sources and keeps nuget.org only.
Package versions are pinned centrally in `SpectrumVisualizer/Directory.Packages.props`. Build output goes to
`SpectrumVisualizer/Artifacts/` (gitignored).

## Test

```powershell
dotnet test SpectrumVisualizer.slnx
dotnet test SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/SpectrumVisualizer.Tests.Unit.csproj -c Release
dotnet test SpectrumVisualizer.slnx --filter "Area=Messages"
dotnet test SpectrumVisualizer.Tests                  # old app
```

`SpectrumVisualizer/SpectrumVisualizer.Tests.Unit` is the only test project of the new app: xUnit, AwesomeAssertions,
Avalonia.Headless. Every test class carries three traits: `Component` (`Core|UI`), `Area`
(`Di|Logging|MainWindow|Messages|Paths|Resources`) and `Category` (`Unit`). `xunit.runner.json` sets `failSkips`, so a
skipped test fails the run. A hung run leaves `SpectrumVisualizer.Tests.Unit.exe` locked, and the next build fails with
MSB3027 until the process is killed.

## Format

Run before presenting changes:

```powershell
dotnet format SpectrumVisualizer.slnx --verify-no-changes
dotnet tool restore
dotnet terse <file.cs>
```

`terse` (pinned in `.config/dotnet-tools.json`) is the comment gate. The pre-commit hook `.githooks/pre-commit` runs both
gates over staged `SpectrumVisualizer/*.cs` files and refuses commits on `master` or `main`; wire it once per clone with
`git config core.hooksPath .githooks`. `.github/workflows/ci.yml` runs build, tests, format and terse for the new app,
then builds and tests the old app.

## Layout

```
.config/                         dotnet local tools (terse)
.githooks/                       pre-commit hook
.github/workflows/               CI
SpectrumVisualizer/              new app: build props, .editorconfig, projects below
  SpectrumVisualizer.Core/       UI-free code (paths)
  SpectrumVisualizer.UI/         Avalonia WinExe: startup, logging, main window, messages, styles, resx text
  SpectrumVisualizer.Tests.Unit/ unit and headless UI tests
SpectrumVisualizer.Uart/         old WinForms app
SpectrumVisualizer.Tests/        old app's MSTest tests
docs/                            architecture docs and plans
```

## Docs

- `docs/architecture/`: how the new app is built and the locked decisions; enter at `docs/architecture/README.md`.
- `docs/plans/`: dated implementation plans; finished ones move to its `completed/` subfolder.
