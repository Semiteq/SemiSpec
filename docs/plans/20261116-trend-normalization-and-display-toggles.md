# Trend normalization, display toggles and .NET 11

## Behavior

- The readout pane hides the Q factor row by default. A View menu check item "Q factor" ("Добротность") shows
  it again. The board still sends Q, and the decoder and the store keep it.
- The trend draws dots by default: one 3 px square at each pixel column's minimum and one at its maximum, and no
  connecting line. A View menu check item "Trend as lines" ("Тренд линиями") brings back today's line.
- The two View menu choices persist in `C:\DISTR\Config\SemiSpec\settings.yaml` across restarts.
- A normalization switch, the label "Нормировка" and three joined segments "Выкл", "×100" and "×1000", sits at the
  right end of the trend's top margin, opposite the state line.
- A press fixes the reference: the pixels inside the current window (wavelength ± delta) of the newest live
  spectrum. From then on every trend point is the mean of `pixel / reference pixel` over the window, times 100 or
  1000: each pixel is divided by its own reference pixel first, and the ratios are averaged after. Points before
  the press are read again from the store and normalized the same way.
- "Выкл" turns normalization off and stays enabled while the trend is normalized. A press on the other factor
  segment fixes a new reference with its own factor; a press on the checked one changes nothing.
- A change of wavelength or delta from the board turns normalization off, clears a held Y range and adds an Info line
  to the message panel.
- While normalization is on, the state line shows "Normalized ×100" or "Normalized ×1000", the Y label's first line
  reads the same, and the hover readout and the overview strip show normalized values.
- The app builds and runs on .NET 11 (SDK 11.0.100, `net11.0`), C# 14 unchanged.
- Scope guard: the spectrum chart, its scale and the readout values do not change. The store schema and the stored
  data do not change. Normalization is not saved across a restart; `readme.md` says so. The reference is a single
  frame: a steady sample at the press is the operator's job.

## Flow branches

| Situation | The app does | Decided by | Checked by |
|---|---|---|---|
| No live spectrum at the current key, or the live spectrum is stale (`SpectrumStaleness`: link not Receiving or over 10 s old) | The factor segments are disabled, with a tooltip that says why; Off stays enabled while normalized | agent: the reference must be the current live value; designer review: a stale link must not lock normalization on | headless test |
| The device does not cover the wavelength (every point NaN) | Buttons disabled, as above | agent: `RecentSpectra.IntensityOf` gives NaN | headless test |
| Some reference pixels are 0 | Those pixels are left out of every later mean | agent: division by 0 | unit test |
| Every reference pixel is 0 (a dark frame) | No normalization, the button stays released, a Warning in the message panel names the reason | agent | headless test |
| A point from a device other than the reference's device, live or stored | NaN: drawn as no data | agent: the windows differ per device (`QueryWindows.For`); the ranges overlap at 898-1028 nm (`SpectrometerCalibration`) | unit test |
| Press while a past moment is shown | The reference is still the newest live spectrum, not the shown moment | agent: the reference is the current live value | headless test |
| "Выкл" pressed | Normalization off; the trend reads the raw history again | operator | headless test |
| The checked factor segment pressed again | Nothing changes | operator: the segmented switch | headless test |
| Press on the other button while one is active | New reference, new factor, history read again | operator: approved on the demo | headless test |
| Board changes wavelength or delta | Normalization off, "Выкл" checked, a held Y range cleared, an Info line in the panel | operator; the range clear is agent: raw values fall outside a normalized range | headless test |
| Normalization toggled while a Y range is held | The held range is cleared, as a From zero toggle does today | agent: `TrendChartViewModel.SetFromZero` clears `ManualVerticalRange` | headless test |
| History read fails during a normalized read | One `TrendHistoryError` as today; the trend keeps the last 120 s recomputed from the recent spectra and the points after them | agent: existing key change path | existing tests |
| `settings.yaml` missing | Defaults (Q hidden, dots); the file appears at the first toggle | agent | unit test |
| `settings.yaml` unreadable or invalid | Defaults, one Warning in the panel and the log; the next toggle rewrites the file | agent | unit test |
| `settings.yaml` holds unknown keys | They are ignored on read and dropped at the next write | agent: two values, no newer writer exists | unit test |
| `C:\DISTR\Config\SemiSpec` not writable (a run without the installer) | Settings go to `%LOCALAPPDATA%\SemiSpec\Config`; the log gets a Warning naming both folders | agent: the log fallback pattern (`AppLogging.Create`) | unit test |
| Settings write fails | The toggle still applies for this run; one Warning in the panel | agent | unit test |

## Decisions

- History before the press is normalized from the store's raw pixels: a trend that mixes raw and normalized points
  on one axis reads wrong, and an empty trend before the press loses context. Cost is one more re-read, the same
  path a wavelength change takes. Rejected: hiding the points before the press.
- The reference is the newest live spectrum at the press, one frame. Rejected: an average over several seconds;
  calibration on a steady sample is the operator's procedure.
- The reference carries its device. The board measures with one device per wavelength (below 904 nm the ATP-2000,
  from 904 nm the ATP-8000, `overview.md`, Continuity), so all live points at one key come from that device. A point
  from the other device gets NaN as a guard for stored rows of other firmware or a device switch.
- Settings live in `C:\DISTR\Config\SemiSpec\settings.yaml`, following SemiPlot (`C:\DISTR\Config\SemiPlot`,
  `Installer/SemiPlot.iss`) and SemiStep (`C:\DISTR\Config\SemiStep`). YAML through YamlDotNet 18.1.0: the format and
  the library SemiPlot's config uses, so one service engineer edits all Semi configs one way. Rejected: JSON through
  `System.Text.Json`, a second format on the site; `%LOCALAPPDATA%` as the primary place, because it makes the choice
  per user, not per PC. The `%LOCALAPPDATA%\SemiSpec\Config` fallback exists only for a run without the installer.
- This revises the locked Startup decision in `docs/architecture/README.md`, "No launch keys and no config files":
  there are still no launch keys, and one settings file holds the two display choices. Every site still runs the same
  setup by default.
- `spectra.db` stays in `%LOCALAPPDATA%\SemiSpec\Spectra`: it is data, not configuration.
- Dots mode draws the column minimum and maximum, not a mean: with an oscillating sample a column holds both low and
  high values, and a mean would draw a false middle band. At the 10 min window and 2 spectra per second a column holds
  about one spectrum. The look at wider windows goes through the Gate before PR 1.
- The overview strip stays a band in both modes.
- In dots mode a lost spectrum shows only as a missing dot; the hover readout still names the gap.
- The Q factor row collapses when hidden. This is a configuration, not a state; inside one run the pane changes
  height only on the operator's toggle.
- The normalized mean has one home in Core, next to `QueryWindows.MeanOf`, and both the cache and the store read use
  it.
- The reference, its factor and the reset on a key change live in `IntensityCache` under its lock: the key changes on
  the distributor task and the press comes from the UI thread. The cache is the one writer; the buttons mirror its
  state, so a refused press leaves them released.
- `LangVersion` stays 14: nothing in this work needs C# 15.
- Delivery: three PRs, each with its own acceptance and docs pass. PR 1: settings, the two View toggles and the dots
  (Tasks 1 to 4). PR 2: normalization (Tasks 5 to 8). PR 3: .NET 11 (Tasks 9 and 10). The client's items do not wait
  on the upgrade.

## Caveats

- .NET 11 is released and installed here.
  probed: `dotnet --list-sdks` lists 11.0.100; NuGet lists `Microsoft.NETCore.App.Ref` 11.0.0, `Microsoft.Data.Sqlite`
  11.0.0, `Microsoft.Extensions.Logging` 11.0.0, `Microsoft.Extensions.Diagnostics.Testing` and
  `Microsoft.Extensions.TimeProvider.Testing` 11.0.0; 2026-11-16.
- ASSUMPTION: `Microsoft.Extensions.DependencyInjection` has an 11.0.0 release; fallback: keep 10.0.8; checked by:
  Task 9 restore.
- The `terse` local tool targets `net10.0` with `rollForward: false` (`.config/dotnet-tools.json`).
  probed: `~/.nuget/packages/terse/1.1.0/tools/` holds only `net10.0`; 2026-11-16. Only `ci.yml` runs `terse`;
  `release.yml` does not.
  ASSUMPTION: the CI runner has no .NET 10 runtime once `setup-dotnet` reads an 11.0.100 `global.json`; fallback: add
  the 10.0 runtime to the `ci.yml` setup step, or set `rollForward: true` for the tool; checked by: the green CI run on
  the PR 3 branch. A local run proves nothing here, because this machine has the .NET 10 SDKs.
- The solution builds and tests on `net11.0` with every package pin unchanged, except one new analyzer error.
  probed: a `git archive HEAD` copy with SDK 11.0.100 and `net11.0`, `dotnet build -c Release` then
  `dotnet test`; every app project compiles; CA2027 (`Task.WhenAny` with `Task.Delay`) fails
  `SpectrumChartViewModelTests.cs` line 210; with CA2027 as a warning, 1068 of 1068 tests pass; 2026-11-16.
  Not probed: the self-contained publish and a real start; checked by: Task 10.
- ASSUMPTION: Serilog.Extensions.Logging 10.0.0 works over Microsoft.Extensions.Logging 11.0.0 (11.x exists only as a
  dev build); fallback: keep Microsoft.Extensions.Logging at 10.0.x; checked by: Task 9 build and the log tests.
- The locked Conventions row in `docs/architecture/README.md` makes SemiPlot the reference app for package pins and
  build gates. SemiPlot is on `net10.0`, SDK 10.0.100. PR 3 removes that row: SemiSpec keeps its own pins.
- ASSUMPTION: a Semi.Avalonia toggle button fits the 24 px top margin (`ChartStateLine.TopMargin`) with Semi's small
  size class; fallback: stop and ask the operator, because a taller margin raises the trend row floor (208 px) and
  `MainWindow.MinimumHeight` (688 px, set by the 1366 x 768 maximised client height); checked by: Task 7 headless
  measure, before any other Task 7 work.
- The trend's state line uses at most 429.7 px in Russian over a 1190.2 px data area at the minimum width
  (`overview.md`, The state line). Two buttons of about 130 px each fit beside it. Task 7 measures the real labels.

## Constraints and non-goals

- The frame layout, the decoder and the store schema (`PRAGMA user_version`) do not change; a schema change replaces
  the history file.
- No test touches `C:\DISTR`: the settings folder check is a parameter the tests answer, as in `AppLogging.Create`.
- The spectrum chart is not normalized.
- Bad input in scope: a missing, empty, malformed or foreign `settings.yaml`. Nothing else in the file is trusted.
- No average-over-time reference, no saved normalization, no per-user settings.
- Tests stay as simple as possible; no speculative abstractions.

## UI design

The operator approved the throwaway demo (a separate Avalonia project in the session scratchpad, Semi theme, synthetic
24 h oscillating history) with two-state toggle buttons. The two Gates still show the built result before each PR.

- Trend top margin: the state line on the left, the switch on the right, its right edge at the data area's right
  edge: the grey label, a 12 px gap, a 22 px frame in Semi's radio-button-as-button look with three joined 18 px
  segments. The checked segment names its state, so colour does not carry it alone. The operator chose this over two
  toggle buttons after a designer review: Off is visible, and the checked state is quieter than a solid accent block.
- View menu, after "Verbose logging": a separator, then "Q factor" and "Trend as lines" as check items.
- The frame is 22 px and the segments 18 px: the band over the data area is 22 px.

## Gates

- Before PR 1: screenshots of the trend in dots mode on an oscillating emulator signal at the 10 min, 1 h and 24 h
  windows, the readout pane with the Q row hidden, and the View menu, go to the operator for approval.
- Before PR 2: a screenshot of the trend with the switch, at the minimum window width in Russian, goes to the
  operator for approval. A button taller than the 24 px margin stops the run at Task 7.
- Task 9: any build or test failure caused by a package on `net11.0` stops the run for an operator decision.

## Acceptance

- `dotnet build SemiSpec.slnx -c Release` → 0 warnings, 0 errors
- `dotnet test SemiSpec.slnx` → every test passes, 0 skipped
- `dotnet format SemiSpec.slnx --verify-no-changes` → exit 0
- `dotnet tool restore && git ls-files -z 'SemiSpec/*.cs' | xargs -0 dotnet terse` → exit 0
- a unit test proves the order of operations: reference `[100, 200]`, pixels `[100, 100]`, factor 100 gives 75, not
  66.7, on the cache path and on the store path
- PR 3 only: `grep -n "net11.0" SemiSpec/Directory.Build.props` → one match;
  `dotnet publish SemiSpec/SemiSpec.UI/SemiSpec.UI.csproj -c Release` → exit 0 and the published exe starts; the CI
  run on the PR 3 branch is green
- manual smoke, with the board emulator on a COM port pair:
  1. Start the app with no `settings.yaml` → Q row hidden, trend draws dots.
  2. View, "Q factor" on, restart → the Q row shows; `C:\DISTR\Config\SemiSpec\settings.yaml` holds the choice.
  3. View, "Trend as lines" on → the trend draws today's line; off → dots again.
  4. Press "×100" on a steady signal → the newest points sit near 100, the history before the press is
     normalized too, the state line reads "Normalized ×100".
  5. Press "Выкл" → raw values return.
  6. Press "×100", then "×1000" → points near 1000.
  7. Restart the emulator with another `--wavelength` → "Выкл" is checked, the panel shows the reset line.
  8. Stop the emulator and wait 10 s → "×100" and "×1000" are disabled with a tooltip, "Выкл" stays enabled.
  9. Run the installer on a clean PC → `icacls C:\DISTR\Config\SemiSpec` lists `BUILTIN\Users` with `(M)`.
- not visible to headless tests: the real button height in the margin, the dot look, the installer's folder rights.

---

## Context

- Q row: `SemiSpec.UI/Spectrum/SignalInfoViewModel.cs` (`QText`), `SpectrumPanel.axaml`, resx key with
  "Добротность".
- Trend drawing: `SemiSpec.UI/Trend/EnvelopeLine.cs`, `EnvelopePath.cs`, `EnvelopeColumn.cs`, `TrendPlotLayers.cs`.
- Trend state: `SemiSpec.Core/Trend/IntensityCache.cs` (key, generation, `ReReadRequested`), `RecentSpectra.cs`
  (`IntensityOf`, `PointsAt`), `TrendExtent.cs` (`TrendKey`), `ReReadRequest.cs`.
- Store read: `SemiSpec.Core/Store/SpectrumHistory.cs` (`IntensityPointsAsync`, `WindowMeans`, `ReadSeries`),
  `QueryWindows.cs` (`MeanOf`); callers `UiServiceCollectionExtensions.cs` (the `TrendFeed` delegate) and
  `Tools.StoreBenchmark/BenchmarkRun.cs`.
- Trend UI: `TrendViewModel.cs` (state line, From zero), `TrendChartViewModel.cs` (`ManualVerticalRange`, Y label),
  `TrendChartView.axaml`, `TrendFeed.cs` (re-read pipeline), `OverviewViewModel.cs` (refold triggers),
  `UI/Chart/ChartStateLine.cs`, `Spectrum/SpectrumStaleness.cs`.
- Menu: `MainWindow/AppMenuBar.axaml`, `MainWindowViewModel.cs`.
- Paths: `SemiSpec.Core/AppDataPaths.cs`, `UI/AppLogging.cs` (write check and fallback pattern).
- Installer: `Installer/SemiSpec.iss` `[Dirs]`; SemiPlot's `Installer/SemiPlot.iss` is the model for `ConfigRoot`.
- Framework pins: `global.json`, `SemiSpec/Directory.Build.props`, `SemiSpec/Directory.Packages.props`,
  `.config/dotnet-tools.json`, `.github/workflows/ci.yml`.
- Text fit: `MainWindowTextFit*Tests`. Test traits: a new `Area=Settings` for the settings tests.

## Boundaries

- Task 1 gives the UI a settings service with the two values and a save; Tasks 2 and 3 read and write through it.
- Task 6 gives the UI, from the cache:
  - start at a factor, which returns a failure value (FluentResults) when the window is all zero or no spectrum is at
    hand;
  - stop;
  - an observable of the normalization state: off, or on with its factor, and on each change to off the reason,
    operator or key change;
  - whether a spectrum at the current key is at hand.
  Task 7 binds the buttons, the state line, the Y label and the messages to these.

## Tasks

### Task 1: Settings file

**Goal:** two persisted choices in `settings.yaml`, under `C:\DISTR\Config\SemiSpec` or the fallback
**Entry:** `SemiSpec.Core/AppDataPaths.cs`, `UI/AppLogging.cs` for the fallback pattern
**Likely touches:** `AppDataPaths.cs`, a new settings type in UI, `UiServiceCollectionExtensions.cs`,
`Directory.Packages.props`, `Installer/SemiSpec.iss`

- [x] add YamlDotNet; read the file at start, write it on each change
- [x] folder check with fallback to `%LOCALAPPDATA%\SemiSpec\Config` and a Warning naming both folders
- [x] missing, invalid, unknown-key and unwritable cases follow the Flow branches
- [x] installer creates `C:\DISTR\Config\SemiSpec` with `users-modify` and `uninsneveruninstall`
- [x] tests (`Area=Settings`): defaults on a missing file, defaults plus Warning on a malformed one, unknown keys
  ignored, a round trip, the fallback, a failed write keeps the value for the run
- [x] run tests - must pass before the next task

### Task 2: Q factor toggle

**Goal:** the Q row is hidden by default and shown by a View menu check item that persists
**Entry:** `MainWindow/AppMenuBar.axaml`, `Spectrum/SignalInfoViewModel.cs`
**Likely touches:** `AppMenuBar.axaml`, `MainWindowViewModel.cs`, `SpectrumPanel.axaml`, both resx files

- [x] View menu check item over the setting, written only through its command
- [x] the Q row collapses while the setting is off
- [x] tests: default hidden, toggle shows the row and saves the setting
- [x] run tests - must pass before the next task

### Task 3: Trend dots

**Goal:** the trend draws min and max dots per column by default, today's line on request
**Entry:** `Trend/EnvelopeLine.cs`, `Trend/TrendPlotLayers.cs`
**Likely touches:** `EnvelopeLine.cs` or a sibling plottable, `TrendPlotLayers.cs`, `TrendChartViewModel.cs`,
`AppMenuBar.axaml`, both resx files

- [x] dots mode: a 3 px square at each visible column's minimum and maximum, one when they are equal
- [x] lines mode draws exactly what the trend draws today
- [x] View menu check item "Trend as lines" over the setting; a switch redraws at once
- [x] tests: the dot positions for a fold with equal and unequal min and max; the mode follows the setting
- [x] run tests - must pass before the next task

### Task 4: PR 1 acceptance and docs

**Goal:** PR 1 meets its Acceptance items and the docs describe it
**Entry:** the plan's `## Acceptance` and `## Gates`

- [x] the build, test, format and terse commands pass; smoke steps 1 to 3 and 9 hold
- [x] the Gate before PR 1: screenshots to the operator, approval recorded in `## UI design`
- [x] `overview.md`: stack (YamlDotNet), components, readout pane, trend chart, settings, installer `[Dirs]`
- [x] `docs/architecture/README.md`: rewrite the Startup row of the locked decisions
- [x] `CLAUDE.md`: the `Settings` test area
- [x] `readme.md` (Russian): the two View items; `readme.md:25` no longer lists "добротность" among the shown
  values by default; the operator approves the wording before the commit

### Task 5: Normalized mean and the store read

**Goal:** Core computes a normalized mean, and the store's points read returns normalized points for a reference
**Entry:** `Core/Store/QueryWindows.cs` (`MeanOf`), `Core/Store/SpectrumHistory.cs` (`WindowMeans`)
**Likely touches:** `QueryWindows.cs`, `SpectrumHistory.cs`, `UiServiceCollectionExtensions.cs`,
`Tools.StoreBenchmark/BenchmarkRun.cs`

- [x] the normalized mean beside `MeanOf`: each pixel over its reference pixel, then the mean; zero reference
  pixels left out, NaN when none is left
- [x] the points read takes an optional reference with its device; a row of another device is NaN
- [x] tests: the 75-not-66.7 case, zero pixels, a row of the other device, a raw read unchanged
- [x] run tests - must pass before the next task

### Task 6: Normalization in the cache

**Goal:** the cache starts, stops and resets normalization, and the trend's points and re-reads follow it
**Entry:** `Core/Trend/IntensityCache.cs`
**Likely touches:** `IntensityCache.cs`, `RecentSpectra.cs`, `ReReadRequest.cs`, `TrendFeed.cs`

- [x] start and stop under the cache lock, per Boundaries; start fixes the newest spectrum at the key
- [x] start, stop and a new reference recompute the recent spectra and raise a re-read that carries the reference
- [x] a key change stops normalization with the key change reason
- [x] tests: a refused all-zero start, the reset on a key change, live points normalized, a press racing a key change
  on two threads, the 75-not-66.7 case on the cache path
- [x] run tests - must pass before the next task

### Task 7: Normalization buttons and labels

**Goal:** the operator turns normalization on and off from the trend, and the trend says it is on
**Entry:** `Trend/TrendViewModel.cs`, `Trend/TrendChartView.axaml`
**Likely touches:** `TrendViewModel.cs`, `TrendChartViewModel.cs`, `TrendChartView.axaml`, `OverviewViewModel.cs`,
`UI/Chart/ChartStateLine.cs`, both resx files, `MainWindowTextFit*Tests`

- [x] first: a headless measure of the button in the 24 px margin; a taller button stops the run (Gates)
- [x] two toggle buttons in the top margin, right end, mirroring the cache's state; disabled per the Flow branches,
  `SpectrumStaleness` included
- [x] state line, Y label and readout reflect the factor; a toggle and a key change reset clear a held Y range; the
  overview refolds on start and stop
- [x] the refusal Warning and the reset Info reach the message panel
- [x] text-fit tests cover the button labels, the longest state line with them and the normalized Y label at the
  trend's floor, in both languages
- [x] tests: the Flow branches marked headless test, including a refused press that leaves the button released
- [x] run tests - must pass before the next task

### Task 8: PR 2 acceptance and docs

**Goal:** PR 2 meets its Acceptance items and the docs describe it
**Entry:** the plan's `## Acceptance` and `## Gates`

- [ ] every Behavior line and Flow branch on normalization holds; smoke steps 4 to 8 hold
- [ ] the Gate before PR 2: the screenshot to the operator
- [ ] full test suite: `dotnet test SemiSpec.slnx`
- [ ] `overview.md`: the intensity cache, the store read, the trend chart, the overview refold
- [ ] `readme.md` (Russian): the buttons, the reset rule, the steady-sample requirement, normalization not saved
  across a restart; the operator approves the wording before the commit

### Task 9: Upgrade to .NET 11

**Goal:** the solution builds, tests and publishes on SDK 11.0.100 and `net11.0`
**Entry:** `global.json`, `SemiSpec/Directory.Build.props`
**Likely touches:** `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.config/dotnet-tools.json`,
`.github/workflows/ci.yml`

- [ ] pin SDK 11.0.100 and `net11.0`; `LangVersion` stays 14
- [ ] move the Microsoft packages with an 11.0.0 release to it; keep the rest
- [ ] make `terse` run in `ci.yml` (Caveats)
- [ ] fix any new analyzer warning the SDK raises
- [ ] tests: the existing suite passes unchanged
- [ ] run tests - must pass before the next task

### Task 10: PR 3 acceptance and docs

**Goal:** PR 3 meets its Acceptance items and the docs describe it
**Entry:** the plan's `## Acceptance`

- [ ] the PR 3 Acceptance items pass, the green CI run included
- [ ] `CLAUDE.md`: .NET 11, the SDK pin
- [ ] `overview.md` stack and `docs/architecture/README.md` Platform row: .NET 11 (`net11.0`), C# 14
- [ ] `docs/architecture/README.md`: remove the Conventions row

## Post-Completion

- The operator's manual test: Acceptance smoke steps 1 to 9.
- The operator reviews the `readme.md` wording before each commit that changes it.
- On each operator PC: the next installer creates `C:\DISTR\Config\SemiSpec`; no manual step.
