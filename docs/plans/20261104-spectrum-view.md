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
- Roadmap: docs/plans/roadmaps/20261104-avalonia-rewrite-roadmap.md — slice spectrum-view
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
  realised window. Traits per class: `Component=UI`, `Area=Chart` for `UI/Chart/`; `Area=Spectrum` for
  `UI/Spectrum/`; `Area=Link` for the distributor; `Area=MainWindow` for the window rows and the layout; `Area=Di` for
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
   - `LogAxisTests` copied from SemiPlot: tick families, minors, the bounds that return nothing.
   - `LogTickGeneratorTests.MajorsCarryTheValueAsAnIntegerCount`, `ARepeatedLabelTurnsMinor`,
     `TheSecondLengthOfAFrameReusesTheCachedTicks`.
   - `ChartPaletteTests.ApplyPaintsTheFiveSurfacesFromThePaletteUnderBothVariants` and
     `ATranslucentKeyKeepsItsOpacityInTheAlpha` (`AppAccentFillBrush` resolves to alpha 64).
2. `dotnet test SpectrumVisualizer.slnx --filter "Area=Link"` passes, including the existing link tests and
   `FrameDistributorTests`: `EveryFrameReachesEverySubscriberInOrder`, `ALateSubscriberSeesOnlyLaterFrames`,
   `TheLatestSpectrumSkipsHeartbeats`, `AThrowingSubscriberDoesNotTakeTheFrameFromTheOthers` (subscriber 1 throws on a
   frame, subscriber 2 receives that same frame, the panel gets one Error entry),
   `AnUnsubscribedObserverReceivesNothingMore`, `StopEndsTheReadLoopAndStartAfterStopDoesNothing`,
   `StopBeforeStartCompletesAtOnce`.
3. `dotnet test SpectrumVisualizer.slnx --filter "Area=Spectrum"` passes, including:
   - `SpectrumScaleTests`: auto limits pad 5 % of the span; with no earlier non-dark frame a flat linear range gives `v - 1 .. v + 1` and a flat log
     range gives `v - 0.5 .. v + 0.5` decades; stick to zero gives `0 .. max x 1.2` linear and
     `0 .. log10(max x 1.2)` decades; an all-zero spectrum gives finite limits in every mode;
     `ADarkFrameWhileFollowingKeepsTheLastNonDarkYLimits` (Y equals the previous spectrum's limits, X is the dark
     frame's device range); `ADarkFrameWithNoEarlierSpectrumTakesTheFlatRangeLimits`.
   - `SpectrumProjectionTests`: inversion writes `max - value`; the log projection writes `log10(max(value, 1))`, so a
     zero pixel lands on 0 decades; an all-zero spectrum inverted stays all zero; a 65535 pixel gives 4.8165 decades.
   - `LiveSpectrumSourceTests`: `Current` is null before a spectrum; a heartbeat changes neither `Current` nor
     `Changed`; a spectrum frame updates `Current` with its `ReceivedAt` and signals `Changed`.
   - `SpectrumChartViewModelTests`: the first spectrum sets the X limits to the device's wavelength range
     (`WavelengthOf(0)` to `WavelengthOf(P - 1)`); the window band spans `WavelengthNm - Delta` to
     `WavelengthNm + Delta` and the centre line sits at `WavelengthNm`; an ATP-2000 frame after an ATP-8000 frame
     shows the ATP-2000 line only and moves the auto X limits; a wheel step freezes the limits across later frames,
     and so does the first pointer move with a button held; `AClickWithoutADragKeepsFollowing`; Reset scale returns
     to following; `ABandOutsideTheDeviceRangeIsHiddenAndTheXLimitsStayTheDeviceRange` (an ATP-8000 frame at
     400 nm: band and centre line invisible, X `WavelengthOf(0)..WavelengthOf(511)` of ATP-8000);
     `AFrameFromTheOtherDeviceWhileFrozenKeepsTheFrozenLimits` (the line may lie outside them; accepted); Stick to zero sets the bottom to zero and freezes; toggling log or inversion
     returns to following; the log axis carries a `LogTickGenerator` and 1 px minor gridlines, the linear axis
     neither; the frame's pixel array is never written; `ApplyPaletteRunsUnderThePlotLock`.
   - `SpectrumChartViewModelRenderTests.ADarkFrameRendersOnTheLinearAxisWithFiniteLimits` and
     `ADarkFrameRendersOnTheLogAxisAtTheFloorWithFiniteLimits`: `Plot.RenderInMemory(640, 480)` throws nothing and
     every axis limit is finite; `AnInvertedDarkFrameOnTheLogAxisRenders`.
   - `SignalInfoViewModelTests`: "-" in every row before a spectrum; the device name, `"{0} nm"`, `"±{0} nm"`,
     amplitude, SNR and Q in `G6` under the current culture, a non-finite value as "-", the update time as
     `HH:mm:ss.fff` in the `TimeProvider`'s local zone.
   - `SpectrumViewModelTests`: three `Changed` signals before the UI scheduler runs apply the latest spectrum once;
     `TheSameCachedSpectrumIsAppliedOnceByReference`; a spectrum present at construction shows at once; Stick to zero
     is enabled once a spectrum is shown; Reset scale is enabled only with a spectrum and a frozen scale
     (`ResetScaleShowsTheFrozenState`).
   - `SpectrumChartViewTests`: `ThePlotIsCachedThroughItsOnlyAncestor`, `ABurstOfRedrawRequestsJoinsOneFrame`,
     `ARequestOnADetachedViewServesNothing`, `TheEmptyStateShowsUntilTheFirstSpectrum`,
     `ALoadedViewRepaintsThePlotWhenTheVariantChanges`, `OnlyPanZoomAndWheelResponsesRemain` (the list holds no
     `DoubleClickBenchmark`, `SingleClickAutoscale`, `SingleClickContextMenu`, `KeyboardAutoscale` or
     `KeyboardPanAndZoom`), `AWheelStepFreezesTheScale`, `APressAndReleaseWithoutAMoveKeepsFollowing`,
     `AMoveWithAButtonHeldFreezesTheScale`.
4. `dotnet test SpectrumVisualizer.slnx --filter "Area=MainWindow"` passes, including `MainWindowViewTests` (rows top
   to bottom: menu, connection row, content, message panel, status bar; the content row holds the chart pane and the
   signal pane; every signal row and chart control realised; no placeholder), `MainWindowLayoutTests` (the signal
   pane keeps its width from the minimum to 2560 px; the chart takes the spare width and height; every spectrum state
   keeps the window and the pane bounds) and `MainWindowTextFitTests` (every pane label, value and control on one line
   in English and Russian; the minimum width holds the status bar, and the pane plus the chart floor).
5. `dotnet test SpectrumVisualizer.slnx --filter "Area=Di"` passes, including: the distributor, the live source and the
   spectrum view model resolve as singletons; `AppLifecycleTests.OpeningTheWindowStartsTheFrameDistributor` (after
   `Opened` and Connect over `FakeSerialPortProvider`, frame bytes fed through `FakePortStream` reach a distributor
   subscriber); `AppLifecycleTests.ClosingTheWindowStopsTheDistributorAfterTheLink` (after the close completes, the
   distributor's stop line is in the log and no subscriber receives anything more).
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
   Stick to zero are disabled; both checkboxes are clear.
2. Run `emu --port COM10`, select `COM11`, Connect. Within 2 s the plot shows an ATP-8000 spectrum: a peak at
   1550 nm reaching about 31000, 30000 over a floor of 1000 (`board-protocol.md:336-337`,
   `SpectrumGenerator.cs:9-10`, `:20-21`), X from about 895 to 2461 nm, and a band
   from 1540 to 1560 nm with a centre line at 1550 nm. The pane reads ATP-8000, 1550 nm, ±10 nm, 30000, 30, 0, and
   the update time advances once a second.
3. Restart the emulator with `--device 2000 --wavelength 400 --delta 25`. The X range moves to about 186 to 1029 nm,
   the peak and the centre line sit at 400 nm, the band spans 375 to 425 nm, the pane reads ATP-2000, 400 nm, ±25 nm.
4. Restart it with `--device 2000 --wavelength 400 --delta 25 --dark-every 3`. Every third spectrum is a flat line at
   0 with amplitude 0 and SNR 0; the Y axis keeps the limits of the previous spectrum, so the axis does not jump; X
   stays the ATP-2000 range; the message panel gets no entry and the log no exception.
5. Tick "Logarithmic scale". The Y axis labels powers of ten with minor gridlines; the floor of 1000 sits at 10^3,
   the peak near 10^4.5. A dark spectrum keeps those limits; its line lies on the 10^0 floor, below the kept range,
   so the data area is empty for that frame and the axis does not move. Untick it: the linear axis returns and the
   plot follows the data again.
6. Tick "Invert spectrum". The peak turns into a dip to 0 at 400 nm and the floor reads about 30000. With the log scale on
   as well, the dip lands on the 1 gridline. A dark spectrum stays a flat line at 0. Untick both.
7. Click the plot without moving the mouse: the plot keeps following the data and Reset scale stays disabled. Zoom
   with the mouse wheel over the peak: Reset scale turns enabled and the next spectra keep the zoomed limits. Press
   Reset scale: it turns disabled and the plot follows the data again. Drag the plot with the left button: the limits
   freeze again. Press Reset scale, then Stick to zero: the Y axis starts at 0 and ends at about 37200 (1.2 times the
   peak), and later spectra keep those limits until Reset scale.
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
- **Folders.** `SpectrumVisualizer.UI/Chart/` holds the chart pieces copied from SemiPlot (palette, log axis, tick
  generator, tick cache); intensity-trend reuses them. `SpectrumVisualizer.UI/Spectrum/` holds the displayed-spectrum
  seam, the scale and projection rules, the view models and the views of this slice. The distributor sits in
  `SpectrumVisualizer.UI/Link/`, beside the channel it drains. Core gains nothing.
- **Frame distributor.** `FrameDistributor(ChannelReader<ReceivedFrame> frames, MessagePanelViewModel, IScheduler
  uiScheduler, ILogger<FrameDistributor>)`, a singleton, is the only reader of `SerialLink.Frames`; `AddUi` passes
  that reader through a factory lambda, so tests hand it a real channel without a test-only constructor. `Start()`
  runs one task:
  `await foreach (var frame in reader.ReadAllAsync(token))`. For each frame it publishes `LatestSpectrum` with
  `Volatile.Write` when the frame carries a measurement, then hands the frame to each subscriber in turn.
  `IObservable<ReceivedFrame> Frames` is an `Observable.Create` over the distributor's own subscriber list: subscribing
  adds the observer and the returned disposable removes it. The list is copy-on-write (an `ImmutableArray` replaced
  under a lock, because subscribers come and go on any thread), and the loop reads one snapshot per frame. Each
  subscriber's `OnNext` runs inside its own try/catch, so a subscriber that throws takes nothing from the others: the
  same frame still reaches every later subscriber. The exception is logged at Error and reported once through
  `TryReportFailure` with the UI scheduler, and the next frame goes out as usual. A `Subject` would stop the frame at the
  first throwing observer. The channel never completes, so the loop
  lives across link sessions and ends only on cancellation. `StopAsync()` cancels and awaits the task and completes at
  once when nothing runs; `Start()` after a start or a stop does nothing; `Dispose()` cancels without waiting, for the
  container's synchronous dispose. Subscribers own their scheduling and never block the loop: the live source filters
  and signals, the view model moves to the UI scheduler with `ObserveOn`, the store adds a queue later.
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
  `LatestSpectrum` to a `DisplayedSpectrum`, caching the instance per frame so a repeated read returns the same
  object, and `Changed` is `distributor.Frames.Where(frame => frame.Frame.Measurement is not null).Select(_ =>
  Unit.Default)`.
- **Latest spectrum only.** `SpectrumViewModel` subscribes in its constructor:
  `source.Changed.ObserveOn(uiScheduler).StartWith(Unit.Default).Select(_ => source.Current).Where(not null)
  .DistinctUntilChanged(ReferenceEqualityComparer.Instance)`, then applies the spectrum to the chart and the pane. Signals queued while the UI thread is
  busy each read the same latest `Current`, and `DistinctUntilChanged` drops the repeats by reference on the instance
  the live source caches per frame, so the chart applies the latest spectrum once without comparing pixel arrays
  (record equality on `SpectrumMeasurement` compares every pixel, `BoardFrame.cs:60-70`). The subscription ends in `Dispose`; the view model is a container singleton, so its
  subscription lives for the process like the status bar's. A pipeline failure goes to `TryReportFailure`.
- **Chart model.** `SpectrumChartViewModel` owns the ScottPlot `Plot`, as SemiPlot's chart view model does, and the
  view binds it with `PlotControl.Reset(viewModel.Plot)`. It holds one `SignalXY` per device, built once with the
  device's wavelengths as `xs` and its own `double[] ys` buffer; only the shown device's line is visible. The window is
  a `HorizontalSpan` from `WavelengthNm - Delta` to `WavelengthNm + Delta` filled with `AppAccentFillBrush`, and a
  `VerticalLine` at `WavelengthNm` in `AppMarkerLineBrush`. `Show(DisplayedSpectrum)` runs on the UI thread: under
  `lock (Plot.Sync)` it fills the device's `ys` through `SpectrumProjection`, moves the band and the line, toggles the
  line visibility on a device change and, while the scale follows, sets the limits from `SpectrumScale`; then it calls
  `RequestRedraw()`. The frame's pixel array is read, never written; the store reads the same array later.
  `ApplyPalette(ChartColors)` and `ChartPalette.Apply` also run inside `lock (Plot.Sync)`, so a render never sees half
  a repaint.
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
  log or inversion toggle (the old limits are in other units or describe another curve). It freezes on a wheel step
  over the plot and on the first pointer move with a button held (a drag), through tunnel handlers for
  `PointerWheelChanged` and `PointerMoved` that call `TakeManualScale()`, and after Stick to zero. A bare click, a
  press and release without a move, keeps it following; so does a key press. While it follows, every spectrum sets the
  limits; while frozen, no spectrum moves them. A dark frame while following keeps the last non-dark Y limits, so the
  axis does not jump every Nth frame: the dark line draws at its floor inside the kept range (on the log axis the floor
  of 0 decades lies below a range kept from a spectrum whose minimum is above 1, so the data area is empty for that
  frame), and X still follows the dark frame's device. With no earlier non-dark frame, the flat-range limits apply. This is the old
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
  rectangle cover the old plot's zoom and pan. The `ScottPlot.dll` 5.1.59 metadata lists the response types
  `DoubleClickBenchmark`, `SingleClickAutoscale`, `SingleClickContextMenu`, `KeyboardAutoscale`,
  `KeyboardPanAndZoom`, `LeftClickDragPan`, `RightClickDragZoom`, `MouseDragZoomRectangle` and `MouseWheelZoom`; the
  XML documentation has no default list, so Task 10 reads the default `UserActionResponses` of a fresh `AvaPlot`
  through the assembly. The view removes `DoubleClickBenchmark`, `SingleClickAutoscale`, `SingleClickContextMenu`,
  `KeyboardAutoscale` and `KeyboardPanAndZoom`: the app is a read-only viewer, ScottPlot's autoscaler never sets the
  limits, and keyboard input never moves them. Pan, drag zoom, the zoom rectangle and wheel zoom remain. Reset scale
  is the one way back to following. `AvaPlot.HandleMouseWheelEvent` stays at its default.
- **Signal pane.** `SignalInfoViewModel` holds the shown `DisplayedSpectrum` and computes seven texts from it, each
  notifying from the setter of that property: spectrometer, wavelength, window, amplitude, SNR, Q factor, updated.
  Amplitude, SNR and Q show the board's values in `G6` under the current culture; a NaN or infinite value shows "-".
  SNR has no unit (the board sends a ratio). The update time is `ReceivedAt` converted to the `TimeProvider`'s local
  zone, `HH:mm:ss.fff`. Before the first spectrum every row reads "-", so every row exists in every state.
- **Chart controls.** `SpectrumViewModel` exposes `IsInverted` and `IsLogarithmic` (two-way bound; the setter assigns
  and raises, and one `WhenAnyValue` over both re-projects), `ResetScaleCommand` and `StickToZeroCommand`
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
    `SpectrumPanel.Width` and asserted by the text-fit test. Values are read-only text, left-aligned in the field
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
| Following | `xs[0] .. xs[P - 1]` | `min - 5 % .. max + 5 %` of the span | `min - 5 % .. max + 5 %` of the span |
| Following, a dark frame after a non-dark one | same | the last non-dark Y limits | the last non-dark Y limits |
| Following, flat (`min == max`) with no earlier non-dark frame | same | `v - 1 .. v + 1` | `v - 0.5 .. v + 0.5` |
| Stick to zero | same | `0 .. max x 1.2`; `0 .. 1` when `max == 0` | `0 .. log10(rawMax x 1.2)`; `0 .. 1` when `rawMax <= 1` |
| Frozen | unchanged | unchanged | unchanged |

`rawMax` is the largest displayed value before the log projection (after inversion). A dark frame is one whose raw
pixels are all zero. The last non-dark Y limits are kept per axis type and cleared by a log or inversion toggle, which
changes their units or their curve. Every input is a finite `ushort` or its logarithm, so every limit is finite.

Projection (`SpectrumProjection.Fill(ReadOnlySpan<ushort> pixels, bool inverted, bool logarithmic, Span<double>
ys)`): `value = inverted ? max - pixel : pixel`; `ys[i] = logarithmic ? Math.Log10(Math.Max(value, 1)) : value`.

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
"Длина волны", "Окно", "Амплитуда", "Сигнал/шум", "Добротность", "Обновлено", "{0} нм", "±{0} нм"; "График",
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
4. `SpectrumViewModel` moves the signal to the UI scheduler, reads `Current`, drops a repeat by reference, calls
   `Chart.Show` and
   sets `SignalInfo.Spectrum` (UI thread).
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
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Chart/LogAxis.cs`, `LogTickGenerator.cs`, `TickCache.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Chart/LogAxisTests.cs`, `LogTickGeneratorTests.cs`

- [ ] copy `LogTick`, `LogAxis.Project` and `LogAxis.Ticks` with their private helpers from
  `SemiPlot/SemiPlot.Core/Trends/LogAxis.cs` (drop `AdmitsMinimum`), renaming the namespace
- [ ] copy `TickCache` (`SemiPlot/SemiPlot.UI/Chart/TickCache.cs:8-39`)
- [ ] write `LogTickGenerator : ITickGenerator` from `SemiPlot/SemiPlot.UI/Chart/LogTickGenerator.cs:10-93` without
  `Mask`, `IsDrawn`, the zero-label and the read-back rules: a major reads `10^position` as `"0.###"` under the current
  culture, a repeated label turns minor, results cached in a `TickCache` keyed on the limits and the length;
  `IsLogarithmic(IYAxis?)` stays
- [ ] copy `SemiPlot/SemiPlot.Tests.Unit/Core/Trends/LogAxisTests.cs` for the copied members (`Component=UI`,
  `Area=Chart`)
- [ ] write `LogTickGeneratorTests`: `MajorsCarryTheValueAsAnIntegerCount` (0..5 decades at 400 px labels "1", "10",
  ... "100000" under `CultureScope("en")` and `CultureScope("ru")`), `ARepeatedLabelTurnsMinor`,
  `TheSecondLengthOfAFrameReusesTheCachedTicks` (same array instance), `NothingForANonFiniteRange`
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 3: Frame distributor
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Link/FrameDistributor.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Link/FrameDistributorTests.cs`

- [ ] implement `FrameDistributor` of Solution Overview: one public constructor over a `ChannelReader<ReceivedFrame>`;
  `LatestSpectrum`, `Frames` over the distributor's own copy-on-write subscriber list, `Start()`, `StopAsync()`,
  `Dispose()`; the loop calls each subscriber's `OnNext` inside its own try/catch, logs a throw at Error and reports it
  once with `TryReportFailure`
- [ ] the loop logs at Debug when it starts and when it stops; cancellation ends it without a log at Error
- [ ] write the `FrameDistributorTests` of Acceptance Evidence item 2 (`Component=UI`, `Area=Link`) over a real bounded
  channel with the link's options
- [ ] write `AThrowingSubscriberDoesNotTakeTheFrameFromTheOthers` with a real `MessagePanelViewModel`: subscriber 1
  throws on frame 1, subscriber 2 receives that same frame 1, the panel holds one Error entry naming the exception
  type, and frame 2 reaches both subscribers
- [ ] write `AnUnsubscribedObserverReceivesNothingMore`: dispose the subscription, write a frame, the observer's count
  stays
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 4: Displayed-spectrum seam and the live source
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/IDisplayedSpectrumSource.cs` (with `DisplayedSpectrum`)
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/LiveSpectrumSource.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Spectrum/LiveSpectrumSourceTests.cs`,
  `FakeDisplayedSpectrumSource.cs`

- [ ] declare `DisplayedSpectrum(SpectrumMeasurement Measurement, DateTimeOffset ReceivedAt)` and
  `IDisplayedSpectrumSource` (`Current`, `Changed`) of Solution Overview; the reason for the interface lives in
  `overview.md`, not in a comment naming a class that does not exist yet
- [ ] implement `LiveSpectrumSource(FrameDistributor)`: `Current` from `LatestSpectrum`, cached per frame instance;
  `Changed` from `Frames` filtered to spectrum frames
- [ ] write `FakeDisplayedSpectrumSource` (settable `Current`, `Changed` from a private subject with a `Signal()`
  method) for the view model tests
- [ ] write `LiveSpectrumSourceTests` (`Component=UI`, `Area=Spectrum`) of Acceptance Evidence item 3 over a
  distributor on a test channel; add `TwoReadsOfOneFrameReturnTheSameInstance`
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 5: Distributor and source in the composition root and the window lifecycle
**Files:**
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/UiServiceCollectionExtensions.cs`, `App.axaml.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Di/CompositionRootTests.cs`, `AppLifecycleTests.cs`

- [ ] register `FrameDistributor` through a factory lambda that passes `GetRequiredService<SerialLink>().Frames`, the
  shape the link's own registration has (`UiServiceCollectionExtensions.cs:27-30`), and `IDisplayedSpectrumSource` as
  `LiveSpectrumSource`, both singletons after the link
- [ ] add the distributor to `ConfiguredServices` (`App.axaml.cs:168-172`) and resolve it in `Configure` (`:141-148`);
  start it in the window's `Opened` beside the port refresh (`:70`)
- [ ] stop it in the close path: `StopLinkAsync` (`:105-117`) becomes a stop of the link, then the distributor, both
  inside the one 5 s limit; rename it `StopBackgroundServicesAsync` and its log line names both
- [ ] write tests: the distributor and the source resolve as one instance each (`CompositionRootTests`);
  `OpeningTheWindowStartsTheFrameDistributor` and `ClosingTheWindowStopsTheDistributorAfterTheLink` in the shape of the
  existing `AppLifecycleTests` (`AppLifecycleTests.cs:50-61`: a `FakePortStream` queued on `FakeSerialPortProvider`,
  then `link.Connect`). The first raises `Opened`, connects, feeds an encoded frame with `FakePortStream.PushBytes`
  (`FakePortStream.cs:51`) and asserts that a distributor subscriber receives it. The second closes the window, waits
  for the close to complete, and asserts that the log sink (`CollectingSink`, `AppLifecycleTests.cs:44`) holds the
  distributor's Debug stop line after the link's "The link on port {Port} stopped" line (`SerialLink.cs:107`). No
  test-only writer to the link's channel exists
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 6: Scale and projection rules
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/SpectrumScale.cs`, `SpectrumProjection.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Spectrum/SpectrumScaleTests.cs`,
  `SpectrumProjectionTests.cs`

- [ ] implement `SpectrumProjection.Fill` of Technical Details as a single pass to find the maximum and a second to
  write, no LINQ (it runs per frame); it returns the raw maximum for the stick-to-zero rule
- [ ] implement `SpectrumScale` as static functions returning an `AxisLimits`-like record of four doubles:
  `Following(xs, ys, logarithmic, lastNonDarkY)` and `StuckToZero(xs, ys, rawMax, logarithmic)`, the rules of the
  scale table; `Following` takes the kept Y limits of the last non-dark frame (null when none) and returns them for a
  dark frame
- [ ] write `SpectrumProjectionTests` and `SpectrumScaleTests` of Acceptance Evidence item 3 as table tests
  (`Component=UI`, `Area=Spectrum`), including the all-zero spectrum in each mode with `double.IsFinite` on every
  limit, `ADarkFrameWhileFollowingKeepsTheLastNonDarkYLimits` and `ADarkFrameWithNoEarlierSpectrumTakesTheFlatRangeLimits`
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 7: Spectrum chart view model
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/SpectrumChartViewModel.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Spectrum/SpectrumChartViewModelTests.cs`,
  `SpectrumChartViewModelRenderTests.cs`, `SpectrumSamples.cs`

- [ ] implement `SpectrumChartViewModel` of Solution Overview: the `Plot`, two `SignalXY` lines built from
  `SpectrometerCalibration.For(device).WavelengthOf` (`SpectrometerCalibration.cs:51-54`), the band and the centre
  line, the axis labels from resx with `Plot.Font.Automatic()`, `Show(DisplayedSpectrum)`, `IsFollowing`,
  `TakeManualScale()`, `ResetScale()`, `StickToZero()`, `SetDisplay(bool inverted, bool logarithmic)`,
  `ApplyPalette(ChartColors)`, `RequestRedraw()` and `RedrawRequested`, `HasSpectrum`
- [ ] every write to the plot (buffers, plottable visibility, limits, tick generator, minor grid width, colours through
  `ApplyPalette` and `ChartPalette.Apply`) happens inside `lock (Plot.Sync)`; the chart keeps the last non-dark Y
  limits per axis type and clears them on a log or inversion toggle; the band and the centre line are hidden when
  `WavelengthNm - Delta .. WavelengthNm + Delta` does not overlap the shown device's range; `RedrawRequested` follows SemiPlot (raises only while a view subscribes; `Dispose` completes it)
- [ ] write `SpectrumSamples`: an ATP-2000 and an ATP-8000 `DisplayedSpectrum` with a peak, and a dark one per device
- [ ] write the `SpectrumChartViewModelTests` of Acceptance Evidence item 3 (`[AvaloniaFact]`, `Area=Spectrum`),
  including `ShowNeverWritesTheFramesPixels` (a copy of the array equals it after `Show` with inversion and log on),
  `ABandOutsideTheDeviceRangeIsHiddenAndTheXLimitsStayTheDeviceRange`,
  `AFrameFromTheOtherDeviceWhileFrozenKeepsTheFrozenLimits` and `ApplyPaletteRunsUnderThePlotLock` (a second thread
  that holds `Plot.Sync` delays `ApplyPalette` until it releases)
- [ ] write `SpectrumChartViewModelRenderTests`: a dark frame per axis type, then `Plot.RenderInMemory(640, 480)`
  throws nothing and `Plot.Axes.GetLimits()` (`ScottPlot.xml:272`) is finite on all four sides; the same for an inverted dark frame on log
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 8: Signal pane view model and texts
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/SignalInfoViewModel.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Spectrum/SignalInfoViewModelTests.cs`

- [ ] add every new resx key of Technical Details in English and Russian; remove `ContentPlaceholder` from both sets
  only in Task 11, where its last reader goes
- [ ] implement `SignalInfoViewModel(TimeProvider)` with `Spectrum` and the seven computed texts of Technical Details,
  each raised from the `Spectrum` setter
- [ ] write `SignalInfoViewModelTests` of Acceptance Evidence item 3 under `CultureScope("en")` and
  `CultureScope("ru")` with a `FakeTimeProvider` in a fixed zone (`Microsoft.Extensions.Diagnostics.Testing` is
  referenced by the test project, `SpectrumVisualizer.Tests.Unit.csproj:10`); compare texts with the generated accessor, never literals
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 9: Spectrum view model
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/SpectrumViewModel.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Spectrum/SpectrumViewModelTests.cs`

- [ ] implement `SpectrumViewModel(IDisplayedSpectrumSource, MessagePanelViewModel, TimeProvider, IScheduler
  uiScheduler, ILogger<SpectrumViewModel>)` of Solution Overview: builds `Chart` and `SignalInfo`, the latest-only
  pipeline with `ObserveOn` first, `IsInverted`, `IsLogarithmic`, `ResetScaleCommand`, `StickToZeroCommand`,
  `Dispose` for the subscription, the commands and both children
- [ ] a failure in the pipeline logs at Error and goes to `TryReportFailure`; it ends that subscription
- [ ] write `SpectrumViewModelTests` of Acceptance Evidence item 3 over `FakeDisplayedSpectrumSource`: the three
  queued signals apply once (count `Chart.RedrawRequested` emissions with a subscribed observer), the spectrum at
  construction shows, `TheSameCachedSpectrumIsAppliedOnceByReference` (two signals with one `Current` instance apply
  once; a new instance with equal values applies again), Stick to zero follows `HasSpectrum`,
  `ResetScaleShowsTheFrozenState` (disabled while following, enabled after `TakeManualScale()`, disabled after Reset),
  the checkboxes reach `Chart.SetDisplay`, a throwing `Current` adds one Error entry
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 10: Spectrum chart view
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/SpectrumChartView.axaml`, `SpectrumChartView.axaml.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Spectrum/SpectrumChartViewTests.cs`

- [ ] load the `dev:desktop-ui` skill before writing the AXAML
- [ ] lay out the view as `TrendChartView.axaml:54-81` without the cursor overlay: a `Panel` with the `PlotLayer`
  decorator (`BitmapCache`) holding `PlotControl` alone, and the empty-state `TextBlock` bound to `Chart.HasSpectrum`
  in `AppSecondaryForegroundBrush`
- [ ] in code-behind copy `RequestFrame`, `ServeFrame`, `ClearPendingFrame`, `ServedFrameCount`, the theme repaint on
  `OnLoaded` and `ActualThemeVariantChanged` with its removal in `OnUnloaded`, and `ReportFailureOf`
  (`TrendChartView.axaml.cs:94`, `:102-148`, `:289-335`); `OnDataContextChanged` binds `PlotControl.Reset(chart.Plot)`
  and subscribes `RedrawRequested`
- [ ] read the default `UserActionResponses` of a fresh `AvaPlot` through the assembly (the XML documentation has no
  default list) and record it in this checkbox; remove `DoubleClickBenchmark`, `SingleClickAutoscale`,
  `SingleClickContextMenu`, `KeyboardAutoscale` and `KeyboardPanAndZoom`, keeping `LeftClickDragPan`,
  `RightClickDragZoom`, `MouseDragZoomRectangle` and `MouseWheelZoom`
- [ ] add tunnel handlers on `PlotControl` that call `Chart.TakeManualScale()` through `DataContext is
  SpectrumChartViewModel chart`: `PointerWheelChanged` always; `PointerMoved` when a pointer button is held
  (`e.GetCurrentPoint(PlotControl).Properties` has a pressed button), once per drag. `PointerPressed` and `KeyDown`
  get no handler, so a bare click keeps the scale following
- [ ] write the `SpectrumChartViewTests` of Acceptance Evidence item 3 (`[AvaloniaFact]`, `Area=Spectrum`), copying
  the shape of `TrendChartViewTests.cs:171` and `:278`; the pointer tests raise headless mouse events on the realised
  view (`MouseDown`, `MouseMove`, `MouseUp`, `MouseWheel` of the headless window) and read `Chart.IsFollowing`
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 11: Main window content: chart pane and signal pane
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/SpectrumPanel.axaml`, `SpectrumPanel.axaml.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/MainWindow/MainWindow.axaml`, `MainWindowViewModel.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/UiServiceCollectionExtensions.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/MainWindow/MainWindowViewTests.cs`,
  `MainWindowViewModelTests.cs`, `MainWindowLayoutTestBase.cs`, `UI/Di/CompositionRootTests.cs`

- [ ] write `SpectrumPanel.axaml` (`x:DataType` `SpectrumViewModel`): the form grid of Solution Overview, every inset
  from the spacing table, values `NoWrap`, the pane width as `SpectrumPanel.Width` (a `const double`, first set from
  the formula with an estimate, fixed in Task 12)
- [ ] replace `ContentArea` (`MainWindow.axaml:33-42`) with a `Grid ColumnDefinitions="*,Auto"`: `SpectrumPane`
  holding `SpectrumChartView` bound to `Spectrum.Chart`, and `SignalPane` holding `SpectrumPanel` bound to `Spectrum`
- [ ] add `SpectrumViewModel Spectrum` to `MainWindowViewModel` (`MainWindowViewModel.cs:23-39`); register
  `SpectrumViewModel` in `AddUi` (`UiServiceCollectionExtensions.cs:31-34`); remove `ContentPlaceholder` from both
  resx sets
- [ ] update `MainWindowViewTests` (`:69-80` placeholder test becomes the content-row test; `:35` construction) and
  `MainWindowViewModelTests`; add `TheSignalPaneRealisesEveryRowAndControl` and `ACheckboxReachesTheChart`
- [ ] `CompositionRootTests.EveryRegisteredServiceResolves` covers the new view model; add
  `TheMainWindowShowsTheContainersSpectrumViewModel`
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 12: Desktop UI pre-handover checklist
**Files:**
- Modify (only to fix a miss): `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/SpectrumPanel.axaml`,
  `SpectrumChartView.axaml`, `MainWindow/MainWindow.axaml`, the resx pair
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/MainWindow/MainWindow.axaml.cs` (`MinimumWidth`),
  `Spectrum/SpectrumPanel.axaml.cs` (`Width`)
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/MainWindow/MainWindowLayoutTests.cs`,
  `MainWindowTextFitTests.cs`

- [ ] every gap and inset in the signal pane and the content row reads off the spacing table; the `ui-theme.md:67` grep
  prints nothing (pane padding 16; label to value 12; row spacing 8; section header 16 above, 8 below, the first 0)
- [ ] every field width reads off the width table or carries a stated reason: the value column 120 (numbers); the
  field column widens only to the widest checkbox or button, measured; record both measured widths per language
- [ ] measure through `RenderedText` in English and Russian: the label column (widest label), the field column, the
  longest values ("-1.23457E-38", "±100 nm", "23:59:59.999", "ATP-8000"); set `SpectrumPanel.Width` to 16 + label +
  12 + field + 16 + 24 rounded up to 8; record the numbers in this checkbox
- [ ] measure the chart floor: an ATP-8000 spectrum on a 400 px wide `RenderInMemory` keeps at least four labelled
  ticks on `Axes.Bottom`; set `MainWindow.MinimumWidth` to the larger of the status bar's 1042.4 px Russian width and
  `SpectrumPanel.Width` + 400, rounded up to 8; record the result
- [ ] at the window's minimum width the pane shows every row and control and the chart keeps at least 400 px; at
  2560 px the pane keeps its width and left-aligned content, and only the chart grows
- [ ] every label, value, checkbox and button fits on one line in English and Russian
- [ ] every state keeps the window size and the pane bounds: no spectrum, ATP-2000, ATP-8000, dark, log on, inverted,
  frozen scale, the message panel shown; the `ui-theme.md:80` grep prints nothing
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 13: Verify acceptance criteria
- [ ] run every command of Acceptance Evidence and record each result with its count (per Area, full suite, none
  skipped; Release build 0 warnings; format and terse exit 0; old solution 13 of 13; both greps print nothing)
- [ ] verify every Overview item maps to a named passing test: the line over wavelength, the band, the pane values,
  inversion, the log axis and its floor, reset, stick to zero, the distributor as the one reader, the seam, the
  frame-paced redraw and the cache
- [ ] verify edge cases and their expected outcomes: a dark frame while following keeps the last non-dark Y limits
  and X follows its device, and with no earlier spectrum takes the flat-range limits, on both axis types, inverted and
  not; a device change while following moves X to the new device's range; a device change while frozen keeps the
  frozen limits, and the plot may stay blank until Reset scale; a band outside the device range is not drawn while the
  pane shows the wavelength; `delta` 0 draws the centre line only; a bare click keeps following; a heartbeat-only
  stream keeps the last spectrum; a throwing subscriber leaves the frame to the others and is reported once
- [ ] run the full suite: `dotnet test SpectrumVisualizer.slnx`; confirm the old solution builds and passes 13 of 13
- [ ] start the app with `dotnet run --project SpectrumVisualizer/SpectrumVisualizer.UI` and confirm the window shows
  the empty-state line and the log gains its start line; leave the port-pair checklist to the owner

### Task 14: [Final] Update documentation
- [ ] `docs/architecture/overview.md`: the purpose paragraph (`:5-11`, "No code reads the decoded frames" at `:10` is
  no longer true); the Stack list (`:13-25`) gains ScottPlot.Avalonia 5.1.59; the components table (`:29-61`) gains
  `FrameDistributor`, `DisplayedSpectrum`, `IDisplayedSpectrumSource`, `LiveSpectrumSource`, `SpectrumProjection`,
  `SpectrumScale`, `SpectrumChartViewModel`, `SignalInfoViewModel`, `SpectrumViewModel`, `ChartPalette`, `LogAxis`,
  `LogTickGenerator`; startup (`Opened` starts the distributor, `Closing` stops the link, then the distributor); the
  main window rows (`:103-111`, the content row) and the minimum width paragraph (`:116-122`); a "Spectrum" section:
  data flow, latest-only, the scale rules, the log floor, inversion, the empty state, the stock input
- [ ] `docs/architecture/board-protocol.md`: "Frames out" (`:283-288`): the distributor is the one reader and forwards
  every frame; the calibration window paragraph (`:317-320`) notes that the chart's band is the app's nm window
- [ ] `docs/architecture/README.md`: the Chart renderer row (`:32`) names ScottPlot.Avalonia 5.1.59, the cache wrapper
  and the frame-paced redraw; a Frame distribution row (one reader, latest spectrum, subscribers own their scheduling)
- [ ] `docs/architecture/ui-theme.md`: the four keys in the key table (`:44-55`) and a "The plot" section (the surfaces,
  the alpha times `Opacity` rule, the repaint on load and variant change)
- [ ] `docs/architecture/ui-text.md`: the key owners (`:39-46`): Signal pane, Chart controls, Spectrum chart; remove
  `ContentPlaceholder` (`:41`); `StatusNoValue` gains the signal pane as a second reader
- [ ] `docs/architecture/testing-strategy.md`: the Area list (`:20-22`) gains `Chart` and `Spectrum`; rows for Chart
  (log axis, tick generator, palette), Spectrum (rules, view models, render tests, chart view) and the distributor in
  the Link row; the Layout row covers the signal pane
- [ ] `readme.md`: replace "The new app has no spectrum chart and no storage, so the old app is still the one that
  shows the spectrum" (`:16-17`) with what the spectrum screen shows
- [ ] `CLAUDE.md`: add `Chart` and `Spectrum` to the Area list (`:40-41`); the Layout line for `SpectrumVisualizer.UI/`
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
