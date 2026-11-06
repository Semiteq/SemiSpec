# SemiSpec

SemiSpec is the operator's desktop viewer for the spectrometer board: it reads the board's frames over a
serial port and shows the spectrum. The application under `SemiSpec/` is a .NET 10 (C# 14) Windows desktop
app on Avalonia 12, ReactiveUI and Semi.Avalonia.

`SemiSpec.slnx` at the root lists every project. Their build gates
(`SemiSpec/Directory.Build.props`, `SemiSpec/Directory.Packages.props`,
`SemiSpec/.editorconfig` with `root = true`) sit inside `SemiSpec/`. All commands run from the repo
root.

## Build

```powershell
dotnet build SemiSpec.slnx -c Release       # warnings are errors
dotnet run --project SemiSpec/SemiSpec.UI
dotnet run --project SemiSpec/SemiSpec.Tools.BoardEmulator -- --help
dotnet run -c Release --project SemiSpec/SemiSpec.Tools.StoreBenchmark -- --help
```

`global.json` pins SDK 10.0.100 (`latestFeature`); `nuget.config` clears inherited sources and keeps nuget.org only.
Package versions are pinned centrally in `SemiSpec/Directory.Packages.props`. Build output goes to
`SemiSpec/Artifacts/` (gitignored).

## Test

```powershell
dotnet test SemiSpec.slnx
dotnet test SemiSpec/SemiSpec.Tests.Unit/SemiSpec.Tests.Unit.csproj -c Release
dotnet test SemiSpec.slnx --filter "Area=Messages"
```

`SemiSpec/SemiSpec.Tests.Unit` is the only test project: xUnit, AwesomeAssertions,
Avalonia.Headless. Every test class carries three traits: `Component` (`Core|UI|Tools`), `Area`
(`Calibration|Chart|Di|Emulator|Link|Logging|MainWindow|Messages|Paths|Protocol|Resources|Spectrum|Store|Trend`) and
`Category` (`Unit`).
`xunit.runner.json` sets `failSkips`, so a skipped test fails the run. A hung run leaves
`SemiSpec.Tests.Unit.exe` locked, and the next build fails with MSB3027 until the process is killed.

## Format

Run before presenting changes:

```powershell
dotnet format SemiSpec.slnx --verify-no-changes
dotnet tool restore
dotnet terse <file.cs>
```

`terse` (pinned in `.config/dotnet-tools.json`) is the comment gate. The pre-commit hook `.githooks/pre-commit` runs
both gates over staged `SemiSpec/*.cs` files and refuses commits on `master` or `main`; install it once per
clone with `cp .githooks/pre-commit .git/hooks/pre-commit`, which also works under a global `core.hooksPath` that
chains `.git/hooks`. `.github/workflows/ci.yml` runs build, tests, format and terse.

## Release

```powershell
dotnet publish SemiSpec/SemiSpec.UI/SemiSpec.UI.csproj -c Release
iscc.exe /DAppVersion=X.Y.Z Installer\SemiSpec.iss
git tag -a vX.Y.Z --cleanup=verbatim -F notes.txt
```

The version is the `Version` element in `SemiSpec/Directory.Build.props`. To release, bump it, merge to
`master`, then push the annotated tag; `docs/architecture/overview.md`, Release, describes the installer script and
`release.yml`.

## Layout

```
.config/                         dotnet local tools (terse)
.githooks/                       pre-commit hook
.github/workflows/               CI and release
Installer/                       Inno Setup script; Output/ holds the compiled installer (gitignored)
SemiSpec/                        build props, .editorconfig, projects below
  SemiSpec.Core/                 UI-free code: paths, board protocol, wavelength calibration,
                                 spectrum projection and scale, log axis ticks, spectrum store (Store/),
                                 trend rules: continuity, intensity cache, history plan, navigation (Trend/)
  SemiSpec.UI/                   Avalonia WinExe: startup, logging, serial link, main window, spectrum chart,
                                 store writer service (Store/), trend chart, overview and feed (Trend/),
                                 messages, styles, resx text
  SemiSpec.Tools.BoardEmulator/  console tool: writes board frames to a COM port
  SemiSpec.Tools.StoreBenchmark/ console tool: fills a store file and measures its writes, size and reads
  SemiSpec.Tests.Unit/           unit and headless UI tests
  Shared/                        source linked into both tools (<Compile Include Link>): shared option parsing
docs/                            architecture docs and plans
```

## Docs

- `readme.md`: the operators' guide, in Russian; the operators read Russian, so it is the one document not in English.
- `docs/architecture/`: how the app is built and the locked decisions; enter at `docs/architecture/README.md`.
- `docs/plans/`: dated implementation plans; finished ones move to its `completed/` subfolder.
