# Spectrum view

## Overview
- Show the board's spectrum in the main window: a ScottPlot `SignalXY` line over wavelength in nm, taken from
  `SpectrometerCalibration` for the frame's device, with a ±`delta` window band around the SCADA wavelength the frame
  carries. The plot sits in a `BitmapCache` wrapper and redraws at most once per display frame, as in SemiPlot.
- Keep every viewer function of the old app: signal info (amplitude, SNR, Q as the board sent them), the last update
  time, inversion, a logarithmic Y axis, reset scale, stick to zero, and the message log with clearing (the existing
  message panel). The visual layout is new: the plot takes the content row, and a fixed-width pane on its right holds
  the signal values and the chart controls on one form grid.
- Give the link's `Frames` channel its one reader: a frame distributor service that keeps the latest spectrum frame and
  forwards every frame to its subscribers. The chart reads through a displayed-spectrum source with one live
  implementation; intensity-trend adds a historical one, spectrum-store adds its queue to the distributor.
- Dark frames (every pixel zero) render on both axis types without an exception and with finite limits.
- Scope guard: no storage, no trend chart, no hover readout, no change to the link, the connection row or the status
  bar, no change to the old app, no launch keys or config files, nothing sent to the board.

## Context (from discovery)
- Roadmap: docs/plans/roadmaps/20261104-avalonia-rewrite-roadmap.md, slice spectrum-view
- Stacking base: local branch `serial-link` at `5fca2ab`; this slice works on the local branch `spectrum-view` created
  from it. Local commits only.
- Verified 2026-11-04 at `5fca2ab`: `dotnet test SpectrumVisualizer.slnx` passes 320 of 320.
- The slice brief is the roadmap's spectrum-view scope (`roadmap:169-190`); its named risk is the log axis with zero
  pixels and the displayed-spectrum seam (`roadmap:184`). The later slices that use the seams: spectrum-store takes
  every frame from the distributor through its own queue (`roadmap:198-201`); intensity-trend shows a moment's spectrum
  through a historical displayed-spectrum source and reuses SemiPlot chart pieces (`roadmap:213-220`).
- Link surface this slice reads:
  - `SerialLink.Frames` is a `ChannelReader<ReceivedFrame>` (`SerialLink.cs:50`) over a bounded channel of 64 with
    `DropOldest`, `SingleWriter` and `SingleReader` (`:38-44`). The channel is created once per `SerialLink`
    (`:44`) and lives across sessions: a grep for `TryComplete|Complete(` in `SpectrumVisualizer.UI/Link/*.cs` finds
    nothing, so no code completes it. `Dispose()` cancels the session only (`:99-102`).
  - `ReceivedFrame(BoardFrame Frame, DateTimeOffset ReceivedAt)` (`ReceivedFrame.cs:6`); the link stamps it with its
    scheduler's `Now`, `DefaultScheduler.Instance` in production (`UiServiceCollectionExtensions.cs:27-30`), so the
    time is UTC.
  - `board-protocol.md:283-288` says the channel has one reader to come and that no code reads it yet.
- Core this slice consumes:
  - `BoardFrame(BoardState State, byte Seq, SpectrumMeasurement? Measurement)`, null for a heartbeat
    (`BoardFrame.cs:6`). `SpectrumMeasurement` carries `Device`, `WavelengthNm`, `Delta`, `Amplitude`, `Snr`, `Q`,
    `Pixels` (`BoardFrame.cs:20-58`); its constructor rejects a pixel array of the wrong length (`:29-34`). `Delta`'s
    doc says the app reads it as nm and the board still as pixels (`:49`). Equality compares every pixel (`:60-70`).
  - `SpectrometerCalibration.For(device)` (`SpectrometerCalibration.cs:41-49`), `WavelengthOf(pixel)` reading a
    wavelength computed once per pixel (`:24-33`, `:51-54`). The wavelengths ascend with the pixel: `PixelFor` binary
    searches them (`:60`). `Spectrometer.PixelCount` 2048 and 512 (`Spectrometer.cs:13-18`), `DisplayName`
    "ATP-2000", "ATP-8000" (`:24-29`).
  - Protocol facts: `snr` is a ratio, not dB (`board-protocol.md:54`); a dark frame has every pixel, `amplitude` and
    `snr` zero (`:58`); the app's window is ±`delta` nm, the board's Amplitude window still ±`delta` pixels on
    ATP-2000 until OptoskyProj#7 (`:317-320`).
- UI seams this slice changes:
  - Main window rows `Auto,Auto,*,Auto,Auto` (`MainWindow.axaml:19`); the content row is a `Border` named
    `ContentArea` with `Padding="16"` holding the `ContentPlaceholder` text (`:33-42`); the message panel row is
    hidden while the list is empty (`:44-52`). `MainWindow.MinimumWidth` is 1048 (`MainWindow.axaml.cs:9`), derived
    from the status bar (`overview.md:116-122`).
  - `MainWindowViewModel(MessagePanelViewModel, ConnectionBarViewModel, AppStatusBarViewModel, LogLevelControl,
    ILogger<>)` (`MainWindowViewModel.cs:23-39`).
  - `AddUi` registers the scheduler, the port provider, the link, the panel, the connection row, the status bar and the
    main window view model (`UiServiceCollectionExtensions.cs:25-34`).
  - `App.CreateMainWindow` refreshes the ports on `Opened` and stops the link on `Closing`
    (`App.axaml.cs:61-74`); `OnMainWindowClosing` holds the close until `StopLinkAsync` completes, at most 5 s
    (`:77-117`); `Configure` resolves `ConfiguredServices` (`:141-148`, record at `:168-172`).
  - The message panel already clears: `ClearCommand` (`MessagePanelViewModel.cs:26`) bound to the Clear button
    (`MessagePanelView.axaml:36-38`); it keeps 200 entries (`MessagePanelViewModel.cs:14`).
  - Palette `App*` keys: `Palette.axaml:64-73` (Light) and `:128-137` (Dark); no chart key exists. The key table is
    `ui-theme.md:44-55`; the inset and colour greps are `ui-theme.md:67` and `:80`.
  - Text keys table `ui-text.md:39-46`; `ContentPlaceholder` is owned by the main window (`:41`).
- Packages: `SpectrumVisualizer/Directory.Packages.props:8-32` pins no ScottPlot package; the UI project references
  Avalonia, ReactiveUI.Avalonia, Semi and the rest (`SpectrumVisualizer.UI.csproj:17-33`). `README.md:32` locks
  "ScottPlot 5" as the chart renderer.
- ScottPlot 5.1.59, documentation (`%USERPROFILE%/.nuget/packages/scottplot/5.1.59/lib/net10.0/ScottPlot.xml`):
  `Plot.Sync` is locked by the `Render()` methods, and code that edits data can lock it (`:2949-2954`);
  `Plot.RenderInMemory(int, int)` renders onto a new image (`:3026-3029`); `HorizontalSpan` marks the full vertical
  range between two horizontal values, an X range (`:3732-3735`); `FontStyler.Automatic()` picks a font per label from
  the characters it contains (`:6592-6596`); `UserInputProcessor.UserActionResponses` is the list of responses, and
  editing it changes the default behaviour (`:1836-1840`); `DoubleClickResponse` and `SingleClickResponse` carry a
  mouse button and a response action (`:1635-1646`, `:1789-1799`).
- SemiPlot pieces copied (`C:/Users/admin/projects/SemiPlot`, `bd8f83f`; paths below are relative to that repo root's
  `SemiPlot/` folder):
  - The pin `ScottPlot.Avalonia` 5.1.59 (`SemiPlot/Directory.Packages.props:29`) and its reference
    (`SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj:29`).
  - The cache wrapper: `PlotControl` alone inside the `PlotLayer` decorator whose `CacheMode` is a `BitmapCache`
    (`SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml:54-66`); the reason it sits on the wrapper, not on `AvaPlot`
    (`docs/architecture/charting.md:307-314`); the pin test `ThePlotIsCachedThroughItsOnlyAncestor`
    (`SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewTests.cs:278`).
  - The empty-state line over the plot (`TrendChartView.axaml:67-81`).
  - The frame-paced redraw: `RequestFrame`, `ServeFrame`, `ClearPendingFrame`
    (`SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs:289-324`), the clear in `OnDataContextChanged`
    (`:219-235`, `PlotControl.Reset(viewModel.Plot)` at `:235`), the `RedrawRequested` subscription (`:281-285`),
    `ServedFrameCount` (`:94`), the reasons in `docs/architecture/charting.md:263-291`, and the burst test
    `ABurstOfRedrawRequests_JoinsOneFrame` (`TrendChartViewTests.cs:171`).
  - Theme repaint on load and on a variant change (`TrendChartView.axaml.cs:102-148`), and `ReportFailureOf`
    (`:328-335`).
  - `ChartPalette.Apply` over five ScottPlot surfaces (`SemiPlot/SemiPlot.UI/Chart/ChartPalette.cs:14-46`) and its
    `Resolve`, which reads the brush's ARGB and ignores its `Opacity` (`:57-65`; `docs/architecture/ui-theme.md:750-752`).
  - `LogAxis.Project` and `LogAxis.Ticks` (`SemiPlot/SemiPlot.Core/Trends/LogAxis.cs`, 299 lines, `Project` at
    `:25`, `Ticks` at `:35`), the rules in `docs/architecture/charting.md:360-378`; `LogTickGenerator`
    (`SemiPlot/SemiPlot.UI/Chart/LogTickGenerator.cs:10-93`), whose labels go through a pen mask
    (`:19`, `:55`, `:69`); `TickCache` (`SemiPlot/SemiPlot.UI/Chart/TickCache.cs:8-39`); their tests
    `SemiPlot/SemiPlot.Tests.Unit/Core/Trends/LogAxisTests.cs` and
    `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/LogTickGeneratorTests.cs`.
  - The axis type in the tick generator, switched with the limits and the minor gridline width under one
    `lock (Plot.Sync)` (`SemiPlot/SemiPlot.UI/Chart/ChartAxisBinder.cs:57-66`, `docs/architecture/charting.md:333-341`,
    `:395-397`).
  - Palette values: `AppMinorGridLineBrush`, `AppMarkerLineBrush`, `AppAccentBrush`, `AppAccentFillBrush` at
    `SemiPlot/SemiPlot.UI/Styles/Palette.axaml:81-82`, `:86-87` (Light) and `:165-166`, `:170-171` (Dark).
  - A headless render of a real plot: `viewModel.Plot.RenderInMemory(320, 240)` (`TrendChartViewTests.cs:40`).
  - SemiPlot disables ScottPlot's input processor for its own gestures (`TrendChartView.axaml.cs:78`); this slice
    keeps it.
- Old app parity (`SpectrumVisualizer.Uart/`): the feature table in Solution Overview cites each old feature.
- Test traits: `Component` (`Core|UI|Tools`), `Area`, `Category=Unit` (`CLAUDE.md:40-41`,
  `testing-strategy.md:20-22`). This slice adds the Areas `Chart` and `Spectrum`.

## Development Approach
- **testing approach**: Regular (code first, then tests in the same task).
- complete each task fully before moving to the next; small, focused changes.
- every task includes new or updated tests for its code; all tests pass before the next task.
- update this plan when scope changes during implementation.
- the old solution keeps building and passing its 13 tests; this slice does not touch it.

## Testing Strategy
- **unit tests** in `SpectrumVisualizer.Tests.Unit`: plain `[Fact]` for the pure pieces (`LogAxis`, the scale rules,
  the projection, the distributor over a real `Channel`); `[AvaloniaFact]` for view models, the chart view and the
  realised window. Traits per class: `Area=Chart` for `Core/Chart/` (`Component=Core`) and `UI/Chart/`
  (`Component=UI`); `Area=Spectrum` for `Core/Spectrum/` (`Component=Core`) and `UI/Spectrum/` (`Component=UI`);
  `Area=Link` for the distributor; `Area=MainWindow` for the window rows and the layout; `Area=Di` for
  composition and lifecycle; `Area=Resources` for keys.
- **distributor tests** write `ReceivedFrame`s into a `Channel.CreateBounded<ReceivedFrame>` with the link's options
  and hand its reader to the distributor's constructor, which takes a `ChannelReader<ReceivedFrame>`; the container
  passes `SerialLink.Frames`. Every wait has a 5 s real-time limit (`Waits`).
- **view model tests** drive the spectrum view model with a fake `IDisplayedSpectrumSource` (a settable `Current` and
  a `Subject<Unit>` for `Changed`) and `AvaloniaScheduler` as the UI scheduler, as the status bar tests do.
- **headless view tests** realise the chart view and the main window: the frame-paced redraw through
  `ServedFrameCount`, the cache layout, the palette on the plot, the empty-state line, the pane rows.
- **render tests** call `Plot.RenderInMemory` on the view model's plot after a dark frame on both axis types.
- no e2e framework exists; headless tests take that role. The port-pair run is manual.

## Acceptance Evidence
Automatable (baseline 320 of 320 at `5fca2ab`):
1. `dotnet test SpectrumVisualizer.slnx --filter "Area=Chart"` passes, including:
   - `LogAxisTests` (`Core/Chart/`) copied from SemiPlot: tick families, minors, the bounds that return nothing.
   - `LogTickGeneratorTests.MajorsOverDecadesCarryTheValueAsAnIntegerCount`,
     `FractionalMajorsUseTheCultureDecimalSeparator`, `ACultureChangeRegeneratesTheCachedLabels`,
     `ARepeatedLabelTurnsMinor`, `TheSecondLengthOfAFrameReusesTheCachedTicks`.
   - `ChartPaletteTests.ApplyPaintsTheFiveSurfacesFromThePaletteUnderBothVariants` and
     `ATranslucentKeyKeepsItsOpacityInTheAlpha` (`AppAccentFillBrush` resolves to alpha 64).
2. `dotnet test SpectrumVisualizer.slnx --filter "Area=Link"` passes, including the existing link tests and
   `FrameDistributorTests`: `EveryFrameReachesEverySubscriberInOrder`, `ALateSubscriberSeesOnlyLaterFrames`,
   `TheLatestSpectrumSkipsHeartbeats`, `AThrowingSubscriberDoesNotTakeTheFrameFromTheOthers` (subscriber 1 throws on a
   frame, subscriber 2 receives that same frame, the panel gets one Error entry),
   `AnUnsubscribedObserverReceivesNothingMore`, `StopEndsTheReadLoopAndStartAfterStopDoesNothing`,
   `StopBeforeStartCompletesAtOnce` (a counting reader shows no read begins),
   `AFailingReadIsLoggedAtErrorAndStopStillCompletes`.
3. `dotnet test SpectrumVisualizer.slnx --filter "Area=Spectrum"` passes, including:
   - `SpectrumScaleTests` (`Core/Spectrum/`): auto limits pad 5 % of the span; a flat linear range gives
     `v - 1 .. v + 1` and a flat log range gives `v - 0.5 .. v + 0.5` decades; stick to zero gives `0 .. max x 1.2` linear and
     `0 .. log10(max x 1.2)` decades; an all-zero spectrum gives finite limits in every mode.
   - `SpectrumProjectionTests` (`Core/Spectrum/`): inversion writes `max - value`; the log projection writes `log10(max(value, 1))`, so a
     zero pixel lands on 0 decades; an all-zero spectrum inverted stays all zero; a 65535 pixel gives 4.8165 decades.
   - `LiveSpectrumSourceTests`: `Current` is null before a spectrum; a heartbeat changes neither `Current` nor
     `Changed`; a spectrum frame updates `Current` with its `ReceivedAt` and signals `Changed`; two reads of one frame
     carry the same measurement.
   - `SpectrumChartViewModelTests`: the first spectrum sets the X limits to the device's wavelength range
     (`WavelengthOf(0)` to `WavelengthOf(P - 1)`); the window band spans `WavelengthNm - Delta` to
     `WavelengthNm + Delta` and the centre line sits at `WavelengthNm`; an ATP-2000 frame after an ATP-8000 frame
     shows the ATP-2000 line only and moves the auto X limits;
     `ABandOutsideTheDeviceRangeIsHiddenAndTheXLimitsStayTheDeviceRange` (an ATP-8000 frame at 400 nm: band and
     centre line invisible, X `WavelengthOf(0)..WavelengthOf(511)` of ATP-8000); the log axis carries a
     `LogTickGenerator` and 1 px minor gridlines, the linear axis neither; the frame's pixel array is never written;
     `ApplyPaletteRunsUnderThePlotLock`.
   - `SpectrumChartViewModelScaleTests`: a manual scale freezes the limits across later frames;
     `AManualScaleBeforeTheFirstSpectrumDoesNothing`; `AHeldMoveFreezesTheScaleFromFivePixelsAlongEitherAxis`
     (`OnHeldMove`: 4.9 px keeps following, 5 px on either axis freezes, no held button keeps following); Reset scale
     returns to following; `AFrameFromTheOtherDeviceWhileFrozenKeepsTheFrozenLimits` (the line may lie outside them;
     accepted); Stick to zero sets the bottom to zero and freezes; toggling log or inversion returns to following;
     `ToggleWhileASpectrumIsShownTakesTheFollowingLimitsOfTheNewMode`;
     `ADarkFrameKeepsTheLastNonDarkYLimitsOfTheSameDeviceOnEveryDisplay`,
     `ADarkFrameAfterALogToggleKeepsTheLogLimitsOfThePeak`,
     `AToggleDuringADarkFrameTakesTheLastNonDarkLimitsInTheNewMode`,
     `StickToZeroOnADarkFrameUsesTheLastNonDarkMaximum`, `ADarkFirstFrameAfterADeviceSwitchTakesTheFlatRange`,
     `ADarkFrameAfterTheOtherDeviceKeepsTheLimitsOfItsOwnDevicesLastPeak`.
   - `SpectrumChartViewModelRenderTests.ADarkFrameRendersOnTheLinearAxisWithFiniteLimits` and
     `ADarkFrameRendersOnTheLogAxisAtTheFloorWithFiniteLimits`: `Plot.RenderInMemory(640, 480)` throws nothing and
     every axis limit is finite, and after a peak the dark frame keeps its limits;
     `AnInvertedDarkFrameOnTheLogAxisRenders`.
   - `SignalInfoViewModelTests`: "-" in every row before a spectrum; the device name, `"{0} nm"`, `"±{0} nm"`,
     amplitude, SNR and Q in `G6` under the current culture, a non-finite value as "-", the update time as
     `HH:mm:ss.fff` in the `TimeProvider`'s local zone.
   - `SpectrumViewModelTests`: three `Changed` signals before the UI scheduler runs apply the latest spectrum once;
     `TheSameMeasurementIsAppliedOnceByReference`; a spectrum present at construction shows at once; Stick to zero
     is enabled once a spectrum is shown; Reset scale is enabled only with a spectrum and a frozen scale
     (`ResetScaleShowsTheFrozenState`); `AThrowingShowReportsOnceAndTheNextSpectrumStillShows`,
     `AThrowingCurrentOnALaterSignalReportsAndThePipelineGoesOn`, `DisposeEndsTheChangedSubscription`.
   - `SpectrumChartViewTests`: `ThePlotIsCachedThroughItsOnlyAncestor`, `ABurstOfRedrawRequestsJoinsOneFrame`,
     `ARequestOnADetachedViewServesNothing`, `TheEmptyStateShowsUntilTheFirstSpectrum`,
     `ALoadedViewRepaintsThePlotWhenTheVariantChanges`, `AnUnloadedViewIgnoresAVariantChange`,
     `ANullDataContextAndBackKeepsThePlottables`, `ADataContextSwitchEndsTheOldRedrawSubscription`,
     `DisposingTheViewModelClearsThePendingFrame`.
   - `SpectrumChartViewInputTests`: `OnlyPanZoomAndWheelResponsesRemain` (the list holds no
     `DoubleClickBenchmark`, `SingleClickAutoscale`, `SingleClickContextMenu`, `KeyboardAutoscale` or
     `KeyboardPanAndZoom`), `AWheelStepFreezesTheScale`, `APressAndReleaseWithoutAMoveKeepsFollowing`,
     `AHeldMoveFreezesTheScaleFromFivePixels` (0 and 2 px keep following, 6 and 40 px freeze),
     `AWheelOnTheEmptyPlotLeavesTheFirstSpectrumFollowing`.
4. `dotnet test SpectrumVisualizer.slnx --filter "Area=MainWindow"` passes, including `MainWindowViewTests` (rows top
   to bottom: menu, connection row, content, message panel, status bar; the content row holds the chart pane and the
   signal pane; no placeholder), `MainWindowSignalPaneTests` (every signal row and chart control realised; a checkbox
   reaches the chart; the insets read off the spacing table; the pane keeps its width from the minimum to 2560 px; the
   chart takes the spare width and height; every spectrum state keeps the window and the pane bounds),
   `MainWindowMessagePanelTests`, `MainWindowLayoutTests` (the connection row and the status bar) and
   `MainWindowTextFitTests` (every pane label, value and control on one line
   in English and Russian; the minimum width holds the status bar, and the pane plus the chart floor).
5. `dotnet test SpectrumVisualizer.slnx --filter "Area=Di"` passes, including: the distributor, the live source and the
   spectrum view model resolve as singletons; `AppLifecycleTests.OpeningTheWindowStartsTheFrameDistributor` (after
   `Opened` and Connect over `FakeSerialPortProvider`, frame bytes fed through `FakePortStream` reach a distributor
   subscriber); `AppLifecycleTests.ClosingTheWindowStopsTheDistributorAfterTheLink` (after the close completes, the
   distributor's stop line is in the log); `AppLifecycleTests.AfterTheCloseADistributorSubscriberReceivesNothingMore`
   (a frame the link writes after the close stays in `SerialLink.Frames` and reaches no subscriber).
6. `dotnet test SpectrumVisualizer.slnx --filter "Area=Resources"` passes with the four new palette keys under both
   variants and every new resx key in the Russian set.
7. `dotnet test SpectrumVisualizer.slnx` passes 320 plus every test this slice adds, with none skipped.
8. `dotnet build SpectrumVisualizer.slnx -c Release` 0 warnings; `dotnet format SpectrumVisualizer.slnx
   --verify-no-changes` and `dotnet terse SpectrumVisualizer` exit 0; `dotnet build SpectrumVisualizer.sln` and
   `dotnet test SpectrumVisualizer.Tests` pass 13 of 13.
9. The two `git grep` commands of `ui-theme.md:67` and `:80` print nothing.

Manual smoke checklist (owner; com0com pair `COM10`-`COM11`, the emulator on `COM10`, the app on `COM11`;
`emu` stands for `dotnet run --project SpectrumVisualizer/SpectrumVisualizer.Tools.BoardEmulator --`):
1. Start the app. The plot area shows the empty-state line; every value in the signal pane reads "-"; Reset scale and
   Stick to zero are disabled; both checkboxes are clear. A wheel step or a drag over the empty plot leaves Reset
   scale disabled, and the first spectrum in step 2 still sets the limits.
2. Run `emu --port COM10`, select `COM11`, Connect. Within 2 s the plot shows an ATP-8000 spectrum: a peak at
   1550 nm reaching about 31000, 30000 over a floor of 1000 (`board-protocol.md:336-337`,
   `SpectrumGenerator.cs:9-10`, `:20-21`), X from 895.4 to 2464.3 nm (the polynomial at pixels 0 and 511), and a band
   from 1540 to 1560 nm with a centre line at 1550 nm. The pane reads ATP-8000, 1550 nm, ±10 nm, 30000, 30, 0, and
   the update time advances once a second.
3. Restart the emulator with `--device 2000 --wavelength 400 --delta 25`. The X range moves to 185.9 to 1025.3 nm,
   the peak and the centre line sit at 400 nm, the band spans 375 to 425 nm, the pane reads ATP-2000, 400 nm, ±25 nm.
4. Restart it with `--device 2000 --wavelength 400 --delta 25 --dark-every 3`. Every third spectrum is a flat line at
   0 with amplitude 0 and SNR 0; the Y axis keeps the limits of the previous spectrum, so the axis does not jump; X
   stays the ATP-2000 range; the message panel gets no entry and the log no exception. Press Stick to zero while a
   dark spectrum shows: Y runs from 0 to about 37200, 1.2 times the previous peak, not 0 to 1. Press Reset scale.
5. Tick "Logarithmic scale". The Y axis labels powers of ten with minor gridlines; the floor of 1000 sits at 10^3,
   the peak near 10^4.5. A dark spectrum keeps those limits; its line lies on the 10^0 floor, below the kept range,
   so the data area is empty for that frame and the axis does not move. Tick or untick it while a dark spectrum
   shows: the axis takes the previous spectrum's limits in the new mode at once, not a flat range around 0. Untick
   it: the linear axis returns and the plot follows the data again.
6. Tick "Invert spectrum". The peak turns into a dip to 0 at 400 nm and the floor reads about 30000. With the log scale on
   as well, the dip lands on the 1 gridline. A dark spectrum stays a flat line at 0. Untick both.
7. Click the plot without moving the mouse, then press, move it by 1 to 4 px and release: the plot keeps following
   the data and Reset scale stays disabled. Zoom with the mouse wheel over the peak: Reset scale turns enabled and the
   next spectra keep the zoomed limits. Press Reset scale: it turns disabled and the plot follows the data again.
   Drag the plot with the left button by 5 px or more: the limits freeze again. Press Reset scale, then Stick to zero:
   the Y axis starts at 0 and ends at about 37200 (1.2 times the peak), and later spectra keep those limits until
   Reset scale.
8. Restart the emulator with `--device 2000 --wavelength 700 --delta 0`: the band collapses to the centre line at
   700 nm and the pane reads ±0 nm. With the limits frozen by Stick to zero, the limits stay; Reset scale follows
   again. Restart it with `--device 8000 --wavelength 400`: X follows the ATP-8000 range, no band or centre line is
   drawn, and the pane reads 400 nm.
9. Restart it with `--device 2000 --rate 2.5`. The update time advances about every 0.4 s; the window stays responsive
   to resize and to the checkboxes.
10. Press Disconnect. The plot keeps the last spectrum and the update time stops; Connect again resumes the updates.
11. Resize the window from 1280 px to its minimum width and to full screen on a wide monitor. The signal pane keeps
    its width and its left edge follows the window's right edge; the plot takes the spare width and height; every
    pane text stays on one line. Stop the emulator: 10 s later the "Board not responding" Warning opens the message
    panel row, the plot loses that height and keeps its width, and the window keeps its size.
12. With a Russian Windows UI every new text is Russian; with English, English. The pane fits at the window's opening
    width in both languages.

## Progress Tracking
- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update plan if implementation deviates from original scope

## Solution Overview
- **Folders.** Core holds the algorithms that need no UI type: `SpectrumVisualizer.Core/Chart/` the log axis ticks
  copied from SemiPlot (`LogAxis`), `SpectrumVisualizer.Core/Spectrum/` the display mode (`SpectrumDisplay`) and the
  projection and scale rules. `SpectrumVisualizer.UI/Chart/` holds the ScottPlot pieces copied from SemiPlot (palette,
  tick generator with its private tick cache); intensity-trend reuses both folders. `SpectrumVisualizer.UI/Spectrum/`
  holds the displayed-spectrum seam, the view models and the views of this slice. The distributor sits in
  `SpectrumVisualizer.UI/Link/`, beside the channel it drains.
- **Frame distributor.** `FrameDistributor(ChannelReader<ReceivedFrame> frames, MessagePanelViewModel, IScheduler
  uiScheduler, ILogger<FrameDistributor>)`, a singleton, is the only reader of `SerialLink.Frames`; `AddUi` passes
  that reader through a factory lambda, so tests hand it a real channel without a test-only constructor. `Start()`
  runs one task:
  `await foreach (var frame in reader.ReadAllAsync(token))`. For each frame it publishes `LatestSpectrum` with
  `Volatile.Write` when the frame carries a measurement, then hands the frame to each subscriber in turn.
  `IObservable<ReceivedFrame> Frames` is a small `IObservable` over the distributor's own subscriber list, not
  `Observable.Create`, whose wrapper unsubscribes an observer that throws: subscribing adds the observer and the
  returned disposable removes it. The list is copy-on-write (an `ImmutableArray` replaced
  under a lock, because subscribers come and go on any thread), and the loop reads one snapshot per frame. Each
  subscriber's `OnNext` runs inside its own try/catch, so a subscriber that throws takes nothing from the others: the
  same frame still reaches every later subscriber. The exception is logged at Error and reported once through
  `TryReportFailure` with the UI scheduler, and the next frame goes out as usual. A `Subject` would stop the frame at the
  first throwing observer. The channel never completes, so the loop
  lives across link sessions and ends on cancellation, or on any other read failure, which it logs at Error.
  `StopAsync()` cancels and awaits the task, then disposes the cancellation source, and completes at once when nothing
  runs; `Start()` after a start or a stop does nothing; `Dispose()` cancels without waiting, for the
  container's synchronous dispose. Subscribers own their scheduling and never block the loop. The one subscriber is
  `LiveSpectrumSource`, which filters spectrum frames and signals; `SpectrumViewModel` reads the source, not the
  distributor, and moves to the UI scheduler with `ObserveOn`; spectrum-store adds its own queue later.
- **Why `LatestSpectrum` and not the latest frame.** A heartbeat carries no measurement (`BoardFrame.cs:6`); if the
  latest frame were a heartbeat the chart would have nothing to read for up to 3 s. The distributor keeps the latest
  frame that carries a spectrum, and that is the only "latest" anything reads.
- **Displayed-spectrum seam.** `DisplayedSpectrum(SpectrumMeasurement Measurement, DateTimeOffset ReceivedAt)` is what
  the chart and the pane show. `IDisplayedSpectrumSource` has `DisplayedSpectrum? Current` and `IObservable<Unit>
  Changed` (emits on any thread; the reader reads `Current` after it). It exists for its two planned implementations:
  `LiveSpectrumSource` now, a historical source in intensity-trend. Intensity-trend adds a switching source that
  implements `IDisplayedSpectrumSource` and replaces the `LiveSpectrumSource` registration, so `SpectrumViewModel`
  keeps one source. A historical source rebuilds a full `DisplayedSpectrum`, so spectrum-store persists device,
  wavelength, delta, amplitude, snr, q, pixels and `ReceivedAt`. `LiveSpectrumSource(FrameDistributor)` maps
  `LatestSpectrum` to a new `DisplayedSpectrum` on every read, around the frame's own measurement, and `Changed` is
  `distributor.Frames.Where(frame => frame.Frame.Measurement is not null).Select(_ => Unit.Default)`.
- **Latest spectrum only.** `SpectrumViewModel` subscribes in its constructor:
  `source.Changed.ObserveOn(uiScheduler).StartWith(Unit.Default)`, a read of `source.Current`, `Where(not null)`,
  `DistinctUntilChanged(spectrum => spectrum.Measurement, ReferenceEqualityComparer.Instance)`, then applies the
  spectrum to the chart and the pane. Signals queued while the UI thread is busy each read the same latest
  measurement, and `DistinctUntilChanged` drops the repeats by reference on it, so the chart applies the latest
  spectrum once without comparing pixel arrays (record equality on `SpectrumMeasurement` compares every pixel,
  `BoardFrame.cs:60-70`). The subscription ends in `Dispose`; the view model is a container singleton, so its
  subscription lives for the process like the status bar's. A throw from the read or from showing a spectrum goes to
  `TryReportFailure` and skips that spectrum only; a failure of `Changed` itself ends the subscription with the same
  report.
- **Chart model.** `SpectrumChartViewModel` owns the ScottPlot `Plot`, as SemiPlot's chart view model does, and the
  view binds it with `PlotControl.Reset(viewModel.Plot)`. It holds one `SignalXY` per device, built once with the
  device's wavelengths as `xs` and its own `double[] ys` buffer; only the shown device's line is visible. The window is
  a `HorizontalSpan` from `WavelengthNm - Delta` to `WavelengthNm + Delta` filled with `AppAccentFillBrush`, and a
  `VerticalLine` at `WavelengthNm` in `AppMarkerLineBrush`. `Show(DisplayedSpectrum)` runs on the UI thread: under
  `lock (Plot.Sync)` it fills the device's `ys` through `SpectrumProjection`, moves the band and the line, toggles the
  line visibility on a device change and, while the scale follows, sets the limits from `SpectrumScale`; then it calls
  `RequestRedraw()`. The frame's pixel array is read, never written; the store reads the same array later.
  The view runs `ChartPalette.Apply` and `ApplyPalette(ChartColors)` inside one hold of `lock (Plot.Sync)`, so a
  render never sees half a repaint. `IsFollowing` raises its change after the lock is released.
- **Band outside the device range.** The following X range is always the shown device's own wavelength range. When the
  band `WavelengthNm - Delta .. WavelengthNm + Delta` does not overlap that range (for example an ATP-8000 frame taken
  at 400 nm), the band and the centre line are hidden; the pane still shows the wavelength and the window. A band that
  overlaps the range in part is drawn and clipped by the data area. While the scale is frozen on one device, a frame
  from the other device keeps the frozen limits, and its line can lie outside them, leaving the plot blank until Reset
  scale; that is accepted, because the operator froze the view.
- **Frame-paced redraw.** Copied from SemiPlot: `RequestRedraw()` raises `RedrawRequested` when a view subscribes; the
  view asks `TopLevel.RequestAnimationFrame` once per pending frame and invalidates the plot in the callback, so every
  request before the next display frame joins one redraw. Nothing in the view calls `AvaPlot.Refresh()`. The only
  redraws outside this path are ScottPlot's own, while the operator drags or zooms.
- **Cache wrapper.** `PlotControl` sits alone in the `PlotLayer` decorator, which carries the `BitmapCache`; the
  cache never goes on `AvaPlot` itself, because Avalonia 12 then freezes the plot on its first frame
  (`SemiPlot/docs/architecture/charting.md:307-314`).
- **Scale.** The scale either follows the data or is frozen. It follows from the start, after Reset scale and after a
  log or inversion toggle (the old limits are in other units or describe another curve). Once a spectrum is shown it
  freezes on a wheel step over the plot and on a pointer move with a button held 5 px or more from the press (a drag),
  and after Stick to zero; before the first spectrum nothing freezes it. Tunnel handlers for `PointerWheelChanged`,
  `PointerPressed`, `PointerMoved` and `PointerReleased` record the press and forward to the chart: a wheel step calls
  `TakeManualScale()`, a move calls `OnHeldMove(press, current, isButtonHeld)`, which decides the freeze from the held
  button and the larger of the X and Y distances. The 5 px is ScottPlot 5.1.59's own floor for `MouseDragPan` and
  `MouseDragZoomRectangle`; ScottPlot still applies a shorter pan on release, and the next spectrum sets the limits
  back while following. A bare click, a press and release without a
  move, keeps it following; so does a key press. While it follows, every spectrum sets the limits; while frozen, no
  spectrum moves them. While a dark frame is shown, the Y limits come from the last non-dark measurement of its own
  device projected in the current mode, so the axis does not jump every Nth frame, Stick to zero takes that spectrum's maximum, and a log
  or inversion toggle shows that spectrum's limits in the new mode. The dark line draws at its floor inside that range
  (on the log axis the floor of 0 decades lies below a range from a spectrum whose minimum is above 1, so the data area
  is empty for that frame), and X still follows the dark frame's device. With no earlier non-dark frame of that
  device, the flat-range limits apply. This is the old
  OxyPlot behaviour: its axes rescaled on every update until the operator zoomed, and Reset scale reset them
  (`SpectrumPainter.cs:66-70`, `:82`). The limits come from `SpectrumScale`, never from ScottPlot's autoscale, so a
  flat range has a defined answer and no limit is NaN. Reset scale is enabled only while a spectrum is shown and the
  scale is frozen, so the button also shows the frozen state.
- **Log axis.** ScottPlot has no log axis, and `SignalXY` has no render hook to project at draw time as SemiPlot's own
  plottable does. So the data is projected: with the log scale on, `ys[i] = log10(max(value, 1))` and the axis runs in
  decades with a `LogTickGenerator`; with it off, `ys[i] = value` and the axis takes a fresh `NumericAutomatic`. The
  toggle re-projects the shown spectrum and swaps the generator, the limits and the minor gridline width (1 px on log,
  0 on linear) under one `lock (Plot.Sync)`. The type lives in the tick generator, as in SemiPlot.
- **Floor for zero pixels.** Pixels are integers, so 1 is the smallest positive reading, and a zero pixel is drawn on
  the 10^0 line with the ones. The axis then never needs a decade below 0, and a dark frame with no earlier spectrum
  is a flat line at 0 decades with limits -0.5 to 0.5.
- **Inversion.** `max - value` over the frame's own maximum, as the old app (`SpectrumManager.cs:32-39`); applied
  before the log projection. An all-zero spectrum stays all zero.
- **Pointer input.** ScottPlot's stock input processor stays on: wheel zoom, drag pan, drag zoom and the zoom
  rectangle cover the old plot's zoom and pan. A fresh `AvaPlot` 5.1.59 holds, in order,
  `MouseInteractWithPlottables`, `MouseDragZoomRectangle`, `MouseDragPan`, `MouseDragZoom`, `MouseWheelZoom`,
  `SingleClickAutoscale`, `SingleClickContextMenu`, `DoubleClickBenchmark`, `KeyboardPanAndZoom` and
  `KeyboardAutoscale`; the XML documentation has no default list, so Task 10 read it through reflection. The view
  removes `DoubleClickBenchmark`, `SingleClickAutoscale`, `SingleClickContextMenu`,
  `KeyboardAutoscale` and `KeyboardPanAndZoom`: the app is a read-only viewer, ScottPlot's autoscaler never sets the
  limits, and keyboard input never moves them. Pan, drag zoom, the zoom rectangle and wheel zoom remain. Reset scale
  is the one way back to following. `AvaPlot.HandleMouseWheelEvent` stays at its default.
- **Signal pane.** `SignalInfoViewModel` holds the shown `DisplayedSpectrum` and computes seven texts from it, each
  notifying from the setter of that property: spectrometer, wavelength, window, amplitude, SNR, Q factor, updated.
  Amplitude, SNR and Q show the board's values in `G6` under the current culture; a NaN or infinite value shows "-".
  SNR has no unit (the board sends a ratio). The update time is `ReceivedAt` converted to the `TimeProvider`'s local
  zone, `HH:mm:ss.fff`. Before the first spectrum every row reads "-", so every row exists in every state.
- **Chart controls.** `SpectrumViewModel` exposes `IsInverted` and `IsLogarithmic` (two-way bound; the setter assigns
  and raises, and one `WhenAnyValue` over both builds a `SpectrumDisplay` for `Chart.SetDisplay`, which re-projects), `ResetScaleCommand` and `StickToZeroCommand`
  (`ReactiveCommand.Create`; Stick to zero's `canExecute` is `HasSpectrum`, Reset scale's is `HasSpectrum &&
  !IsFollowing`, both from `WhenAnyValue` on the chart). It owns `Chart` and `SignalInfo` and builds both.
- **Empty state.** Until the first spectrum, a line over the plot reads "No spectrum yet. Connect to the board in the
  row above." After a Disconnect the last spectrum stays, and its update time tells its age; a new Connect does not
  clear it.
- **Layout** (the `dev:desktop-ui` rules). The window stays resizable; the plot is the one element that stretches. The
  content row becomes a `Grid` with columns `*,Auto`:
  - Column 0, `SpectrumPane`: the chart view, edge to edge with no padding, because ScottPlot's figure draws its own
    axis margins and paints its figure background with `AppPanelBackgroundBrush`.
  - Column 1, `SignalPane`: a fixed-width `Border` with a left border in `AppBorderBrush`, `AppPanelBackgroundBrush`,
    `Padding="16"`, one form grid (`ColumnDefinitions="Auto,Auto"`, `ColumnSpacing="12"`, `RowSpacing="8"`):
    - section header "Signal" (bold, sentence case, 0 above, 8 below), then seven label and value rows;
    - section header "Chart" (16 above, 8 below), then "Invert spectrum" and "Logarithmic scale" checkboxes in the
      field column with empty label cells (their text names the setting), then Reset scale and Stick to zero, one
      button per row in the field column.
  - The field column is 120 px (the number width) or the widest checkbox or button, measured in English and Russian.
    The pane width is 16 + label column + 12 + field column + 16 + 24 slack, rounded up to the 8 px grid, set as
    `SpectrumPanel.PaneWidth` and asserted by the text-fit test. Values are read-only text, left-aligned in the field
    column, `NoWrap`.
  - The message panel row keeps its show-on-first-entry behaviour; it takes height from the plot and never changes the
    window size.
- **Window minimum.** `MainWindow.MinimumWidth` becomes the larger of the status bar's measured width and the signal
  pane plus a 400 px chart floor, rounded up to 8 px. The floor is the narrowest plot at which the ATP-8000 X axis still
  carries at least four labelled ticks, measured with `RenderInMemory` in the checklist task.
- **Palette.** Four SemiPlot keys join `Palette.axaml` in both variants: `AppAccentBrush` (the spectrum line),
  `AppAccentFillBrush` (the window band, 25 % opacity), `AppMarkerLineBrush` (the centre line),
  `AppMinorGridLineBrush` (log minor gridlines). `ChartPalette.Resolve` multiplies the colour's alpha by the brush's
  `Opacity`, so the fill keeps its transparency on the plot; SemiPlot's copy reads the ARGB alone.
- **Log tick labels.** `LogTickGenerator` labels a major `10^position` with `"0.###"` under the current culture, keeps
  SemiPlot's rule that a repeated label turns into a minor, and drops the mask, the zero-label and the read-back rules,
  which exist for SemiPlot's pen formats. `LogAxis.AdmitsMinimum` is not copied: nothing here takes a manual minimum.
  SemiPlot's `IDrawnTickGenerator` and `LinearTickGenerator` are not copied: they skip hidden axes in a multi-axis set,
  and this plot draws one Y axis.

Old app features and their new home:

| Old feature | Old code | New home |
| --- | --- | --- |
| Spectrum line over wavelength | `SpectrumManager.cs:41-49`, `SpectrumPainter.cs:126-135` | `SignalXY` per device over `SpectrometerCalibration.WavelengthOf` |
| Axis titles "Длина волны [нм]", "Интенсивность", "Интенсивность (Log)" | `SpectrumPainter.cs:20-22` | `SpectrumAxisWavelength`, `SpectrumAxisIntensity` in resx; the log state shows in the checkbox and the tick labels |
| Plot title "Спектр" | `SpectrumPainter.cs:19`, `:95-96` | dropped: the window title names the app and the pane names the content |
| Signal info: average, SNR, Q | `mainForm.cs:99-106`, `mainForm.Designer.cs:104`, `:135`, `:145` | signal pane rows Amplitude, SNR, Q factor, in the protocol's order Amp, SNR, Q (`board-protocol.md:53-55`); the old app read them as Average, SNR, Quality and showed SNR and Q swapped (`roadmap:42`) |
| SNR in "дБ" | `mainForm.cs:104` | no unit: the board sends a ratio (`board-protocol.md:54`) |
| Number format F0 for 0..10, F2 otherwise | `mainForm.cs:103-105` | `G6` under the current culture; non-finite as "-" |
| Last update time, `HH:mm:ss.fff`, drawn in the plot's corner | `SpectrumPainter.cs:23`, `:40-47`, `:61-62`, `:161-182` | the Updated row in the pane, from `ReceivedAt` in local time |
| Inversion checkbox, `max - value` | `mainForm.cs:25`, `SpectrumManager.cs:20`, `:32-39` | "Invert spectrum" checkbox; applied to displayed values, before the log projection |
| Logarithmic Y axis | `mainForm.cs:28`, `SpectrumPainter.cs:85-91`, `:112-124` | "Logarithmic scale" checkbox; zero pixels on the 10^0 line |
| Reset scale | `mainForm.cs:26`, `SpectrumPainter.cs:66-70`, `:82` | Reset scale button: the scale follows the data again |
| Stick to zero | `mainForm.cs:27`, `SpectrumPainter.cs:72-76`, `:83`, `:137-156` | Stick to zero button: Y from 0 to max x 1.2, X over the device's range, frozen; the old X margin of 5 % of each wavelength value is dropped |
| Mouse zoom and pan on the plot | ASSUMPTION: OxyPlot's default `PlotView` controller, not configured in the old code | ScottPlot's stock pan, drag zoom, zoom rectangle and wheel zoom; no click or keyboard responses; a wheel step or a drag freezes the scale |
| Message log with "Отчистить историю сообщений", 100 entries | `mainForm.cs:22`, `:29`, `mainForm.Designer.cs:112`, `EventHandler.cs:12`, `:26-42` | the existing message panel and its Clear button (`MessagePanelView.axaml:36-38`, `MessagePanelViewModel.cs:14`, `:26`); no change |
| "Текущее устройство: COMx" group box title | `mainForm.cs:75` | the status bar's port segment (serial-link); no change |
| Port list, Connect, Disconnect | `mainForm.cs:44-51`, `:137-176` | the connection row (serial-link); no change |
| A new plot on every Connect | `mainForm.cs:87` | the plot keeps the last spectrum across Disconnect and Connect |
| ±`delta` window marker | none: the old frame carried no wavelength (`roadmap:38`) | band and centre line from the frame's `WavelengthNm` and `Delta` |

## Technical Details
Scale rules (`SpectrumScale`, pure; `ys` are the projected values of the shown spectrum; `xs` the device's wavelengths):

| Case | X | Y, linear | Y, log (decades) |
| --- | --- | --- | --- |
| Following | `xs[0] .. xs[P - 1]` of the shown device | `min - 5 % .. max + 5 %` of the reference's span | `min - 5 % .. max + 5 %` of the reference's span |
| Following, flat reference (`min == max`) | same | `v - 1 .. v + 1` | `v - 0.5 .. v + 0.5` |
| Stick to zero | same | `0 .. rawMax x 1.2`; `0 .. 1` when `rawMax == 0` | `0 .. log10(rawMax x 1.2)`; `0 .. 1` when `rawMax <= 1` |
| Frozen | unchanged | unchanged | unchanged |

The reference is the shown spectrum, or, while a dark frame is shown, the last non-dark measurement of its own device
projected again in the current inversion and axis mode; with no earlier non-dark frame of that device the dark frame
is its own reference and takes the flat range. `rawMax` is the reference's largest displayed value before the log projection (after inversion). A
dark frame is one whose raw pixels are all zero. Every input is a finite `ushort` or its logarithm, so every limit is
finite.

Projection (`SpectrumProjection.Fill(ReadOnlySpan<ushort> pixels, SpectrumDisplay display, Span<double> ys)`):
`value = display.IsInverted ? max - pixel : pixel`;
`ys[i] = display.IsLogarithmic ? Math.Log10(Math.Max(value, 1)) : value`.

Signal pane texts (resx keys in brackets):

| Row label | Value |
| --- | --- |
| Spectrometer (`SignalDevice`) | `Spectrometer.DisplayName` |
| Wavelength (`SignalWavelength`) | `SignalWavelengthFormat` `{0} nm` |
| Window (`SignalWindow`) | `SignalWindowFormat` `±{0} nm` |
| Amplitude (`SignalAmplitude`) | `G6` |
| SNR (`SignalSnr`) | `G6` |
| Q factor (`SignalQ`) | `G6` |
| Updated (`SignalUpdated`) | `HH:mm:ss.fff`, local time |

Before the first spectrum, and for a non-finite float, a value reads `StatusNoValue` ("-").

New resx keys (en and ru): `SignalTitle`, `SignalDevice`, `SignalWavelength`, `SignalWindow`, `SignalAmplitude`,
`SignalSnr`, `SignalQ`, `SignalUpdated`, `SignalWavelengthFormat`, `SignalWindowFormat`; `ChartTitle`,
`ChartInvert`, `ChartLogScale`, `ChartResetScale`, `ChartStickToZero`; `SpectrumAxisWavelength`,
`SpectrumAxisIntensity`, `SpectrumEmpty`. `ContentPlaceholder` is removed. Russian texts: "Сигнал", "Спектрометр",
"Длина волны", "Окно измерения", "Амплитуда", "Сигнал/шум", "Добротность", "Обновлено", "{0} нм", "±{0} нм"; "График",
"Инвертировать спектр", "Логарифмическая шкала", "Сбросить масштаб", "Прижать к нулю"; "Длина волны, нм",
"Интенсивность", "Спектра пока нет. Подключитесь к плате в строке выше." English: "Signal", "Spectrometer",
"Wavelength", "Window", "Amplitude", "SNR", "Q factor", "Updated", "{0} nm", "±{0} nm"; "Chart", "Invert spectrum",
"Logarithmic scale", "Reset scale", "Stick to zero"; "Wavelength, nm", "Intensity", "No spectrum yet. Connect to the
board in the row above." The axis labels are set from the accessor, then `Plot.Font.Automatic()` picks a font that
holds Cyrillic.

New palette keys, values from SemiPlot:

| Key | Light | Dark | Consumer |
| --- | --- | --- | --- |
| `AppAccentBrush` | `#3574F0` | `#3574F0` | the spectrum line |
| `AppAccentFillBrush` | `#3574F0`, `Opacity="0.25"` | `#3574F0`, `Opacity="0.25"` | the ±`delta` band |
| `AppMarkerLineBrush` | `#6F737A` | `#A8ADBD` | the centre line at the SCADA wavelength |
| `AppMinorGridLineBrush` | `#F4F5F7` | `#2B2D30` | minor gridlines, drawn on the log axis only |

ScottPlot surfaces painted by `ChartPalette.Apply`: figure background `AppPanelBackgroundBrush`, data background
`AppContentBackgroundBrush`, major grid `AppSubtleLineBrush`, minor grid `AppMinorGridLineBrush`, each axis's label,
tick labels, ticks and frame `AppSecondaryForegroundBrush`. `ChartPalette.Apply` then paints the two lines, the band
and the centre line through `SpectrumChartViewModel.ApplyPalette(ChartColors)`.

Data flow:
1. The link writes `ReceivedFrame` to its channel (session task).
2. The distributor reads it, sets `LatestSpectrum` for a spectrum frame, calls each subscriber's `OnNext` under its own
   try/catch (distributor task).
3. The live source passes spectrum frames on as `Changed` (distributor task).
4. `SpectrumViewModel` moves the signal to the UI scheduler, reads `Current`, drops a repeat of the same measurement,
   calls `Chart.Show` and sets `SignalInfo.Spectrum` (UI thread).
5. `Chart.Show` fills the buffer and the limits under `Plot.Sync` and calls `RequestRedraw()`.
6. The view serves one redraw in the next display frame.

## What Goes Where
- **Implementation Steps**: the package pin, the copied chart pieces, the distributor, the seam, the view models, the
  views, the layout, tests and docs in this repo.
- **Post-Completion**: the port-pair smoke run and the real-board check.

## Implementation Steps

### Task 1: ScottPlot pin and chart palette
**Files:**
- Modify: `SpectrumVisualizer/Directory.Packages.props`, `SpectrumVisualizer/SpectrumVisualizer.UI/SpectrumVisualizer.UI.csproj`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Styles/Palette.axaml`
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Chart/ChartPalette.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Chart/ChartPaletteTests.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/AppResourcesTests.cs`

- [x] pin `ScottPlot.Avalonia` 5.1.59 in `Directory.Packages.props` (alphabetical, between `RJCP.SerialPortStream` and
  `Semi.Avalonia`, `:24-25`) as in `SemiPlot/Directory.Packages.props:29`; add the `PackageReference` to the UI
  project (`SpectrumVisualizer.UI.csproj:17-33`)
- [x] add `AppAccentBrush`, `AppAccentFillBrush`, `AppMarkerLineBrush`, `AppMinorGridLineBrush` to both variants of
  `Palette.axaml` (after `:73` and `:137`) with the values of Technical Details
- [x] copy `ChartPalette.Apply` and `Resolve` (`SemiPlot/SemiPlot.UI/Chart/ChartPalette.cs:14-46`, `:57-65`) without
  the marker half; `Resolve` multiplies the alpha by `brush.Opacity`; add `Colors(IResourceHost, ThemeVariant)`
  returning a `ChartColors` record (line, band fill, centre line) for the plottables
- [x] write `ChartPaletteTests` (`Component=UI`, `Area=Chart`): the five surfaces on a real `Plot` equal the resolved
  keys under Light and Dark; `AppAccentFillBrush` resolves to alpha 64; a missing key throws naming the key
- [x] extend `AppResourcesTests` with the four new keys under both variants
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 2: Log axis and its tick generator
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.Core/Chart/LogAxis.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Chart/LogTickGenerator.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Core/Chart/LogAxisTests.cs`,
  `UI/Chart/LogTickGeneratorTests.cs`

- [x] copy `LogTick` and `LogAxis.Ticks` with their private helpers from `SemiPlot/SemiPlot.Core/Trends/LogAxis.cs`
  (drop `Project` and `AdmitsMinimum`) into Core, renaming the namespace
- [x] write `LogTickGenerator : ITickGenerator` from `SemiPlot/SemiPlot.UI/Chart/LogTickGenerator.cs:10-93` without
  `Mask`, `IsDrawn`, `IsLogarithmic`, the zero-label and the read-back rules: a major reads `10^position` as `"0.###"`
  under the current culture, a repeated label turns minor; a private tick cache holds the last two results keyed on the
  limits, the length and the culture
- [x] copy `SemiPlot/SemiPlot.Tests.Unit/Core/Trends/LogAxisTests.cs` for the copied members (`Component=Core`,
  `Area=Chart`)
- [x] write `LogTickGeneratorTests` (`Component=UI`, `Area=Chart`): `MajorsOverDecadesCarryTheValueAsAnIntegerCount`
  (0..5 decades at 400 px label "1", "10", ... "100000" under `CultureScope("en")` and `CultureScope("ru")`),
  `FractionalMajorsUseTheCultureDecimalSeparator`, `ACultureChangeRegeneratesTheCachedLabels`,
  `ARepeatedLabelTurnsMinor`, `TheSecondLengthOfAFrameReusesTheCachedTicks` (same array instance),
  `NothingForANonFiniteRange`
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 3: Frame distributor
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Link/FrameDistributor.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Link/FrameDistributorTests.cs`

- [x] implement `FrameDistributor` of Solution Overview: one public constructor over a `ChannelReader<ReceivedFrame>`;
  `LatestSpectrum`, `Frames` over the distributor's own copy-on-write subscriber list, `Start()`, `StopAsync()`,
  `Dispose()`; the loop calls each subscriber's `OnNext` inside its own try/catch, logs a throw at Error and reports it
  once with `TryReportFailure`
- [x] the loop logs at Debug when it starts and when it stops; cancellation ends it without a log at Error
- [x] write the `FrameDistributorTests` of Acceptance Evidence item 2 (`Component=UI`, `Area=Link`) over a real bounded
  channel with the link's options
- [x] write `AThrowingSubscriberDoesNotTakeTheFrameFromTheOthers` with a real `MessagePanelViewModel`: subscriber 1
  throws on frame 1, subscriber 2 receives that same frame 1, the panel holds one Error entry naming the exception
  type, and frame 2 reaches both subscribers
- [x] write `AnUnsubscribedObserverReceivesNothingMore`: dispose the subscription, write a frame, the observer's count
  stays
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 4: Displayed-spectrum seam and the live source
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/IDisplayedSpectrumSource.cs` (with `DisplayedSpectrum`)
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/LiveSpectrumSource.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Spectrum/LiveSpectrumSourceTests.cs`,
  `FakeDisplayedSpectrumSource.cs`

- [x] declare `DisplayedSpectrum(SpectrumMeasurement Measurement, DateTimeOffset ReceivedAt)` and
  `IDisplayedSpectrumSource` (`Current`, `Changed`) of Solution Overview; the reason for the interface lives in
  `overview.md`, not in a comment naming a class that does not exist yet
- [x] implement `LiveSpectrumSource(FrameDistributor)`: `Current` wraps `LatestSpectrum` in a new `DisplayedSpectrum`
  on every read, around the frame's own measurement; `Changed` from `Frames` filtered to spectrum frames
- [x] write `FakeDisplayedSpectrumSource` (settable `Current`, `Changed` from a private subject with a `Signal()`
  method) for the view model tests
- [x] write `LiveSpectrumSourceTests` (`Component=UI`, `Area=Spectrum`) of Acceptance Evidence item 3 over a
  distributor on a test channel; add `TwoReadsOfOneFrameCarryTheSameMeasurement`
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 5: Distributor and source in the composition root and the window lifecycle
**Files:**
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/UiServiceCollectionExtensions.cs`, `App.axaml.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Di/CompositionRootTests.cs`, `AppLifecycleTests.cs`

- [x] register `FrameDistributor` through a factory lambda that passes `GetRequiredService<SerialLink>().Frames`, the
  shape the link's own registration has (`UiServiceCollectionExtensions.cs:27-30`), and `IDisplayedSpectrumSource` as
  `LiveSpectrumSource`, both singletons after the link
- [x] add the distributor to `ConfiguredServices` (`App.axaml.cs:168-172`) and resolve it in `Configure` (`:141-148`);
  start it in the window's `Opened` beside the port refresh (`:70`)
- [x] stop it in the close path: `StopLinkAsync` (`:105-117`) becomes a stop of the link, then the distributor, both
  inside the one 5 s limit; rename it `StopBackgroundServicesAsync` and its log line names both
- [x] write tests: the distributor and the source resolve as one instance each (`CompositionRootTests`);
  `OpeningTheWindowStartsTheFrameDistributor` and `ClosingTheWindowStopsTheDistributorAfterTheLink` in the shape of the
  existing `AppLifecycleTests` (`AppLifecycleTests.cs:50-61`: a `FakePortStream` queued on `FakeSerialPortProvider`,
  then `link.Connect`). The first raises `Opened`, connects, feeds an encoded frame with `FakePortStream.PushBytes`
  (`FakePortStream.cs:51`) and asserts that a distributor subscriber receives it. The second closes the window, waits
  for the close to complete, and asserts that the log sink (`CollectingSink`, `AppLifecycleTests.cs:44`) holds the
  distributor's Debug stop line after the link's "The link on port {Port} stopped" line (`SerialLink.cs:107`). No
  test-only writer to the link's channel exists
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 6: Scale and projection rules
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.Core/Spectrum/SpectrumDisplay.cs`, `SpectrumScale.cs`,
  `SpectrumProjection.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Core/Spectrum/SpectrumScaleTests.cs`,
  `SpectrumProjectionTests.cs`

- [x] declare `SpectrumDisplay(bool IsInverted, bool IsLogarithmic)`, the display mode every rule below takes
- [x] implement `SpectrumProjection.Fill` of Technical Details as a single pass to find the maximum and a second to
  write, no LINQ (it runs per frame); it returns the raw maximum for the stick-to-zero rule and whether the frame is
  dark
- [x] implement `SpectrumScale` as static functions returning `SpectrumLimits` (an `AxisRange` for X and for Y):
  `Following(xs, ys, display)` and `StuckToZero(xs, rawMax, display)`, the rules of the scale table; the caller passes
  the reference's projected values, so the scale knows nothing about dark frames
- [x] write `SpectrumProjectionTests` and `SpectrumScaleTests` of Acceptance Evidence item 3 as table tests
  (`Component=Core`, `Area=Spectrum`), including the all-zero spectrum in each mode with `double.IsFinite` on every
  limit
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 7: Spectrum chart view model
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/SpectrumChartViewModel.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Spectrum/SpectrumChartViewModelTests.cs`,
  `SpectrumChartViewModelScaleTests.cs`, `SpectrumChartViewModelRenderTests.cs`, `SpectrumChartAssertions.cs`,
  `SpectrumSamples.cs`

- [x] implement `SpectrumChartViewModel` of Solution Overview: the `Plot`, two `SignalXY` lines built from
  `SpectrometerCalibration.For(device).WavelengthOf` (`SpectrometerCalibration.cs:51-54`), the band and the centre
  line, the axis labels from resx with `Plot.Font.Automatic()`, `Show(DisplayedSpectrum)`, `IsFollowing`,
  `TakeManualScale()`, `OnHeldMove(press, current, isButtonHeld)`, `ResetScale()`, `StickToZero()`,
  `SetDisplay(SpectrumDisplay)`, `ApplyPalette(ChartColors)`, `RequestRedraw()` and `RedrawRequested`, `HasSpectrum`;
  every scale change runs through one helper that takes `Plot.Sync`, raises `IsFollowing` after the lock and requests
  a redraw
- [x] every write to the plot (buffers, plottable visibility, limits, tick generator, minor grid width, colours through
  `ApplyPalette` and `ChartPalette.Apply`) happens inside `lock (Plot.Sync)`; each device line keeps its last
  non-dark measurement, and a dark frame projects its own device's again in the current mode as the reference of the
  Y limits; the band and the centre line are hidden when
  `WavelengthNm - Delta .. WavelengthNm + Delta` does not overlap the shown device's range; `RedrawRequested` follows SemiPlot (raises only while a view subscribes; `Dispose` completes it)
- [x] write `SpectrumSamples`: an ATP-2000 and an ATP-8000 `DisplayedSpectrum` with a peak, and a dark one per device
- [x] write the `SpectrumChartViewModelTests` and `SpectrumChartViewModelScaleTests` of Acceptance Evidence item 3
  (`[AvaloniaFact]`, `Area=Spectrum`),
  including `ShowNeverWritesTheFramesPixels` (a copy of the array equals it after `Show` with inversion and log on),
  `ABandOutsideTheDeviceRangeIsHiddenAndTheXLimitsStayTheDeviceRange`,
  `AFrameFromTheOtherDeviceWhileFrozenKeepsTheFrozenLimits` and `ApplyPaletteRunsUnderThePlotLock` (a second thread
  that holds `Plot.Sync` delays `ApplyPalette` until it releases)
- [x] write `SpectrumChartViewModelRenderTests`: a dark frame per axis type, then `Plot.RenderInMemory(640, 480)`
  throws nothing and `Plot.Axes.GetLimits()` (`ScottPlot.xml:272`) is finite on all four sides; the same for an inverted dark frame on log
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 8: Signal pane view model and texts
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/SignalInfoViewModel.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Spectrum/SignalInfoViewModelTests.cs`

- [x] add every new resx key of Technical Details in English and Russian; remove `ContentPlaceholder` from both sets
  only in Task 11, where its last reader goes
- [x] implement `SignalInfoViewModel(TimeProvider)` with `Spectrum` and the seven computed texts of Technical Details,
  each raised from the `Spectrum` setter
- [x] write `SignalInfoViewModelTests` of Acceptance Evidence item 3 under `CultureScope("en")` and
  `CultureScope("ru")` with a `FakeTimeProvider` in a fixed zone (`Microsoft.Extensions.Diagnostics.Testing` is
  referenced by the test project, `SpectrumVisualizer.Tests.Unit.csproj:10`); compare texts with the generated accessor, never literals
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 9: Spectrum view model
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/SpectrumViewModel.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Spectrum/SpectrumViewModelTests.cs`

- [x] implement `SpectrumViewModel(IDisplayedSpectrumSource, MessagePanelViewModel, TimeProvider, IScheduler
  uiScheduler, ILogger<SpectrumViewModel>)` of Solution Overview: builds `Chart` and `SignalInfo`, the latest-only
  pipeline with `ObserveOn` first, `IsInverted`, `IsLogarithmic`, `ResetScaleCommand`, `StickToZeroCommand`,
  `Dispose` for the subscription, the commands and both children
- [x] a throw from the `Current` read or from showing a spectrum goes to `TryReportFailure` and skips that spectrum;
  only a failure of `Changed` itself ends the subscription
- [x] write `SpectrumViewModelTests` of Acceptance Evidence item 3 over `FakeDisplayedSpectrumSource`: the three
  queued signals apply once (count `Chart.RedrawRequested` emissions with a subscribed observer), the spectrum at
  construction shows, `TheSameMeasurementIsAppliedOnceByReference` (two signals over one measurement apply once; a
  new measurement with equal values applies again), Stick to zero follows `HasSpectrum`,
  `ResetScaleShowsTheFrozenState` (disabled while following, enabled after `TakeManualScale()`, disabled after Reset),
  `TheCheckboxesReachTheChart`, `AThrowingShowReportsOnceAndTheNextSpectrumStillShows`,
  `AThrowingCurrentOnALaterSignalReportsAndThePipelineGoesOn`
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 10: Spectrum chart view
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/SpectrumChartView.axaml`, `SpectrumChartView.axaml.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Spectrum/SpectrumChartViewTests.cs`,
  `SpectrumChartViewInputTests.cs`, `SpectrumChartViewTestBase.cs`

- [x] load the `dev:desktop-ui` skill before writing the AXAML
- [x] lay out the view as `TrendChartView.axaml:54-81` without the cursor overlay: a `Panel` with the `PlotLayer`
  decorator (`BitmapCache`) holding `PlotControl` alone, and the empty-state `TextBlock` bound to `Chart.HasSpectrum`
  in `AppSecondaryForegroundBrush`
- [x] in code-behind copy `RequestFrame`, `ServeFrame`, `ClearPendingFrame`, `ServedFrameCount` (internal, read by
  the frame-pacing tests) and the theme repaint on `OnLoaded` and `ActualThemeVariantChanged` with its removal in
  `OnUnloaded` (`TrendChartView.axaml.cs:94`, `:102-148`); `OnDataContextChanged` caches the chart view model, binds
  `PlotControl.Reset(chart.Plot)` and subscribes `RedrawRequested`
- [x] read the default `UserActionResponses` of a fresh `AvaPlot` 5.1.59 through reflection (processor enabled; the
  XML documentation has no default list), in order: `MouseInteractWithPlottables`, `MouseDragZoomRectangle`,
  `MouseDragPan`, `MouseDragZoom`, `MouseWheelZoom`, `SingleClickAutoscale`, `SingleClickContextMenu`,
  `DoubleClickBenchmark`, `KeyboardPanAndZoom`, `KeyboardAutoscale`; remove `DoubleClickBenchmark`,
  `SingleClickAutoscale`, `SingleClickContextMenu`, `KeyboardAutoscale` and `KeyboardPanAndZoom`, keeping
  `MouseDragPan`, `MouseDragZoom`, `MouseDragZoomRectangle`, `MouseWheelZoom` and `MouseInteractWithPlottables` (it
  moves no axis)
- [x] add tunnel handlers on `PlotControl` that forward to the cached chart view model: `PointerWheelChanged` calls
  `TakeManualScale()`; `PointerPressed` records the press and `PointerReleased` clears it; `PointerMoved` passes the
  press, the current point and whether a button is held to `OnHeldMove`, which freezes from 5 px. `KeyDown` gets no
  handler, and a bare click moves no point, so both keep the scale following
- [x] write the `SpectrumChartViewTests` and `SpectrumChartViewInputTests` of Acceptance Evidence item 3
  (`[AvaloniaFact]`, `Area=Spectrum`) over `SpectrumChartViewTestBase`, copying
  the shape of `TrendChartViewTests.cs:171` and `:278`; the pointer tests raise headless mouse events on the realised
  view (`MouseDown`, `MouseMove`, `MouseUp`, `MouseWheel` of the headless window) and read `Chart.IsFollowing`
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 11: Main window content: chart pane and signal pane
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/SpectrumPanel.axaml`, `SpectrumPanel.axaml.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/MainWindow/MainWindow.axaml`, `MainWindowViewModel.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/UiServiceCollectionExtensions.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/MainWindow/MainWindowViewTests.cs`,
  `MainWindowViewModelTests.cs`, `MainWindowLayoutTestBase.cs`, `UI/Di/CompositionRootTests.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/MainWindow/MainWindowSignalPaneTests.cs`,
  `MainWindowMessagePanelTests.cs`

- [x] write `SpectrumPanel.axaml` (`x:DataType` `SpectrumViewModel`): the form grid of Solution Overview, every inset
  from the spacing table, values `NoWrap`, the pane width as `SpectrumPanel.PaneWidth` (a `const double`; a const
  named `Width` would hide `Control.Width`), first set from the formula with an estimate of 352 and fixed in Task 12,
  applied to the `SignalPane` border that carries the padding
- [x] replace `ContentArea` (`MainWindow.axaml:33-42`) with a `Grid ColumnDefinitions="*,Auto"`: `SpectrumPane`
  holding `SpectrumChartView` bound to `Spectrum.Chart`, and `SignalPane` holding `SpectrumPanel` bound to `Spectrum`
- [x] add `SpectrumViewModel Spectrum` to `MainWindowViewModel` (`MainWindowViewModel.cs:23-39`); register
  `SpectrumViewModel` in `AddUi` (`UiServiceCollectionExtensions.cs:31-34`); remove `ContentPlaceholder` from both
  resx sets
- [x] update `MainWindowViewTests` (`:69-80` placeholder test becomes the content-row test; `:35` construction) and
  `MainWindowViewModelTests`; add `TheSignalPaneRealisesEveryRowAndControl` and `ACheckboxReachesTheChart` to
  `MainWindowSignalPaneTests`, and move the message panel tests to `MainWindowMessagePanelTests`, both over
  `MainWindowLayoutTestBase`, which holds the pane's part names and inset constants
- [x] `CompositionRootTests.EveryRegisteredServiceResolves` covers the new view model; add
  `TheMainWindowShowsTheContainersSpectrumViewModel`
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 12: Desktop UI pre-handover checklist
**Files:**
- Modify (only to fix a miss): `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/SpectrumPanel.axaml`,
  `SpectrumChartView.axaml`, `MainWindow/MainWindow.axaml`, the resx pair
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/MainWindow/MainWindow.axaml.cs` (`MinimumWidth`),
  `Spectrum/SpectrumPanel.axaml.cs` (`PaneWidth`)
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/MainWindow/MainWindowLayoutTests.cs`,
  `MainWindowSignalPaneTests.cs`, `MainWindowTextFitTests.cs`

- [x] every gap and inset in the signal pane and the content row reads off the spacing table; the `ui-theme.md:67` grep
  prints nothing (pane padding 16; label to value 12; row spacing 8; section header 16 above, 8 below, the first 0).
  Measured by `MainWindowSignalPaneTests.TheSignalPaneInsetsReadOffTheSpacingTable` on the arranged grid cells: form origin
  (1 + 16, 16), first header cell top 0, every row gap 8, `ChartHeader` 16 below the last signal row, one label edge and
  one field edge 12 after the label column. It caught a miss: Semi's buttons centre in the field column (Reset scale at
  +34 px, Stick to zero at +48 px in the headless run); both now carry `HorizontalAlignment="Left"`. Inset grep: no output
- [x] every field width reads off the width table or carries a stated reason: the value column 120 (numbers); the
  field column widens only to the widest checkbox or button, measured; record both measured widths per language.
  Widest value: en 92.0 px, ru 92.1 px, under 120. Controls (Invert / Log / Reset / Stick): en 129.4 / 140.6 / 102.1 /
  109.4 px, ru 182.7 / 198.9 / 157.9 / 140.5 px, so the field column is en 140.6 px, ru 198.9 px ("Логарифмическая
  шкала"); the reason for the width above 120 is that checkbox
- [x] measure through `RenderedText` in English and Russian: the label column (widest label), the field column, the
  longest values ("-1.23457E-38", "±100 nm", "23:59:59.999", "ATP-8000"); set `SpectrumPanel.PaneWidth` to 16 + label +
  12 + field + 16 + 24 rounded up to 8; record the numbers in this checkbox.
  `MainWindowTextFitTests.TheSignalPaneHoldsItsWidestLabelValueAndControlOnOneLineWithSlack`, faces Inter plus Microsoft
  YaHei, Segoe UI, Arial at 14 px. Values are the type maxima under the culture: `-1.23457E-38` / `-1,23457E-38`,
  "65535 nm", "±255 nm" (`Delta` is a byte, so 255 bounds the plan's 100), "23:59:59.999", both device names. Labels:
  en 91.0 px, ru 93.0 px. Bold headers: en 43.2 px, ru 54.5 px (fit both columns). The formula adds the 1 px left
  border of `SignalPane`: en 1 + 16 + 91.0 + 12 + 140.6 + 16 + 24 = 300.6, ru 1 + 16 + 93.0 + 12 + 198.9 + 16 + 24 =
  360.9, rounded 368; the estimate 352 failed by 16 px in Russian
- ➕ review: the Russian window label became "Окно измерения" (117.5 px); ru 1 + 16 + 117.5 + 12 + 198.9 + 16 + 24 =
  385.4, rounded 392, so `SpectrumPanel.PaneWidth` = 392 and the content floor is 392 + 400 = 792; `MinimumWidth`
  stays 1048 and the chart keeps 656 px at it
- [x] measure the chart floor: an ATP-8000 spectrum on a 400 px wide `RenderInMemory` keeps at least four labelled
  ticks on `Axes.Bottom`; set `MainWindow.MinimumWidth` to the larger of the status bar's 1042.4 px Russian width and
  `SpectrumPanel.PaneWidth` + 400, rounded up to 8; record the result.
  `AnAtp8000SpectrumOnTheChartFloorKeepsFourLabelledWavelengthTicks`: 8 labelled ticks at 400 px (1 000 to 2 400 by
  200). max(1042.4, 392 + 400 = 792) = 1042.4, rounded 1048: `MinimumWidth` stays 1048, its summary names both bounds
- [x] at the window's minimum width the pane shows every row and control and the chart keeps at least 400 px; at
  2560 px the pane keeps its width and left-aligned content, and only the chart grows.
  `AtTheMinimumWidthTheSignalPaneShowsEveryRowAndTheChartKeepsItsFloor` (window 1048 x 800 with the message panel
  shown: pane 392 px, chart 656 px, every part's bottom inside the pane padding) and
  `AtALargeWidthTheSignalPaneKeepsItsWidthAndContentAndOnlyTheChartGrows` (pane 392 px ending at 2560, the chart
  grows by exactly 1280 px, every pane part keeps its offset in the pane)
- [x] every label, value, checkbox and button fits on one line in English and Russian. The NoWrap check caught a
  miss: Semi's `CheckBox` template wraps string content (`TextWrapping="Wrap"` on its `ContentPresenter`, template
  priority, so a style cannot override it); both checkboxes now hold a `TextBlock` that the pane's NoWrap style reaches.
  The widths are the ones recorded above
- [x] every state keeps the window size and the pane bounds: no spectrum, ATP-2000, ATP-8000, dark, log on, inverted,
  frozen scale, the message panel shown; the `ui-theme.md:80` grep prints nothing.
  `EverySpectrumStateKeepsTheWindowAndThePaneBounds`: the seven spectrum states give equal window size and pane and
  chart bounds. The message panel row cannot keep the pane height literally: it takes its height from the content row,
  so the test asserts the window size, both panes' width and left unchanged and both heights lower by exactly the
  message panel's height. Colour grep: no output
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task (final count in Task 13)

### Task 13: Verify acceptance criteria
- [x] run every command of Acceptance Evidence and record each result with its count (per Area, full suite, none
  skipped; Release build 0 warnings; format and terse exit 0; old solution 13 of 13; both greps print nothing).
  At HEAD: Area=Chart 50, Area=Link 76, Area=Spectrum 110, Area=MainWindow 66, Area=Di 21, Area=Resources 9, all
  passed, 0 skipped; full suite 505 of 505, including the two tests below. Release build 0 warnings, 0 errors;
  `dotnet format --verify-no-changes` and `dotnet terse SpectrumVisualizer` exit 0; both `ui-theme.md` greps print
  nothing. Acceptance item 5 had a gap: no test showed that a subscriber gets nothing after the close.
  `AppLifecycleTests.AfterTheCloseADistributorSubscriberReceivesNothingMore` closes it with no test-only writer: a
  subscriber receives a frame, the window closes, the link connects again on a second `FakePortStream` and writes a
  frame, the test reads that frame back from `SerialLink.Frames` (so the distributor never took it) and the subscriber
  still holds one frame. With the distributor's stop removed from the close path the test fails
- [x] verify every Overview item maps to a named passing test: the line over wavelength, the band, the pane values,
  inversion, the log axis and its floor, reset, stick to zero, the distributor as the one reader, the seam, the
  frame-paced redraw and the cache. Line over wavelength: `TheFirstSpectrumSetsTheXLimitsToTheDeviceRange`,
  `AnAtp2000FrameAfterAnAtp8000FrameShowsTheAtp2000LineOnly`; band: `TheWindowBandSpansTheDeltaAroundTheWavelength`;
  pane values: `ASpectrumShowsTheBoardValuesUnderTheCurrentCulture`, `TheUpdateTimeIsTheReceivedTimeInTheLocalZone`,
  `EveryRowReadsNoValueBeforeASpectrum`; inversion and floor: `FillInvertsOverTheFrameMaximumThenTakesTheFlooredLog`;
  log axis: `TheLogAxisRunsInDecades`, `TheLogAxisCarriesALogTickGeneratorAndMinorGridlinesAndTheLinearAxisNeither`;
  reset: `ResetScaleReturnsToFollowing`, `ResetScaleShowsTheFrozenState`; stick to zero:
  `StickToZeroSetsTheBottomToZeroAndFreezes`, `StuckToZeroRunsFromZeroToTheHeadroomOverTheRawMaximum`; distributor:
  `EveryFrameReachesEverySubscriberInOrder`, `OpeningTheWindowStartsTheFrameDistributor`; seam:
  `ASpectrumFrameUpdatesCurrentAndSignalsChanged`, `TwoReadsOfOneFrameCarryTheSameMeasurement`; redraw:
  `ABurstOfRedrawRequestsJoinsOneFrame`; cache: `ThePlotIsCachedThroughItsOnlyAncestor`
- [x] verify edge cases and their expected outcomes: a dark frame while following keeps the last non-dark Y limits
  and X follows its device, and with no earlier spectrum takes the flat-range limits, on both axis types, inverted and
  not; a device change while following moves X to the new device's range; a device change while frozen keeps the
  frozen limits, and the plot may stay blank until Reset scale; a band outside the device range is not drawn while the
  pane shows the wavelength; `delta` 0 draws the centre line only; a bare click keeps following; a heartbeat-only
  stream keeps the last spectrum; a throwing subscriber leaves the frame to the others and is reported once.
  The kept Y limits were tested on the linear axis only;
  `ADarkFrameKeepsTheLastNonDarkYLimitsOfTheSameDeviceOnEveryDisplay` now covers all four combinations of log and
  inversion, and `ADarkFirstFrameAfterADeviceSwitchTakesTheFlatRange` and
  `ADarkFrameAfterTheOtherDeviceKeepsTheLimitsOfItsOwnDevicesLastPeak` the device switch. The flat range with no earlier
  spectrum: the three render tests (both axes; an inverted dark frame is all zero, as
  `FillInvertsOverTheFrameMaximumThenTakesTheFlooredLog` shows). Device change:
  `AnAtp2000FrameAfterAnAtp8000FrameShowsTheAtp2000LineOnly`, `AFrameFromTheOtherDeviceWhileFrozenKeepsTheFrozenLimits`;
  band: `ABandOutsideTheDeviceRangeIsHiddenAndTheXLimitsStayTheDeviceRange`; delta 0: `ADeltaOfZeroDrawsTheCentreLineOnly`;
  bare click: `APressAndReleaseWithoutAMoveKeepsFollowing`; heartbeats: `TheLatestSpectrumSkipsHeartbeats`,
  `AHeartbeatChangesNeitherCurrentNorChanged`; throwing subscriber: `AThrowingSubscriberDoesNotTakeTheFrameFromTheOthers`
- [x] run the full suite: `dotnet test SpectrumVisualizer.slnx`; confirm the old solution builds and passes 13 of 13.
  505 of 505; `dotnet build SpectrumVisualizer.sln` 0 errors (its warnings are not gated; this slice changes no file
  of the old app), `dotnet test SpectrumVisualizer.Tests` 13 of 13
- [x] start the app with `dotnet run --project SpectrumVisualizer/SpectrumVisualizer.UI` and confirm the window shows
  the empty-state line and the log gains its start line; leave the port-pair checklist to the owner.
  The app ran 25 s under `timeout` with no exception; the log gained "SpectrumVisualizer starting; log folder ...".
  The empty-state line could not be seen: the workstation was locked, and the screenshot shows the lock screen. The
  headless `TheEmptyStateShowsUntilTheFirstSpectrum` covers it. The port-pair checklist (com0com, real board) is
  skipped as not automatable and stays with the owner

### Task 14: [Final] Update documentation
- [x] `docs/architecture/overview.md`: the purpose paragraph (`:5-11`, "No code reads the decoded frames" at `:10` is
  no longer true); the Stack list (`:13-25`) gains ScottPlot.Avalonia 5.1.59; the components table (`:29-61`) gains
  `FrameDistributor`, `DisplayedSpectrum`, `IDisplayedSpectrumSource`, `LiveSpectrumSource`, `SpectrumProjection`,
  `SpectrumScale`, `SpectrumChartViewModel`, `SignalInfoViewModel`, `SpectrumViewModel`, `ChartPalette`, `LogAxis`,
  `LogTickGenerator`; startup (`Opened` starts the distributor, `Closing` stops the link, then the distributor); the
  main window rows (`:103-111`, the content row) and the minimum width paragraph (`:116-122`); a "Spectrum" section:
  data flow, latest-only, the scale rules, the log floor, inversion, the empty state, the stock input
- [x] `docs/architecture/board-protocol.md`: "Frames out" (`:283-288`): the distributor is the one reader and forwards
  every frame; the calibration window paragraph (`:317-320`) notes that the chart's band is the app's nm window
- [x] `docs/architecture/README.md`: the Chart renderer row (`:32`) names ScottPlot.Avalonia 5.1.59, the cache wrapper
  and the frame-paced redraw; a Frame distribution row (one reader, latest spectrum, subscribers own their scheduling)
- [x] `docs/architecture/ui-theme.md`: the four keys in the key table (`:44-55`) and a "The plot" section (the surfaces,
  the alpha times `Opacity` rule, the repaint on load and variant change)
- [x] `docs/architecture/ui-text.md`: the key owners (`:39-46`): Signal pane, Chart controls, Spectrum chart; remove
  `ContentPlaceholder` (`:41`); `StatusNoValue` gains the signal pane as a second reader
- [x] `docs/architecture/testing-strategy.md`: the Area list (`:20-22`) gains `Chart` and `Spectrum`; rows for Chart
  (log axis, tick generator, palette), Spectrum (rules, view models, render tests, chart view) and the distributor in
  the Link row; the Layout row covers the signal pane
- [x] `readme.md`: replace "The new app has no spectrum chart and no storage, so the old app is still the one that
  shows the spectrum" (`:16-17`) with what the spectrum screen shows
- [x] `CLAUDE.md`: add `Chart` and `Spectrum` to the Area list (`:40-41`); the Layout line for `SpectrumVisualizer.UI/`
  names the spectrum chart

## Post-Completion
**Manual verification:**
- the smoke checklist of Acceptance Evidence over a com0com pair, during the owner's final assembly check.
- the real board after the firmware-frame-sender slice: the same checklist against the board instead of the emulator,
  with an eye on how often the board alternates spectrometers. ASSUMPTION: the board measures with one spectrometer at a
  time, chosen by the SCADA wavelength, so consecutive spectra come from one device except at a switch; if it
  alternates, the following scale jumps between the two X ranges and the owner decides on a device choice.

**Remaining slices:**
- spectrum-store: SQLite storage of every received spectrum, fed from the frame distributor through its own queue,
  with 24 h retention and the intensity-series query.
- intensity-trend: the intensity-over-time chart at the SCADA wavelength with SemiPlot trend behavior, and a click
  that shows a past spectrum through a historical displayed-spectrum source.
- firmware-frame-sender: the OptoskyProj#20 frame sender in the board firmware, verified on the board against the app.
- release-3-0: the 3.0.0 installer over the self-contained publish folder, the release workflow, and removal of the
  old app.

**Executed by exec:**
- branch: spectrum-view

Run note: 25 commits on `spectrum-view` (`6efcc6b`..`d0ac7bb`) before this record, stacked on `serial-link` at
`5fca2ab`; two of them add and revise this plan. Review phase 1 (5 agents) found 0 critical, 6 major and about 27
minor findings, all fixed in one fix iteration (`9c10e54`, `db4dbc3`, `27af489`, `02351e6`, `f02ec40`); its critical
re-check found the code clean. Phase 2 (smells) fixed 9 minor and 3 carried findings in `a5e58c2`, `b9c4d2a` and
`a216459`. Phase 3 (codex) was skipped because codex is not installed. Phase 3b (comments) fixed 12 minor comment
findings in `d0ac7bb`. Phase 4 (critical) found the code clean at `d0ac7bb`. Final state: 505/505 tests in
`SpectrumVisualizer.slnx`, 13/13 in `SpectrumVisualizer.sln`, Release build 0 warnings, format and terse clean. The
manual smoke checklist was not run: this machine has no com0com pair, and the workstation was locked during the app
start check, so the empty-state line was confirmed by the headless test only.

**Decisions made autonomously:**
- On the log axis a dark frame while following draws below the kept Y range, so the data area is empty for that
  frame; accepted, because the axis stays still and the pane shows the values.
- `ChartColors` lives in `ChartPalette.cs`, its only producer. `Resolve` takes alpha = round(A x clamp(Opacity, 0, 1)),
  so 0.25 gives 64.
- `LogAxis.Project` and `LogTickGenerator.IsLogarithmic` were deleted (no reader); `TickCache` is folded into
  `LogTickGenerator` with the culture in its key. Copied `LogAxisTests` lose their underscores; culture tests use
  `CultureScope`.
- `FrameDistributor.Frames` is a private `IObservable` over the subscriber list, not `Observable.Create`: Create's
  `AutoDetachObserver` unsubscribes an observer whose `OnNext` throws (measured). A subscriber throw is logged once
  through `TryReportFailure`; the per-frame snapshot is read under the lock that replaces it.
- `StopBackgroundServicesAsync` catches every failure, and `StopLinkThenDistributorAsync` stops the distributor in
  `finally`, so the window closes after a failed stop. No test covers a faulting stop: no fake makes `DisconnectAsync`
  throw.
- Every close after `Opened` is held until the distributor's read task ends
  (`WithAnIdleLinkTheWindowClosesOnceTheDistributorStops`). `CollectingSink` is thread-safe and the lifecycle logger is
  verbose, because the distributor's stop line is Debug.
- `AfterTheCloseADistributorSubscriberReceivesNothingMore` reconnects the link on a second `FakePortStream` and reads the
  late frame back from `SerialLink.Frames`; no test-only writer exists. `StopBeforeStartCompletesAtOnce` uses a
  counting `ChannelReader` and a 200 ms bounded wait.
- The test helper `ProtocolSamples.Spectrum` became `SpectrumFrame`: the namespace `Tests.Unit.UI.Spectrum` shadowed it
  (CS0118).
- `LiveSpectrumSource.Current` builds a new `DisplayedSpectrum` on every read, with no cache.
- `SpectrumProjection.Fill` returns `ProjectedSpectrum(RawMax, IsDark)`: darkness means every raw pixel is zero, which
  the projected values cannot show. `SpectrumScale.Following(xs, ys, logarithmic)` and
  `StuckToZero(xs, rawMax, logarithmic)` return the ScottPlot-free `SpectrumLimits(AxisRange X, AxisRange Y)`.
- `LogAxis` sits in `Core/Chart`; `SpectrumDisplay`, `SpectrumProjection` and `SpectrumScale` in `Core/Spectrum`; all
  public, because Core has no `InternalsVisibleTo`.
- The dark-frame reference is per device (`DeviceLine.LastNonDark`); a dark frame re-projects it in the current mode
  into a scratch buffer. A non-dark flat frame takes the flat range.
- A `delta` of 0 hides the band and draws the centre line only.
- One `ChangeScale` helper runs every scale change, `TakeManualScale` included: it takes `Plot.Sync`, raises
  `IsFollowing` after the lock and requests one redraw. `RequestRedraw` is private; no `ObjectDisposedException` guards
  and no `_isDisposed` flag.
- The view takes `Plot.Sync` once around `ChartPalette.Apply` and `ApplyPalette`. `PlotControl.Reset` is skipped for
  the same plot and for a null `DataContext`; a switch from chart A to chart B still disposes A's plot, because
  `AvaPlot.Reset` always does.
- `OnHeldMove(press, current, isButtonHeld)` freezes at 5 px, the larger of the X and Y distances, for every button.
  ScottPlot 5.1.59's `MouseDragZoom` has no floor, so a right drag under 5 px zooms a little and the next frame
  restores it. Nothing freezes before the first spectrum.
- The default `UserActionResponses` were read through reflection; `MouseInteractWithPlottables` stays. The headless
  drag test passes `RawInputModifiers.LeftMouseButton` on the move.
- A throw from the `Current` read or from `Chart.Show` is reported through `TryReportFailure` and skips that spectrum;
  only a failure of `Changed` ends the subscription. The static Serilog handler in the chart view was removed.
  `ThreeQueuedSignalsApplyTheLatestSpectrumOnce` blocks on a `ManualResetEventSlim` after one flake.
- `SignalInfoViewModel.Spectrum` compares by reference; the update time formats `HH:mm:ss.fff` with the invariant
  culture, numbers `G6` with the current one. A private `TimeProvider` subclass replaces `FakeTimeProvider`, which the
  referenced package lacks.
- `SpectrumViewModel` disposes its subscriptions, both commands and `Chart`; `MainWindowViewModel` leaves the injected
  `SpectrumViewModel` to the container.
- The const is `SpectrumPanel.PaneWidth`, 392 after the Russian label "Окно измерения"; the formula adds the 1 px left
  border, and the fit tests assert `PaneWidth` is at least the measured need. Widest values use type maxima
  (65535 nm, ±255 nm). `MainWindow.MinimumWidth` stays 1048.
- Checkboxes hold a `TextBlock`, because Semi's template wraps string content at template priority; both buttons carry
  `HorizontalAlignment="Left"`. With the message panel shown, both pane heights shrink by its height.
- Test classes were split under the size cap: `MainWindowSignalPaneTests` and `MainWindowMessagePanelTests` from
  `MainWindowViewTests`; `SpectrumChartViewTests` and `SpectrumChartViewInputTests` over `SpectrumChartViewTestBase`;
  `SpectrumChartViewModelTests` and `SpectrumChartViewModelScaleTests` with `SpectrumChartAssertions`;
  `FrameReaderDecodeBufferedTests` from `FrameReaderTests`.
- The Context section keeps the SemiPlot source names (`ReportFailureOf`, `LogAxis.Project`, `TickCache`): it
  describes SemiPlot, not this repo.

## Verify it yourself

Automated, from the repo root on branch `spectrum-view`:

1. `dotnet build SpectrumVisualizer.slnx -c Release` gives 0 warnings, 0 errors.
2. `dotnet test SpectrumVisualizer.slnx` passes 505 of 505. Per area with `--filter "Area=X"`: Spectrum 110, Link 76,
   MainWindow 66, Protocol 56, Emulator 56, Chart 50, Messages 32, Di 21, Calibration 15, Resources 9, Logging 7,
   Paths 7.
3. `dotnet format SpectrumVisualizer.slnx --verify-no-changes`, then `dotnet tool restore` and
   `dotnet terse SpectrumVisualizer`; both gates exit 0.
4. `dotnet build SpectrumVisualizer.sln` and `dotnet test SpectrumVisualizer.Tests` build the old app and pass 13 of 13.
5. The chart behaviour is pinned by these test classes:
   - `SpectrumScaleTests`, `SpectrumProjectionTests`: padding, flat ranges, Stick to zero, inversion, the log floor,
     finite limits for an all-zero spectrum.
   - `SpectrumChartViewModelTests`: device X ranges, the band and the centre line, the log tick generator, pixels
     never written, the palette under the plot lock.
   - `SpectrumChartViewModelScaleTests`: following and frozen, the 5 px held move, Reset scale, Stick to zero, the
     per-device dark-frame reference.
   - `SpectrumChartViewModelRenderTests`: dark frames render on both axes with finite limits.
   - `SpectrumChartViewTests`, `SpectrumChartViewInputTests`: the cache wrapper, the frame-paced redraw, the empty
     state, the removed ScottPlot responses, wheel, click and drag.
   - `LogAxisTests`, `LogTickGeneratorTests`, `ChartPaletteTests`: ticks, culture labels, the palette surfaces.
   - `MainWindowSignalPaneTests`, `MainWindowTextFitTests`: the 392 px pane and the 1048 px minimum width in English
     and Russian.

By hand: run the manual smoke checklist in Acceptance Evidence over the com0com pair. The highest-value checks, with
the emulator on `COM10` and the app on `COM11`:

1. Step 4 and step 5, `--dark-every 3` on the linear axis, then with "Logarithmic scale" ticked: the Y axis keeps the
   previous spectrum's limits on every dark frame; on the log axis the dark line lies below the kept range.
2. Step 7, a click and a 1 to 4 px move keep the plot following and Reset scale disabled; a drag of 5 px or more
   freezes the limits.
3. Step 4 and step 7, Stick to zero, also during a dark frame: Y runs from 0 to about 1.2 times the last peak and
   stays there until Reset scale.
4. Step 3 and step 8, restart the emulator between `--device 8000` and `--device 2000`: X follows each device's
   range while following, and a frozen scale keeps its limits.
5. Step 11, resize the window to its 1048 px minimum and to full screen: the signal pane keeps 392 px and every text
   stays on one line.
