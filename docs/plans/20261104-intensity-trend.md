# Intensity trend

## Overview
- Show how the intensity at SCADA's wavelength changes over time, under the spectrum chart. The intensity of a spectrum
  is the mean of its pixels in the ±`delta` nm window of the Core calibration, dark frames included, the same value the
  store's series computes. The wavelength and `delta` come from the latest spectrum frame.
- The trend behaves like SemiPlot's trend: a min/max envelope line per pixel column, a local time axis, live follow
  that stops when the operator scrolls back, wheel zoom around the cursor, drag pan, time window presets with 10 min by
  default, a 36 px overview of the whole 24 h history, and a hover readout.
- The line breaks for lost spectra, for more than 10 s without a spectrum and for spectra of a spectrometer that does
  not cover the wavelength. A thin strip marks the times when the board reported a failed measurement.
- Live data comes from an in-memory intensity cache fed by `FrameDistributor.Frames`, one point per spectrum frame.
  Live follow, zoom, pan and the overview read the cache. A wavelength or `delta` change re-reads the stored history at
  the new key: the visible range first, then the rest of the 24 h newest first, cancellable, behind a throttled
  trigger.
- A click on the trend shows that moment's spectrum on the spectrum chart through a switching displayed-spectrum
  source, with a "Spectrum from HH:mm:ss" line over the chart, a marker at that moment on the trend and a "Back to
  live" button.
- Scope guard: no protocol change, no store schema change (one new points read over the series' statement and row
  loop), no launch keys
  or config files, nothing sent to the board, no change to the old app.

## Context (from discovery)
- Roadmap: docs/plans/roadmaps/20261104-avalonia-rewrite-roadmap.md — slice intensity-trend (#3)
- Stacking base: local branch `spectrum-store` at `9070dfb`; this slice works on the local branch `intensity-trend`
  created from it. Local commits only.
- The slice brief is the roadmap's intensity-trend scope (`roadmap:219-249`). The resolved fork on dark spectra
  (`roadmap:315-316`) keeps dark measurements on the trend and asks this slice to correct the issue text that promises
  otherwise. Issue Semiteq/SpectrumVisualizer#3 ("Темновые спектры на график не попадают") is that text.
- Core store surface this slice reads:
  - `SpectrumHistory(path)` (`SpectrumHistory.cs:15`): `SpectrumAtAsync(moment, token)` returns the latest row at or
    before the moment, ties to the larger `id` (`:20-24`, `:32-45`); `IntensitySeriesAsync(query, token)` (`:47-53`);
    `StoredRangeAsync` (`:56-73`). Every call opens its own reader connection inside `Task.Run` and maps a
    `SqliteException` to a failed `Result` (`:136-153`). `ReadSeries` (`:79-109`) runs one statement per range,
    checks the token every 256 rows (`:18`, `:92-95`) and folds through `IntensityBuckets`; the statement cuts each
    row's window with `substr` inside a `CASE` (`:115-134`), and the comment at `:117-118` records why a second
    expression column crashes `GetBytes`. `ToStoredSpectrum` builds a new `SpectrumMeasurement` per row (`:155-179`);
    `StoredSpectrum(Measurement, ReceivedAt, Seq, State)` (`:205-206`).
  - `QueryWindows(wavelengthNm, delta)` (`QueryWindows.cs:11`): `Covers(device)` is the firmware table range
    (`:22-26`, `:57-61`), `For(device)` the pixel window (`:28-31`), `MeanOf(windowBytes)` the mean of little-endian
    u16 pixels (`:45-55`).
  - `IntensityBuckets(query)` folds `Add(receivedAt, value)` into min/max per column (`IntensityBuckets.cs:13-33`) and
    `AddUncovered` into `UncoveredCount` (`:36-43`); `ToSeries` gives NaN min/max to an empty column (`:45-57`).
    `IntensityQuery.Create` truncates to whole ms, rejects an empty range, a width under 1 ms or over 40 000 columns
    (`:66-67`, `:107-134`); `ColumnOf` and `ColumnStart` sit on a grid anchored at the Unix epoch (`:137-151`).
    `IntensityColumn(Start, Min, Max, Count, UncoveredCount)` (`:181-182`).
  - Schema: `spectrum` with `seq`, `state` and `pixels` (`SpectrumDatabase.cs:17-29`), `heartbeat(received_at, seq,
    state)` with its time index (`:30-35`); both tables share the retention delete (`:47-50`). The heartbeat table has
    no reader in the app after this slice either; the store keeps writing it, for diagnostics of a copied file.
  - `StoreFiles.Prepare` wipes the instance's own file before the open (`StoreFiles.cs:24-30`), so the file holds only
    frames of this run, every one of which the distributor handed out after the trend feed subscribed.
- Protocol facts: `BoardFrame(State, Seq, Measurement?)`, null for a heartbeat (`BoardFrame.cs:6`); `BoardState` bits
  connected and measurement failed per device (`:8-16`). `SequenceTracker.Observe` returns the lost count from the
  `seq` step, a heartbeat repeating the last spectrum's `seq` (`SequenceTracker.cs:12-29`, `board-protocol.md:123-146`);
  `Reset()` starts a new baseline (`SequenceTracker.cs:31-34`). The link resets its tracker on every open
  (`board-protocol.md:254-255`), and the tracker observes before the channel write, so a channel drop leaves no gap in
  the link's count (`board-protocol.md:298-300`). A heartbeat comes after 3 s with nothing sent (`board-protocol.md:28`);
  the link calls a board silent after 10 s (`LinkHealth.cs:10`, `board-protocol.md:202-205`). A spectrum frame always
  carries its own device's failure bit at 0 (`board-protocol.md:42-44`). Table ranges: ATP-2000 189-1028 nm, ATP-8000
  898-2461 nm (`board-protocol.md:316-319`).
- UI surface this slice changes:
  - `IDisplayedSpectrumSource` with `Current` and `Changed`; `DisplayedSpectrum(Measurement, ReceivedAt)`
    (`IDisplayedSpectrumSource.cs:8-18`). `LiveSpectrumSource.Current` wraps the distributor's latest spectrum in a new
    record around the frame's own measurement (`LiveSpectrumSource.cs:14-17`); `Changed` skips heartbeats (`:19-21`).
  - `SpectrumViewModel` reads the source with `DistinctUntilChanged` on the measurement by reference
    (`SpectrumViewModel.cs:38-44`); its commands at `:53-60`.
  - `SpectrumChartViewModel.Project` is the only writer of the per-device `LastNonDark` (`SpectrumChartViewModel.cs:189-212`,
    the write at `:198-201`); `SetDisplay` (`:82-101`) and `ResetScale` (`:124-131`) run `Project` again on the shown
    spectrum. `TakeManualScale` freezes (`:104-112`); `ChangeScale` raises `IsFollowing` after the lock (`:250-264`).
  - `FrameDistributor.LatestSpectrum` (`FrameDistributor.cs:52`) and `Frames` (`:58`).
  - `AddUi` registers `LiveSpectrumSource` as the `IDisplayedSpectrumSource` (`UiServiceCollectionExtensions.cs:42`)
    and no `SpectrumHistory` (`:23-50`); the store's `StoreFile` record is `SpectrumStore.cs:15`, registered at
    `UiServiceCollectionExtensions.cs:40`.
  - `App.CreateMainWindow` starts the store, then the distributor on `Opened` (`App.axaml.cs:74-80`); the close chain
    is `StopLinkDistributorThenStoreAsync` (`:146-164`); `Configure` and `ConfiguredServices` (`:188-196`, `:217-223`).
  - Main window content: `ContentArea` with columns `*,Auto`, `SpectrumPane` and the 392 px `SignalPane`
    (`MainWindow.axaml:34-55`); `MinimumWidth` 1256 and `OpeningWidth` 1280 (`MainWindow.axaml.cs:12`, `:14`); the
    minimum is the Russian status bar at 1252.2 px (`overview.md:169-178`). The signal pane form has 13 rows ending
    with Reset scale and Stick to zero (`SpectrumPanel.axaml:23`, `:93-127`); `PaneWidth` 392 (`SpectrumPanel.axaml.cs:10`).
    The chart view's `Panel` holds the cached `PlotLayer` and the empty-state line (`SpectrumChartView.axaml:14-30`).
  - `MainWindowViewModel` takes the panel, the connection row, the status bar and the spectrum view model
    (`MainWindowViewModel.cs:24-42`).
  - Palette keys in both variants (`Palette.axaml:64-77`, `:132-145`), their table `ui-theme.md:44-59`, the inset and
    colour greps `ui-theme.md:90` and `:103`. Text keys table `ui-text.md:41-51`.
- Benchmark tool: `BenchmarkCommand` defaults to 172 800 rows (`BenchmarkCommand.cs:8`); `MeasureAsync` times the
  10 min and 24 h series and the `SpectrumHistory` calls (`BenchmarkRun.cs:71-98`). Measured on the spectrum-store run:
  24 h series warm 647 ms churned, 10 min series warm 5 ms (`docs/plans/20261104-spectrum-store.md:248-251`).
- Emulator options `--device` (default 8000), `--rate`, `--wavelength`, `--delta`, `--bad-crc-every`, `--dark-every`,
  `--both-connected`, `--failed` (`EmulatorCommand.cs:19-71`); a corrupted frame still consumes its `seq`
  (`board-protocol.md:365-366`).
- SemiPlot pieces copied (`C:/Users/admin/projects/SemiPlot` at `bd8f83f`; paths relative to that repo root):
  - Envelope: `SemiPlot/SemiPlot.UI/Chart/EnvelopeColumn.cs:4` (`X, Min, Max, Center`);
    `SemiPlot/SemiPlot.UI/Chart/EnvelopePath.cs` (`VisibleRange` `:18-32`, `Build` `:34-105`, a NaN column breaks the
    path at `:52-57` through `IsGap` `:145-148`); `SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs` (`IPlottable` `:12`,
    state under one lock `:28`, `GetAxisLimits` `:57-89`, `Render` `:91-124`, `ReplaceColumns` `:202-225`). Rules:
    `SemiPlot/docs/architecture/charting.md:12-15`, `:30-43`, `:69-75`. Tests `EnvelopePathTests.cs` (`:31`, `:42`,
    `:78`), `EnvelopeLineTests.cs:49`, `ChartGapRenderTests.cs:57` under `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/`.
  - Time axis: ScottPlot's `DateTimeAutomatic` assigned to the existing bottom axis
    (`SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs:254-256`) and `LocalTimeAxis.ToAxis` / `FromAxis` over OADate
    in local time (`SemiPlot/SemiPlot.UI/Chart/LocalTimeAxis.cs:8-20`), test `LocalTimeAxisTests.cs:15`, `:26`. SemiPlot
    has no custom time tick generator.
  - Navigation: `SemiPlot/SemiPlot.Core/Trends/TrendNavigationModel.cs` (1 s to 365 d `:7-8`, ladder 1.25 `:13`, `Pan`
    auto-detaches sticky `:37-45`, `Zoom` around the anchor `:47-60`, `SetWidth` keeps the right edge `:88`, left clamp
    `PlaceFrom` `:94-98`, `JumpToNow` `:100`, `OnLiveEdge` `:113-123`); tests `TrendNavigationModelTests.cs:18`, `:61`,
    `:86`. Wheel factors 0.8 and 1.25 and a 10 % Shift pan (`TrendChartView.axaml.cs:26-28`, `:422-472`),
    `ChartWheelRouter.Route` (`SemiPlot/SemiPlot.UI/Chart/ChartWheelRouter.cs:19-40`), the 4 px click slop
    (`SemiPlot/SemiPlot.UI/Chart/ChartPressRouter.cs:29`, `:71`), drag pan past the slop and click on release
    (`TrendChartView.axaml.cs:597-643`, `:691-723`), input processor disabled and wheel unhandled (`:78`, `:81`). Rules
    `SemiPlot/docs/architecture/trend-interaction.md:137-152`, `:211-218`, `:238-279`, `:298-302`.
  - Presets: `TimeWindowPreset(Label, Width)` and the list 1, 2, 3, 5, 10, 30 min and 1 h
    (`SemiPlot/SemiPlot.UI/Chart/TimeWindowPanelViewModel.cs:18`, `:61-70`); SemiPlot's default window is 1 h
    (`SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs:20`).
  - Columns: one per pixel, 256 to 2048 (`SemiPlot/SemiPlot.UI/Chart/HistoryColumnTarget.cs:7-17`).
  - Hover readout: the Avalonia overlay `Canvas` with a `Line` and a `Border` over the cached plot
    (`SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml:82-96`), `ChartCursorOverlay.Project`
    (`SemiPlot/SemiPlot.UI/Chart/ChartCursorOverlay.cs:16-28`), the placement and hide rules
    (`TrendChartView.axaml.cs:798-844`, `:757`), `trend-interaction.md:303-318`; tests `ChartCursorOverlayTests.cs:41`.
  - Overview: `SemiPlot/SemiPlot.UI/Minimap/MinimapView.axaml` (36 px strip `:16`, window highlight in
    `AppAccentFillBrush` with an `AppAccentBrush` border `:28-32`, hover line `:39`),
    `SemiPlot/SemiPlot.UI/Minimap/MinimapView.axaml.cs` (6 px highlight minimum `:17`, pointer navigation `:185-226`),
    `SemiPlot/SemiPlot.UI/Minimap/MinimapBand.cs` (fill opacity 0.35 `:19`, `Render` `:39-57`),
    `SemiPlot/SemiPlot.Core/Trends/MinimapGeometry.cs` (`WindowFraction` `:12`, `FractionAt` `:75`, `TimeAtFraction`
    `:86`, `BandFigures` `:93-127`), `SemiPlot/SemiPlot.UI/Minimap/MinimapViewModel.cs` (`NavigateToFraction`
    `:153-166`); the feed `SemiPlot/SemiPlot.UI/Minimap/MinimapBandFeed.cs` (`_requests.Select(Read).Switch()` at
    `:112-119`, `Read` through `Observable.Create` with a token `:152-158`, stale drop `:186-204`, next-read delay
    clamp(span/1000, 2 s, 60 s) `:206-211`); tests `MinimapBandFeedTests.cs:61` (`ASupersededBandRead_IsCancelled`),
    `MinimapGeometryTests.cs:57`.
  - History planner: `SemiPlot/SemiPlot.UI/Chart/ChartHistoryPlanner.cs:47-85` (the visible window first, then one
    expanded request) and the single-flight debouncer with a 150 ms trailing window
    (`SemiPlot/SemiPlot.UI/Chart/ChartHistoryRequestDebouncer.cs:52-74`, `TrendChartViewModel.cs:24-25`).
  - SemiPlot has no selected moment: a click picks a pen (`ChartPressRouter.cs:31-69`). The click-to-moment route is
    new here.
  - Stripped domain: pens (`Pen`, `PenLineStyle`, `TrendPenState`, per-pen axes, the log axis), archive layers
    (`ArchiveExtent`, `AggregationLayer`, `TrendCoordinator`), hold, unread and outage logic, time markers, the time
    window panel's calendar form.
- Test traits: `Component`, `Area`, `Category=Unit` (`CLAUDE.md:41-43`, `testing-strategy.md:23-25`). This slice adds
  the Area `Trend`.

## Development Approach
- **testing approach**: Regular (code first, then tests in the same task).
- complete each task fully before moving to the next; small, focused changes.
- every task includes new or updated tests for its code; all tests pass before the next task.
- update this plan when scope changes during implementation.
- the old solution keeps building and passing its 13 tests; this slice does not touch it.

## Testing Strategy
- **unit tests** in `SpectrumVisualizer.Tests.Unit`: plain `[Fact]` for the Core pieces (the continuity rule, the
  cache, the planner, the navigation model, the column mapping, the overview geometry); temporary SQLite files through
  `SpectrumHistoryTestBase` for the new read; `[AvaloniaFact]` for view models, the trend view, the overview view and
  the realised window. Traits: `Area=Trend` for `Core/Trend/` (`Component=Core`) and `UI/Trend/` (`Component=UI`);
  `Area=Store` for the store read; `Area=Spectrum` for the switching source and the chart rules; `Area=MainWindow`,
  `Area=Di`, `Area=Resources` as before; `Component=Tools, Area=Store` for the benchmark rows.
- **feed tests** drive the trend feed with a real `Channel` behind a real `FrameDistributor`, as
  `FrameDistributorTests` do, and a `TestScheduler` for the throttle; every real-time wait has the 5 s limit of `Waits`.
- **cancellation tests** hold a read with a gate in a fake history reader and assert that a second key change cancels
  the first read's token, the shape of `MinimapBandFeedTests.ASupersededBandRead_IsCancelled`.
- **render tests** call `Plot.RenderInMemory` on the trend's plot with gaps and with an all-NaN range.
- **timing** runs in the benchmark tool, never in the unit suite; the suite runs the tool on 2 000 rows.
- no e2e framework exists; headless tests take that role. The port-pair run is manual.

## Acceptance Evidence
Automatable (baseline: verified 2026-11-04, `dotnet test SpectrumVisualizer.slnx` at `9070dfb` passes 635 of 635):
1. `dotnet test SpectrumVisualizer.slnx --filter "Area=Trend"` passes, including:
   - `FrameContinuityTests`: `TheFirstFrameIsTheBaselineAndBreaksNothing`; `ASeqStepOfOneBreaksNothing`;
     `ASeqStepOfThreeBreaksWithTwoLost`; `AHeartbeatRepeatingTheSeqBreaksNothing`;
     `AHeartbeatReportingALossBreaksWithItsCount`;
     `AGapOverTenSecondsWithoutAnyFrameBreaksAndIgnoresTheSeqStep` (10.001 s, seq 7 then 1: one NoSpectra break, no
     Loss); `HeartbeatsOnlyForOverTenSecondsBreakAtTheNextSpectrum` (spectra stop, heartbeats every 3 s for 15 s, then a
     spectrum: one NoSpectra break at it, the heartbeats' `seq` still counted);
     `ASpectrumExactlyTenSecondsAfterTheLastBreaksNothing`; `ASeqGapExactlyTenSecondsAfterTheLastFrameCountsTheLoss`
     (seq 7 at 0 s, seq 9 at 10.0 s: Loss 1, no NoSpectra); `TheMeasuringDevicesFailedBitMarksAFailure` (either
     device, in a heartbeat or a spectrum frame); `TheIdleDevicesFailedBitMarksNothing`;
     `AFailedBitBeforeTheFirstSpectrumMarksNothing`; `ASpectrumOfTheOtherDeviceMovesTheFailureToItsBit`; `ABoardRestartInsideTenSecondsBreaksWithTheLinksLossCount` (seq 55 then 1: 201 lost).
   - `IntensityCachePointTests`, `IntensityCacheKeyChangeTests`, `IntensityCacheHistoryTests` and
     `IntensityCacheDisposeTests`: `ASpectrumAddsOnePointAtTheFrameMean` (equals `QueryWindows.MeanOf` of the window);
     `ADarkSpectrumAddsAPointAtZero`; `ASpectrumOfADeviceThatDoesNotCoverTheWavelengthAddsAnUncoveredPoint`;
     `AHeartbeatAddsNoPoint`; `TheFirstSpectrumSetsTheKeyWithoutAReRead`;
     `AKeyChangeRecomputesTheRecentSpectraAndAsksForAReRead` (wavelength or `delta`);
     `AKeyChangeDropsThePointsOlderThanTheRecentSpectraAndKeepsEveryBreakAndMark`;
     `AReturnToThePreviousKeyAsksForAReReadLikeAnyChange`; `AfterTheDisposeFramesAndChunksAreIgnored`; `FoldingWhileFramesAndChunksLandDoesNotThrow`;
     `PointsBreaksAndMarksOlderThanADayBeforeNowAreDropped` (a `FakeTimeProvider`);
     `AFoldOfADayCountsOnlyThePointsOfItsRange` (the counts of a 10 min and a 24 h fold over a day of points; the
     benchmark's 10 min fold row guards the cost of the binary search);
     `AHistoryChunkOfAnOldKeyGenerationIsIgnored`; `HistoryChunksLandInTimeOrderWhateverTheirArrivalOrder`;
     `TheFoldEqualsTheStoreSeriesOverTheSameFrames` (one set of frames written to a temporary store and fed to the
     cache: every `IntensityColumn` of the fold equals the store's `IntensitySeriesAsync` column for the same query,
     breaks and marks aside); `AReReadAtANewKeyKeepsTheLiveBreaks` (frames with a `seq` gap and a silence fed live,
     the key changed, the chunk landed from the store: the breaks of the fold are the live ones);
     `TheMomentLookupReadsTheRecentSpectraAtOrBefore`.
   - `TrendHistoryPlanTests`: `TheVisibleRangeComesFirst`; `TheRestFollowsNewestFirstInHourChunks`;
     `EveryRangeEndsAtTheRecentSpectraStart`; `AVisibleRangeInsideTheRecentSpectraReadsNothingFirst`;
     `TheRangesCoverTheDayWithoutOverlap`.
   - `TrendNavigationModelFollowTests`, `TrendNavigationModelWidthTests` and `TrendNavigationModelZoomTests`, copied
     from SemiPlot (`OnLiveEdgeWhenStickyAdvancesWindowAndKeepsWidth`,
     `PanPastLiveEdgeIntoThePastAutoDetachesSticky`, `PanWhileLiveEdgeStaysInsideWindowKeepsSticky`, the zoom anchor and
     the left clamp) with the widths 10 s to 24 h.
   - `EnvelopeColumnTests` (UI): `AColumnWithValuesBecomesAnEnvelopeColumnAtItsCentre`;
     `AnEmptyColumnWithoutABreakIsLeftOut`; `AnUncoveredEmptyColumnBecomesAGap`; `ABreakInAColumnWithValuesPutsAGapBeforeIt`.
     `TrendColumnsTests` (Core): `TheColumnWidthIsWholeMilliseconds` (span over pixels, 256 to 2048 columns, ceiling to
     1 ms); `FailedColumnsLessThanFiveSecondsApartJoinIntoOneRange`.
   - `EnvelopePathTests` copied (`VisibleRangeKeepsOneColumnBeyondEachEdgeOfTheWindow`, `BuildDegenerateColumnIsOnePoint`,
     `BuildColumnBelowThePreviousPointEntersAtItsMax`, `AGapColumnBreaksThePath`) and
     `OnlyADegenerateColumnBetweenGapsIsLone`; `EnvelopeLineTests.ALoneColumnWithOneValueDrawsAMark`.
   - `TrendChartViewModelNavigationTests` and `TrendChartViewModelDisplayTests`: `TheDefaultWindowIsTenMinutesAndFollowsLive`; `ANewFrameMovesTheWindowWhileFollowing`;
     `APanIntoThePastStopsFollowingAndANewFrameKeepsTheWindow`; `FollowLiveReturnsToTheNewestFrame`;
     `AWheelStepZoomsAroundTheCursorTime`; `ThePresetSetsTheWidthAndKeepsFollowing`;
     `FollowingLeavesAnEmptySpanBeforeTheFirstPoint` (10 min window, 2 min of points: `From` = now - 10 min);
     `APanStopsAtTheFirstPoint`; `AZoomOutStopsAtTheFirstPoint`; `TheYAxisLabelNamesTheKey`; `TheYLimitsPadTheVisibleEnvelope`;
     `AFlatVisibleRangeTakesPlusMinusOne`; `TheShownMomentDrawsAMarker`; `AFailureColumnDrawsTheStrip`;
     `OnlyTheFailedBitOfTheMeasuringDeviceDrawsTheStrip` (ATP-2000 spectra carrying the ATP-8000 bit draw no strip and
     no failure line; their own bit draws both); `SpectraFifteenSecondsApartWithHeartbeatsBetweenDrawAMarkEach`.
   - `TrendChartViewModelRenderTests`: `ARangeWithGapsRendersWithFiniteLimits`; `AnAllGapRangeRendersTheEmptyAxes`.
   - `TrendReadoutTests`: the time in local `HH:mm:ss`; one value; a min-max range; "No data"; "{n} spectra lost",
     also 201 after a board restart; "No spectra" for a NoSpectra gap, with "Measurement failed: ATP-8000" under it when
     a heartbeat in the gap carried the bit; "Spectrometer does not cover {nm} nm"; every text from `Resources`.
   - `TrendFeedRereadTests`, `TrendFeedMomentTests` and `TrendFeedFailureTests`: `TheFeedFillsTheCacheFromTheDistributor`; `AKeyChangeReadsTheVisibleRangeFirst`;
     `ASecondKeyChangeCancelsTheReadInFlight`; `KeyChangesInsideTheThrottleReadOnce`;
     `AFailedReadReportsOnceAndKeepsTheLivePart`;
     `AReadUsesTheWindowFromBeforeItsFirstRangeNotALaterPan` (a pan before the UI dispatch moves the read's window, a
     pan during the read changes no range); `EveryKeyChangeLogsAtDebugWithItsRunningCount`;
     `BackToLiveCancelsTheMomentReadInFlight`;
     `AReadThatFailsAfterTheStopReportsNothing`; `StopCancelsTheReadsAndAwaitsThem` (a re-read and a click read both
     held at a gate). The feed tests run the feed's UI hops on the headless dispatcher.
   - `TrendChartViewTests`: `ThePlotIsCachedThroughItsOnlyAncestor`; `ABurstOfRedrawRequestsJoinsOneFrame`;
     `TheInputProcessorIsDisabledAndTheWheelUnhandled`; `ADragPastFourPixelsPansAndStopsFollowing`;
     `AClickWithinFourPixelsShowsTheMoment`; `TheHoverOverlayFollowsThePointerAndHidesOnExit`;
     `TheHoverOverlayHidesWhileDragging`; `TheEmptyStateShowsUntilTheFirstPoint`.
   - `OverviewViewModelTests` and `OverviewViewTests`: the band spans the cache extent; the highlight shows the window
     and keeps 6 px; a press navigates the trend to that time and stops following; the band refreshes after a history
     chunk lands; spectra 15 s apart show a 3 px mark each
     (`SpectraFifteenSecondsApartWithHeartbeatsBetweenShowAMarkEach`).
2. `dotnet test SpectrumVisualizer.slnx --filter "Area=Store"` passes, with `SpectrumHistoryPointsTests`:
   `PointsCarryTheWindowMeanPerSpectrum`; `PointsOfAnUncoveredDeviceAreNaN`; `ACorruptRowIsNaN`;
   `PointsComeInReceiveOrder`; `ACancelledReadThrowsOperationCanceled`; and the existing series tests unchanged over the
   shared row loop.
3. `dotnet test SpectrumVisualizer.slnx --filter "Area=Spectrum"` passes, including `SwitchingSpectrumSourceTests`
   (`LiveByDefault`; `ShowMomentSwitchesCurrentAndSignals`; `LiveFramesWhileHistoricalKeepTheMoment`;
   `ReturnToLiveShowsTheLatestLiveSpectrum`; `EveryShownMomentCarriesANewMeasurement`) and in
   `SpectrumChartViewModelHistoricalScaleTests`: `AHistoricalSpectrumDoesNotWriteLastNonDark`,
   `AToggleWhileAHistoricalSpectrumIsShownDoesNotWriteLastNonDark`, `AHistoricalDarkSpectrumUsesTheLiveReferenceOfItsDevice`,
   `AHistoricalSpectrumOfTheFrozenDeviceKeepsTheFrozenLimits`, `AHistoricalSpectrumOfTheOtherDeviceReturnsToFollowing`,
   `BackToLiveOnTheOtherDeviceReturnsToFollowing`; `SpectrumViewModelCommandTests.BackToLiveIsEnabledOnlyWhileHistorical`,
   `TheHistoricalLineShowsTheMomentInLocalTime`.
4. `dotnet test SpectrumVisualizer.slnx --filter "Area=MainWindow"` passes, including `MainWindowTrendLayoutTests`
   (rows of the content area; the trend under the spectrum chart, the overview under the trend, the side pane across
   all three; the side pane width unchanged at `SpectrumPanel.PaneWidth`) and `MainWindowTextFitTests` with the new
   pane texts, the trend axis labels and the readout lines in English and Russian, and `MinimumWidth` 1256 unchanged;
   `MainWindowChartHeightTests` at 1280 x 800, 1366 x 768 (client 1366 x 689) and 1280 x 1024, with and without the
   message panel, and at the 688 px minimum: the spectrum and trend rows split 3 : 2 above their `MinHeight`, each data
   area keeps at least 120 px, nothing runs past the window, the overview stays 37 px;
   `MainWindowLayoutTests.AWindowTallerThanTheScreenOpensInsideItsWorkArea` (the headless screen is a 1920 x 1280 work
   area and its window has no frame, so only the clamp of a 2000 px window to 1280 px shows there; the frame allowance
   and the floor at `MinHeight` wait for the smoke run on a 1366 x 768 screen, step 14).
5. `dotnet test SpectrumVisualizer.slnx --filter "Area=Di"` passes: the cache, the history, the feed, the switching
   source and the trend view models resolve as singletons (`CompositionRootTests.TheTrendPartsAreOneInstanceEach`,
   `TheTrendViewModelSharesTheContainersChartAndOverview`, `TheMainWindowShowsTheContainersTrendViewModel`). The
   container registers `SwitchingSpectrumSource` over `LiveSpectrumSource` and has no `IDisplayedSpectrumSource`
   registration; `SpectrumViewModel`, `TrendChartViewModel` and `TrendFeed` take `SwitchingSpectrumSource`.
   `AppLifecycleTrendTests.OpeningTheWindowStartsTheTrendFeedBeforeTheDistributor` and
   `ClosingTheWindowWaitsForTheTrendFeedsReadBeforeTheStoreStops` (a re-read held at a gate: the store's stop line
   comes only after the gate opens).
6. `dotnet test SpectrumVisualizer.slnx --filter "Area=Resources"` passes with every new resx key in the Russian set.
7. `dotnet test SpectrumVisualizer.slnx` passes 635 plus every test this slice adds, none skipped: 921 of 921 at the
   end of the run.
8. `dotnet build SpectrumVisualizer.slnx -c Release` 0 warnings; `dotnet format SpectrumVisualizer.slnx
   --verify-no-changes` and `dotnet terse SpectrumVisualizer` exit 0; `dotnet build SpectrumVisualizer.sln` and
   `dotnet test SpectrumVisualizer.Tests` pass 13 of 13.
9. The `git grep` commands of `ui-theme.md:90` and `:103` print nothing.
10. Timing on a 24 h store file:
    `dotnet run -c Release --project SpectrumVisualizer/SpectrumVisualizer.Tools.StoreBenchmark` prints these rows, each
    as first run and warm median (min to max) of 5:

    | Row | Target |
    | --- | --- |
    | Trend cache fold, 24 h (172 800 points) to 2048 columns | warm median at most 20 ms |
    | Trend cache fold, 10 min to 2048 columns | warm median at most 2 ms |
    | Trend re-read at a new wavelength: visible 10 min first (ms to its landing) | warm median at most 100 ms |
    | Trend re-read at a new wavelength: the whole day in its plan (ms to the last chunk) | warm median at most 2 000 ms |

    The fold row is the cost of one cache-fed redraw of the widest window; the re-read rows run `TrendHistoryPlan` and
    `IntensityPointsAsync` against the churned file at a wavelength other than the stored one, so every row reads its
    pixels as after a SCADA change. `StoreBenchmarkRunTests.ASmallRunPrintsEveryMeasurementAndDeletesItsFile` holds
    the new rows on 2 000 rows.

    Measured on the dev laptop: Intel Core i7-1355U, 32 GB RAM, Micron MTFDKBA1T0QFM 1 TB NVMe SSD, Windows 11; the OS
    file cache is not dropped, so every number is a cached read. 172 800 ATP-2000 rows, re-read at 700 and 750 nm. The
    Task 4 run (2026-11-04, Task 4 working tree) gives churned warm medians. The re-run (2026-11-04, at `ea86598`)
    followed the review phases, which changed the fold's call path (`TrendTimeline`, `FoldCurrent`); cells read
    "first; warm median (min to max)" of 5, F the fresh file, C the churned one. The run used `--folder` in the
    session scratchpad, and that folder was removed after it.

    | Row | Target (warm median, churned) | Task 4, C | Re-run at `ea86598` | Verdict |
    | --- | --- | --- | --- | --- |
    | Cache fold, 24 h, 2048 columns | at most 20 ms | 1.3 ms | F 1.2; 1.1 (1.0 to 1.1) ms. C 1.4; 1.4 (1.4 to 1.5) ms | met |
    | Cache fold, 10 min, 2048 columns | at most 2 ms | 0.1 ms | F 0.1; 0.1 (0.1 to 0.1) ms. C 0.1; 0.1 (0.1 to 0.1) ms | met |
    | Re-read, visible 10 min, to its landing | at most 100 ms | 4.6 ms | F 9.5; 4.2 (3.6 to 8.5) ms. C 5.2; 4.4 (4.3 to 4.5) ms | met |
    | Re-read, whole day, to the last chunk | at most 2 000 ms | 698.6 ms | F 604.8; 812.3 (569.6 to 847.3) ms. C 718.8; 720.4 (694.7 to 754.9) ms | met |

    The fresh whole-day row spreads from 569.6 to 847.3 ms, and its median lies above its first run; that spread is
    run-to-run noise on one machine, not a regression. The churned rows moved by at most 0.1 ms on the folds and by
    21.8 ms (3 %) on the whole day against the Task 4 run, inside the same kind of spread. Cold-cache numbers on the
    operator PC are unmeasured.

Manual smoke checklist (owner; com0com pair `COM10`-`COM11`, the emulator on `COM10`, the app on `COM11`; `emu` stands
for `dotnet run --project SpectrumVisualizer/SpectrumVisualizer.Tools.BoardEmulator --`):
1. Start the app. The trend shows its empty-state line, the overview is empty, the time window reads "10 min", Follow
   live is disabled.
2. Run `emu --port COM10 --device 2000`, select `COM11`, Connect. A point per second appears at the right edge of the
   trend; the Y label reads "Intensity" over "650 nm ±10 nm"; the time axis shows local time; the window's right edge
   moves with the clock; the overview band fills the strip and the highlight covers it until there is more data than
   the window.
3. Hover the trend: a vertical line follows the pointer and the readout shows the local time and the value. Move off
   the plot: both disappear.
4. With more than 10 min of data, or with the 1 min preset, drag the trend to the right by more than 4 px: the window
   moves into the past, Follow live turns enabled, new points no longer move the window. Press Follow live: the window
   jumps to now and follows again.
5. After a pan has stopped following, wheel over the middle of the trend: the time under the cursor stays under the
   cursor while the span narrows or widens (a zoom-out stops at the first point). The preset box turns empty after the
   zoom; pick each preset, also from the empty box: the width changes, and while following the right edge stays at
   now. While following, wheel in at the right edge: following keeps.
6. Click a point on the trend without moving the mouse: the spectrum chart shows that moment's spectrum, a line over
   the chart reads "Spectrum from HH:mm:ss" ("Спектр от HH:mm:ss" in Russian), the Updated row shows that time, a marker line stands at that moment on
   the trend, Back to live is enabled. New frames do not replace the shown spectrum. Press Back to live: the latest
   spectrum shows, the line and the marker disappear.
7. With more than 10 min of data (or the 1 min preset), restart the emulator with
   `--device 2000 --wavelength 400 --delta 25`. Within about 1 s the trend re-reads: the Y label changes to
   400 nm ±25 nm and the whole visible history is redrawn at 400 nm; older history fills in after.
   A gap stands at the restart (the emulator's `seq` starts again; within 10 s the readout reads the link's count, for
   example "199 spectra lost"). Spectra the store did not write are absent from the redrawn history.
8. Stop the emulator, wait more than 10 s (15 s), then start it again with `--device 2000`: the empty span at the
   right edge grows during the 15 s wait; the line breaks over the silence; hover the gap: "No spectra". Stop it
   again, wait more than 10 s, then start it with `--device 2000 --rate 0` for 15 s (heartbeats only) and back to
   `--device 2000 --rate 1`: the line breaks again and the readout says "No spectra".

   Each restart in steps 9 to 12 that comes within 10 s of the last frame also breaks the line once at the restart with
   the link's loss count; that break is not the one the step checks.
9. Restart with `--device 2000 --bad-crc-every 5`: the line breaks at every fifth spectrum; hover a break: "1 spectra
   lost" (the Russian form follows the resx text).
10. Restart with `--device 8000 --wavelength 650`: ATP-8000 does not cover 650 nm, so the trend shows a gap and the
    readout says "Spectrometer does not cover 650 nm". Restart with `--device 8000 --wavelength 950` and then
    `--device 2000 --wavelength 950`: both devices cover 950 nm, so after the break at the restart the line resumes
    with no uncovered gap.
11. Restart with `--device 2000 --dark-every 3`: every third point drops to 0.
12. Restart with `--device 2000 --both-connected --failed 8000`: no failure strip shows and the readout has no
    "Measurement failed" line. The ATP-2000 spectra carry the ATP-8000 bit, the idle device's, and the trend marks only
    the measuring device's bit; at `--rate 1` the emulator sends no heartbeats. Only the emulated device's own bit
    shows in heartbeats only (`board-protocol.md:360`). Then run `--device 2000 --rate 0 --failed 2000` for 15 s and
    back to `--device 2000`: the strip covers the heartbeat-only stretch as one bar, not dashes, and hovering its gap
    reads "No spectra" with "Measurement failed: ATP-2000" under it.
13. Pick the 24 h preset after several minutes: the whole history shows without delay. Pick the 1 min or 10 min preset
    and press in the overview: the trend window moves to that time and stops following.
14. Resize the window to its 1256 px minimum and to full screen: the side pane keeps 392 px, the charts take the spare
    width. At the minimum height show the message panel (for example a failed Connect): the side pane scrolls, no text
    wraps; a short window alone leaves the pane room at default fonts. On a 1366 x 768 screen the window opens inside
    the work area. With a Russian Windows UI every new text is Russian.
15. At 1366 x 768 (maximised, taskbar shown) both charts keep a readable data area; judge the 3 : 2 split by eye at
    1280 x 800 and 1280 x 1024.

## Progress Tracking
- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update plan if implementation deviates from original scope

## Solution Overview
- **Folders.** `SpectrumVisualizer.Core/Trend/` holds every rule that needs no UI type: the continuity rule, the
  intensity cache and its `TrendTimeline`, the history plan, the navigation model, the column width and query
  (`TrendColumns`), the overview geometry. The store's point records, `TrendPoint` and `IntensityChunk`, sit in
  `Core/Store/` beside the read that returns them. The 10 s silence limit is `LinkTiming.SilenceLimit` in
  `Core/Protocol/`, shared by the link health rules and the continuity rule. The benchmark tool references Core only,
  so it times the same cache and plan the app runs. `SpectrumVisualizer.UI/Trend/` holds the ScottPlot and Avalonia
  pieces: `EnvelopeColumn` with the column mapping, the envelope plottable, the failure strip, `TrendPlotLayers`, the
  trend chart view model and view with `TrendCursorOverlay`, the overview, the feed and the trend view model.
  `ChartFrames` in `UI/Chart/` paces the redraws of both chart views; `ClockText.TimeOfDay` in `UI/Localization/`
  formats the readout time and the historical line. The switching source sits in `UI/Spectrum/` beside the seam it
  implements.
- **Continuity rule** (`FrameContinuity`, Core). It sees every frame in time order, spectra and heartbeats, and owns a
  `SequenceTracker`. Session rule: when more than 10 s pass without any frame (the link's silence limit), it calls
  `Reset()` before it observes the next frame, so the `seq` step across that gap never counts as loss. A reconnect, an
  unplug, an emulator restart after 10 s or a stopped board all fall under it. Up to 10 s, `Observe` gives the lost
  count of spectra and heartbeats alike, and a count above 0 emits a Loss break with it. A board restart inside 10 s
  shows as a Loss break with the link's count (`board-protocol.md:144`), for example "199 spectra lost"; the readout
  accepts that number. A spectrum that comes more than 10 s after the previous spectrum emits a NoSpectra break,
  whatever heartbeats came between, so a heartbeat-only stretch breaks the line. The measuring device's measurement-failed bit
  in a frame's state, heartbeat or spectrum, emits a Failure mark. The measuring device is the device of the spectrum,
  or of the last spectrum before a heartbeat; before the first spectrum no bit marks. The board keeps the idle
  device's bit until that device measures again (`main.c:502`, `:1064`). A frame the link channel dropped leaves a `seq` gap in the
  distributor stream, so it breaks the line too.
- **Breaks belong to the run, not to the key.** Breaks and marks come from `seq`, time and `state`, never from the
  wavelength or `delta`. The instance file is wiped at open (`StoreFiles.cs:24-30`) and the trend feed subscribes before
  the distributor starts, so every stored frame passed through the cache first. The cache therefore keeps its breaks
  and marks for its whole life, and a key change recomputes point values only; the store read returns points only.
- **Intensity cache** (`IntensityCache`, Core, one lock; the time-ordered points, breaks and marks and their fold live in
  `TrendTimeline`, which the cache calls under that lock). Points are `(UnixMs, Value)` with NaN for an uncovered
  spectrum, plus breaks `(UnixMs, Lost)`, 0 lost for a NoSpectra break, and failure marks `(UnixMs, BoardState)`. The cache owns the key, the
  `QueryWindows` of the key, a `FrameContinuity`, the recent spectra (the last 120 s of spectrum frames by receive
  time, references only; the pixel arrays are immutable) and a `TimeProvider`. `Add(ReceivedFrame)` passes every frame
  to the continuity rule and keeps its breaks and marks, then for a spectrum:
  - the first spectrum sets the key and asks for nothing;
  - a spectrum with another wavelength or `delta`, a return to the previous key included, bumps the key generation,
    drops the points older than the recent spectra, recomputes the recent spectra's values at the new key, and raises
    `ReReadRequested(generation, key, recentStart)`; the feed logs the change at Debug with the generation, which
    counts the key changes;
  - otherwise it adds one point: `QueryWindows.MeanOf` over the frame's window bytes when the device covers the
    wavelength, NaN otherwise.
  After each add it drops points, breaks and marks older than 24 h before `TimeProvider.GetUtcNow()`, the store's
  retention rule (`overview.md`, The writer service). `Changed` signals after every change. `Fold(IntensityQuery)` finds the first
  point at or after `From` by binary search over the time-ordered points, breaks and marks, and feeds the range into
  `IntensityBuckets`, so the cache and the store fold one way and a 10 min fold costs the points of 10 min.
  `FoldCurrent(queryFor)` reads the extent, builds the query and folds under one lock hold. `LandHistory(generation,
  chunk)` inserts the points of a chunk read from the store when its generation is current, in time order; history
  chunks never overlap each other or the recent spectra, because every planned range ends at `recentStart`.
  `SpectrumAt(moment)` answers from the recent spectra when the moment is inside them, null otherwise. After `Dispose`
  the cache ignores frames and chunks.
  Memory: 172 800 points of 16 bytes plus the breaks, about 3 MB for a day at 2 spectra per second.
- **Why recent spectra.** The store writes in batches behind its queue (`overview.md`, Writes), so the newest rows may
  not be in the file when a re-read starts. The cache recomputes the last 120 s from frames it already holds and asks
  the store only for older rows; 120 s covers the writer's normal lag, and the boundary holds no double point.
  `recentStart` is truncated to whole ms, and the recent spectra are evicted on their ms-truncated stamps, so the
  store's `received_at < recentStart` and the recent set meet exactly. Frames the store never wrote (a full queue, a
  failed batch, both counted as dropped) are absent from the history after a re-read at the new key, although the live
  trend showed them; that is accepted, and the dropped-frames count in the status bar already reports them.
- **Store read** (`SpectrumHistory.IntensityPointsAsync(wavelength, delta, from, to, token)`). It streams the spectrum
  rows of the range through the same `CASE ... substr` statement and the same row loop as `ReadSeries`, and returns an
  `IntensityChunk` of `(UnixMs, Value)` points, NaN for an uncovered device or a corrupt row. `ReadSeries` becomes the
  bucket fold over that loop, so both reads share one statement and one row loop. The statement gains no column, so
  the `GetBytes` constraint of `SpectrumHistory.cs:155` stays met.
- **History plan** (`TrendHistoryPlan`, Core). For a key change it returns ranges in order: the visible window clipped
  to `[newest - 24 h, recentStart)` first, then the rest of that day in 1 h chunks, newest first. SemiPlot reads the
  visible window, then one expanded request (`ChartHistoryPlanner.cs:47-85`); the hour chunks here let the trend fill
  progressively while a day of pixel pages is read, and let a cancel land within one chunk.
- **Feed** (`TrendFeed`, UI). `Start()` subscribes `FrameDistributor.Frames` with an observer that calls
  `IntensityCache.Add` (a mean over at most 2048 pixels; it never touches SQLite and never blocks), the same way the
  store only enqueues. The re-read pipeline copies the shape of `MinimapBandFeed.cs:112-119`:
  `ReReadRequested.Throttle(500 ms, throttleScheduler).ObserveOn(uiScheduler)`, the window snapshot, then
  `Select(Read).Switch()`, where `Read` is `Observable.Create`
  with a `CancellationToken` that runs the plan's ranges one after another and lands each chunk. A newer key change
  disposes the old read, which cancels its token; `IntensityPointsAsync` checks it every 256 rows. A request with no
  read maps to an empty observable, so it only cancels. At trigger time the feed moves to the UI scheduler and
  snapshots the visible window from `TrendChartViewModel` into an immutable `TimeRange`, then passes that value to the
  read on the thread pool; nothing reads the navigation model off the UI thread. A failed read reports one
  `TrendHistoryError` (Warning) through `TryReportFailure` and ends that read; the trend keeps only the data since the
  change; a read that ends after the stop reports nothing. The click reads of `TrendViewModel` run through the feed as
  well, and Back to live cancels the click read in flight. `StopAsync()` disposes the subscription and both read pipelines and
  awaits their tasks; `Dispose()` cancels without waiting.
- **Trend chart model** (`TrendChartViewModel`, UI). Owns the `Plot` with its `TrendPlotLayers` (one `EnvelopeLine`,
  one `TrendMarkerStrip` for failure columns, one `VerticalLine` for the shown moment, the bottom axis with
  `DateTimeAutomatic` over local OADate through `LocalTimeAxis`), and a `TrendNavigationModel`. `Refresh()` on the UI
  thread builds an `IntensityQuery` for the window at one column per pixel of the data area
  (`TrendColumns.MinimumColumns` to `MaximumColumns`) through `cache.FoldCurrent`, and `TrendPlotLayers.Draw` maps the
  columns with `EnvelopeColumn.Map` and replaces the envelope's columns under
  `lock (Plot.Sync)`, the lock every ScottPlot render holds; the plottables take no lock of their own. It sets the Y limits
  from the envelope: `min - 5 % .. max + 5 %`, `v - 1 .. v + 1` for a flat range, unchanged when nothing is visible.
  Cache changes reach it through `cache.Changed.Sample(100 ms, uiScheduler)`, a 1 s tick refreshes it while
  following, and navigation changes refresh at once. While following, the window's right edge is now, not the newest
  point. Every refresh requests one redraw; the view serves it in the next display frame through `ChartFrames`, the
  same redraw pacing as the spectrum chart view.
- **Gaps** (`EnvelopeColumn.Map`, UI). A column with values becomes `EnvelopeColumn(centre time, Min, Max, Last)`. An
  empty column with no break and no uncovered spectrum is left out, so the path connects across columns narrower than
  the frame period, and a stretch of more than 10 s without spectra breaks only through its NoSpectra break. An
  empty column with an uncovered spectrum, or any column with a break, adds a NaN gap column at its start before its
  value column, so `EnvelopePath` breaks the line there. A break inside a column that also holds
  values therefore cuts the connector into that column at every zoom; at the 24 h width one lost spectrum shows as a
  one-column cut. A column with a failure mark draws a 4 px strip at the bottom of the data area in
  `AppSeverityErrorBrush`, failed columns less than 5 s apart joined into one span; the readout names the failure, so
  colour never carries it alone.
- **Buckets.** `IntensityBuckets` gains `AddBreak(moment, lost)` and `AddFailure(moment, state)`, and `IntensityColumn`
  gains `Last`, `BreakCount`, `LostCount`, `NoSpectraCount` and `Failed` (the OR of the failure bits). The store's `ReadSeries` adds none
  of the new counts and keeps its columns otherwise equal.
- **Navigation.** `TrendNavigationModel` is copied with its widths changed to 10 s .. 24 h, the history the store keeps.
  The window opens at 10 min, sticky. `OnLiveEdge(now)` sets `From = now - width` while sticky, with no clamp, so
  a run shorter than the window leaves an empty span on the left, as in SemiPlot (`TrendNavigationModel.cs:113-123`),
  and a silence shows as an empty span on the right. A `Pan` or a `Zoom` that leaves now outside the window detaches.
  `Pan`, `Zoom` and a `SetWidth` while not sticky place the window through `PlaceFrom`, the only clamp, which keeps
  `From` at or after the cache's first point (`:94-98`). The view copies SemiPlot's input: `UserInputProcessor.Disable()`,
  `HandleMouseWheelEvent = false`, wheel zoom 0.8 / 1.25 around the cursor time, Shift + wheel pans 10 %, a left drag
  past 4 px pans and detaches, a release inside 4 px is a click. Presets are 1 min, 10 min, 1 h, 6 h and 24 h; the box
  shows the preset of the current width, empty after a zoom off every preset, and a picked preset sets the width and
  keeps the right edge (unless the first-point clamp moves it while not following) and sticky. Follow live jumps to now and turns sticky; it is enabled only while not following.
  AvaPlot 5.1.59 works in DIPs on a canvas Avalonia already scaled, so the hover, the zoom anchor and the click moment
  hold at 125 % (decompiled, no fix needed).
- **Hover readout.** The overlay is SemiPlot's: a `Canvas` over the cached plot with a vertical line and a readout
  `Border`, placed by `TrendCursorOverlay.ShowAt` (after SemiPlot's `ChartCursorOverlay.Project`) and hidden by its
  `Hide` while dragging and on pointer exit. It moves without a plot redraw, and flips left of the line at the plot's
  right edge. Its text comes from the column under the pointer in the last fold: the local time `HH:mm:ss` of the
  column (`ClockText.TimeOfDay`),
  then one value or "min – max" in `G6` under the current culture and the spectrum count, or the gap reason ("No
  data", "{0} spectra lost", "No spectra", "Spectrometer does not cover {0} nm"), then "Measurement failed: {device}"
  lines, also inside a NoSpectra gap when a heartbeat there carried the bit. A value column with a break shows the
  break reasons under its values; a column with both reads "No spectra", then the lost count.
- **Click to a moment.** A click passes the cursor time to `TrendViewModel.ShowMoment(moment)`. The requests run
  through `Select(Read).Switch()`, so a second click cancels the first read. `Read` asks the cache's recent spectra
  first, then `SpectrumHistory.SpectrumAtAsync`; the pipeline belongs to `TrendFeed`, so the close chain cancels and
  awaits it. A found spectrum goes to `SwitchingSpectrumSource.ShowMoment` on the UI
  thread; none (before the first row) changes nothing and logs at Debug; a failure reports one `TrendMomentError`
  (Warning).
- **Switching source** (`SwitchingSpectrumSource`, UI, `ReactiveObject`, implements `IDisplayedSpectrumSource`). It
  wraps `LiveSpectrumSource`; the container registers both as themselves and has no `IDisplayedSpectrumSource`
  registration, and `SpectrumViewModel`, `TrendChartViewModel` and `TrendFeed` take `SwitchingSpectrumSource`.
  `Current` is the historical spectrum while one is shown, the live `Current` otherwise. `CurrentChanged` merges the
  live `CurrentChanged` with a private subject that emits on each mode change; the name keeps it apart from
  `ReactiveObject.Changed`. `ShowMoment(measurement, receivedAt)` and `ReturnToLive()` run on the UI thread and set `ShownMoment`
  (`DateTimeOffset?`). Every shown moment carries a new `SpectrumMeasurement` built around the row's pixels:
  `ShowMoment` copies the measurement it gets into a new instance that shares the immutable pixel array, so
  `DistinctUntilChanged` by reference shows every click. While a moment is shown, live signals read the same
  historical record and `DistinctUntilChanged` drops them.
- **Chart rules for a historical spectrum.** `DisplayedSpectrum` gains `IsHistorical`. `Project` writes `LastNonDark`
  only for a live spectrum, also when `SetDisplay` or `ResetScale` re-project a historical one, so a moment from the
  past never becomes the dark-frame reference. A historical dark spectrum takes the live `LastNonDark` of its device,
  or the flat range without one. A frozen scale records the device it was frozen on. A historical spectrum, and the
  first live spectrum after Back to live, keep the frozen limits when their device is that device and return the scale
  to following otherwise, so a moment from the other device never leaves the plot blank, and the operator can compare
  moments of one device at one zoom. Live frames keep today's rule (`overview.md`, The scale).
- **Linked charts.** While a moment is shown, the trend draws `AppMarkerLineBrush` at that moment, and a line over the
  spectrum chart reads "Spectrum from HH:mm:ss" (local time) in `AppSecondaryForegroundBrush`. The Updated row of the
  signal pane already shows the shown spectrum's time.
- **Overview.** SemiPlot's minimap without markers: a 36 px band (`OverviewBand`, after SemiPlot's `MinimapBand`, fill at 0.35 of `AppAccentBrush`'s
  colour) of the cache folded to 250 columns over the cache's extent, with the window highlight (at least 6 px) and a
  hover line. A press or drag in the band moves the trend window there and stops following. The band refolds on the
  SemiPlot cadence clamp(span / 1000, 2 s, 60 s) and after each landed history chunk. It reads the cache, so it needs
  no store read and no cancellation of its own.
- **Lifecycle.** `Opened` starts the store, the trend feed, then the distributor, so both subscriptions exist before
  the first frame. The close chain stops the link, the distributor, the trend feed, then the store, so no reader
  connection of the trend holds a snapshot when the store checkpoints.
- **Layout** (`dev:desktop-ui`). The window stays resizable and its width needs do not grow: the trend stacks under the
  spectrum chart in the left column, and the side pane keeps 392 px.
  - `ContentArea` gets `RowDefinitions="3*,2*,Auto"`: `SpectrumPane` (row 0), `TrendPane` (row 1, a 1 px top border in
    `AppBorderBrush`), `OverviewPane` (row 2, 36 px band plus a 1 px top border, 37 px). The spectrum takes the larger
    share because it is the primary live view. ScottPlot's axis margins, measured in Task 14, are 66.2 px on the
    spectrum and 82.2 px on the trend (its time ticks take two lines, date over time), plus the trend pane's 1 px
    border. The rows carry `MinHeight` 192 (spectrum) and 208 (trend), so each data area keeps at least 120 px; the
    window's `MinHeight` is 688 (`MainWindow.MinimumHeight`), and the message panel's list scrolls past 64 px (one
    three-line entry), so the panel stands at most 133 px.
  - A 200 px data floor in both charts does not fit 1366 x 768: 200 + 66.2 and 200 + 83.2 make 549.4 px, the fixed rows
    (menu 32, connection row 49, overview 37, status bar 35.6) make 153.6 px, so the window needs 703 px of client height
    before the message panel; a maximised 1366 x 768 window gives 689. The floor is 120 px instead, and nothing is cut
    with the message panel shown.
  - Heights measured in Task 14 (`MainWindowChartHeightTests`; headless rows, the status bar corrected to the tallest
    real face, Segoe UI at 14 px, 35.6 px against the headless 33; data areas from ScottPlot rendering at the pane's
    size; the message panel at its 133 px cap):

    | Window | Client height | Message panel | Content row | Spectrum row (data) | Trend row (data) |
    | --- | --- | --- | --- | --- | --- |
    | 1280 x 800 | 800 | hidden | 686 | 389 (322.8) | 260 (176.8) |
    | 1280 x 800 | 800 | 133 | 553 | 308 (241.8) | 208 (124.8), floor |
    | 1366 x 768, maximised over a 48 px taskbar and a 31 px title bar | 689 | hidden | 575 | 323 (256.8) | 215 (131.8) |
    | 1366 x 768, maximised | 689 | 133 | 442 | 197 (130.8) | 208 (124.8), floor |
    | 1280 x 1024 | 1024 | hidden | 910 | 524 (457.8) | 349 (265.8) |
    | 1280 x 1024 | 1024 | 133 | 777 | 444 (377.8) | 296 (212.8) |
    | the minimum | 688 | 133 | 441 | 196 (129.8) | 208 (124.8), floor |

    Menu 32, connection row 49, overview 37 in every row. Above their floors the charts split 3 : 2. At 1366 x 768 with
    the message panel shown the rows need 686.6 px of the 689, so the status bar stays inside the window.
  - `SignalPane` spans the three rows. Its form wraps in a vertical `ScrollViewer`, so the pane scrolls instead of
    clipping when the shown message panel takes its height, and no row ever disappears; at default fonts a short
    window alone leaves it room. The window opens inside the screen's work area.
  - The Chart section gains a "Back to live" button under Stick to zero, enabled only while a moment is shown.
  - A new "Trend" section follows (16 above, 8 below): a "Time window" label with a preset `ComboBox` 120 px wide (a
    short code, from the width table), then a "Follow live" button in the field column, `HorizontalAlignment="Left"`.
  - The trend's Y label names the key on two lines ("Intensity" over "650 nm ±10 nm"), so no pane row repeats it. On
    one line the widest key ("Интенсивность на 2461 нм ±255 нм", 256.4 px in Segoe UI 16) overruns the 154.8 px the
    plot gives a label at the trend's floor; on two lines the longest line is 133.4 px (ru) and 137.6 px (en).
  - Empty state over the trend until the first point: "No trend yet. It appears with the first spectrum."

## Technical Details
Continuity rule (`FrameContinuity.Observe(ReceivedFrame) -> Continuity`):

| Case | Result |
| --- | --- |
| first frame | baseline; nothing |
| more than 10 s after the previous frame of any kind | tracker `Reset()`; this frame is the new baseline, no Loss |
| up to 10 s, `SequenceTracker.Observe` returns k > 0 | Loss break at this frame with k lost |
| up to 10 s, k = 0 | nothing |
| a spectrum more than 10 s after the previous spectrum | NoSpectra break, 0 lost, at this spectrum, besides the above |
| state has the measuring device's failed bit (`Atp2000MeasurementFailed` or `Atp8000MeasurementFailed`) | a failure mark at this frame with that bit, besides the above |

Cache key: `(WavelengthNm, Delta)` of the latest spectrum frame. Recent spectra: 120 s by ms-truncated receive time.
Retention: 24 h before now. Throttle of re-reads: 500 ms. Cache `Changed` sample: 100 ms.

Column mapping (`EnvelopeColumn.Map(series, toAxis)`, UI), per column:

| Column | Envelope output |
| --- | --- |
| `Count > 0`, no break | `(Start + width / 2, Min, Max, Last)` |
| `Count > 0`, `BreakCount > 0` | gap at `Start`, then the value column |
| `Count = 0`, `BreakCount > 0` or `UncoveredCount > 0` | gap at `Start` |
| `Count = 0`, nothing else | none |

Column width (`TrendColumns.ColumnWidth`, Core): `ceil(span / clamp(dataAreaPixels, 256, 2048))` in whole ms, at
least 1 ms. X values are local OADate through `LocalTimeAxis.ToAxis`.

Store read result (`Core/Store/IntensityChunk.cs`): `IntensityChunk(DateTimeOffset From, TrendPoint[] Points)`; `TrendPoint(long UnixMs, double
Value)`, NaN for uncovered or corrupt. Cache-only types: `TrendBreak(long UnixMs, int Lost)`, 0 lost for NoSpectra;
`TrendFailure(long UnixMs, BoardState State)`; `TrendExtent(TrendKey Key, DateTimeOffset First, DateTimeOffset
Newest)` and `TrendFold(TrendExtent Extent, IntensityQuery Query, IntensitySeries Series)`.

New resx keys (en / ru):

| Key | English | Russian |
| --- | --- | --- |
| `TrendTitle` | Trend | Тренд |
| `TrendTimeWindow` | Time window | Интервал |
| `TrendFollowLive` | Follow live | Следить за данными |
| `TrendPresetMinutesFormat` | {0} min | {0} мин |
| `TrendPresetHoursFormat` | {0} h | {0} ч |
| `TrendAxisTime` | Time | Время |
| `TrendAxisIntensity` | Intensity | Интенсивность |
| `TrendAxisIntensityFormat` | Intensity, a line break, {0} nm ±{1} nm | Интенсивность, a line break, {0} нм ±{1} нм |
| `TrendEmpty` | No trend yet. It appears with the first spectrum. | Тренда пока нет. Он появится с первым спектром. |
| `TrendReadoutRangeFormat` | {0} – {1} | {0} – {1} |
| `TrendReadoutCountFormat` | Spectra: {0} | Спектров: {0} |
| `TrendReadoutNoData` | No data | Нет данных |
| `TrendReadoutLostFormat` | {0} spectra lost | Потеряно спектров: {0} |
| `TrendReadoutNoSpectra` | No spectra | Нет спектров |
| `TrendReadoutUncoveredFormat` | Spectrometer does not cover {0} nm | {0} нм вне диапазона спектрометра |
| `TrendReadoutFailedFormat` | Measurement failed: {0} | Ошибка измерения: {0} |
| `ChartBackToLive` | Back to live | Вернуться к текущему |
| `SpectrumHistoricalFormat` | Spectrum from {0} | Спектр от {0} |
| `TrendHistoryTitle`, `TrendHistoryDetail` (no remedy key; the mapper passes an empty remedy) | The trend history could not be read / The stored spectra at the new wavelength or window could not be read; the trend shows only the data since the change. / (empty) | Не удалось прочитать историю тренда / Сохранённые спектры на новой длине волны или ширине окна не прочитаны; тренд показывает только данные после смены. / (пусто) |
| `TrendMomentTitle`, `TrendMomentDetail`, `TrendMomentRemedy` | The spectrum of that moment could not be read / The stored spectrum at the clicked time could not be read. / Click another moment. | Не удалось прочитать спектр этого момента / Сохранённый спектр на выбранное время не прочитан. / Выберите другой момент. |

Palette: no new key. Consumers added: `AppAccentBrush` (the envelope line, the overview band at 0.35, the overview
highlight border), `AppAccentFillBrush` (the overview highlight), `AppMarkerLineBrush` (the shown moment on the
trend), `AppSecondaryForegroundBrush` (the hover line, the historical line over the spectrum chart, the trend's empty
state), `AppPanelBackgroundBrush` and `AppBorderBrush` (the readout box, the trend and overview pane borders),
`AppSeverityErrorBrush` (the failure strip).

Data flow:
1. The distributor hands every frame to the store's queue and to `TrendFeed` (distributor task).
2. `TrendFeed` calls `IntensityCache.Add`: a point, a break, a mark, or a key change that raises `ReReadRequested`.
3. `Changed`, sampled at 100 ms, and a 1 s tick while following reach `TrendChartViewModel.Refresh` and the
   overview on the UI thread; the navigation model moves the window's right edge to now while sticky.
4. A key change, throttled at 500 ms, snapshots the visible window on the UI thread and starts one read on the pool:
   the plan's ranges through `IntensityPointsAsync`, each chunk's points landed in the cache; a newer key change
   cancels it.
5. A click reads the moment and switches the spectrum source; the spectrum view model shows it.

## What Goes Where
- **Implementation Steps**: the Core trend rules, the store read, the benchmark rows, the switching source and the
  chart rules, the trend views and view models, the layout, tests and docs in this repo.
- **Post-Completion**: the port-pair smoke run, the real-board check, the benchmark on the operator PC, the issue text.

## Decisions
- The trend's key is the wavelength and `delta` of the latest spectrum frame; any change of either re-reads the history.
- Dark spectra stay on the trend as points at zero, as the store's series already counts them.
- Session rule: more than 10 s without any frame resets the `seq` baseline, so the step across it is not loss. Up to
  10 s a `seq` step above one is loss, heartbeats included; a board restart inside 10 s shows the link's loss count.
- More than 10 s since the last spectrum breaks the line as "No spectra", heartbeats or not.
- Lost spectra, a stretch without spectra and uncovered spectra break the line; a measurement-failed bit marks a strip,
  not a break, and only the measuring device's bit marks: the device of the spectra in that column, or of the last
  spectrum before a heartbeat-only stretch. The store keeps every frame's `state` unchanged.
- A value column with one value between two gaps draws a 3 px square on the trend and at least a 3 px square in the
  overview band, so spectra more than 10 s apart (the angle and zone trigger modes, `main.c:1514-1519`) stay visible
  while each still stands between NoSpectra breaks.
- Breaks and marks belong to the run and survive a key change; a key change recomputes point values only, and the
  store read returns points only. The heartbeat table has no reader in the app and stays written, for diagnostics.
- The cache holds one point per spectrum (time and mean), not columns, so zoom into any part of the day needs no
  store read; the display folds the cache through `IntensityBuckets` on the epoch grid, the same fold as the store.
- The cache keeps the last 120 s of spectrum frames; a key change recomputes them at once and the store read ends at
  their start, which covers the writer's normal lag. Frames the store did not write are absent after a re-read.
- The store read returns points through the series' own statement and row loop.
- Cache retention is 24 h before now, the store's rule. The fold starts by binary search.
- The feed logs every key change at Debug with the generation as the running count. Every key change re-reads, a
  return to the previous key included; the 500 ms throttle joins fast changes into one read.
- The feed snapshots the visible window on the UI thread at trigger time; the read on the pool gets that value only.
- The plan reads the visible window first, then the rest of the day newest first in 1 h chunks; a newer key change
  cancels the read through `Select(Read).Switch()`; key changes are throttled at 500 ms.
- The window opens at 10 min; presets are 1 min, 10 min, 1 h, 6 h and 24 h; the width runs from 10 s to 24 h.
- A historical spectrum never writes `LastNonDark`; a historical dark spectrum takes the live reference of its device.
- Showing a moment, or returning to live, keeps a frozen scale when the spectrum's device is the device the scale was
  frozen on, and returns the scale to following otherwise.
- Every shown moment carries a new `SpectrumMeasurement`; `ShowMoment` copies it around the same pixel array.
- Live follow ends the window at now, refreshed at least once a second, and sets `From = now - width` with no clamp;
  only `PlaceFrom` (pan, zoom, a preset while not following) clamps at the first point. A pan or a zoom that leaves now outside stops following.
- The preset box mirrors the chart's width: the matching preset, or empty after a zoom off every preset.
- Failed columns less than 5 s apart draw one strip span.
- The window opens inside the screen's work area, never below its minimum size.
- The trend and the overview stack under the spectrum chart; the side pane spans all three rows, scrolls vertically and
  keeps 392 px, so `MinimumWidth` stays 1256. The chart rows split 3 : 2, spectrum first, with `MinHeight` 192 and
  208 (a 120 px data floor each); the window's `MinHeight` is 688 and the message panel's list caps at 64 px, so
  every row shows inside a maximised 1366 x 768 window with the message panel shown.
- No new palette key: the trend reads existing keys.
- The trend feed, with its re-read and click reads, stops after the distributor and before the store, so no trend
  reader holds a snapshot at the store's checkpoint.

## Implementation Steps
The task file lists name the files as each task wrote them. Review phase 2 split the large test classes over
abstract bases (`IntensityCacheTests`, `TrendFeedTests`, `TrendChartViewModelTests`, `TrendNavigationModelTests`,
`OverviewGeometryTests`, `SpectrumViewModelTests`; the parts are listed under Decisions made autonomously) and moved
the types named in Solution Overview, Folders.

### Task 1: Continuity rule and bucket extensions
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.Core/Trend/FrameContinuity.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Core/Store/IntensityBuckets.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Core/Trend/FrameContinuityTests.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Core/Store/IntensityBucketsTests.cs`

- [x] write `FrameContinuity` with a `SequenceTracker`, the 10 s session reset and the 10 s NoSpectra rule of
  Technical Details; `TrendBreak` and `TrendFailure` live in the same file as the closed type family they form
  (review phase 1 dropped `BreakKind`: a break with 0 lost is a NoSpectra break)
- [x] add `AddBreak`, `AddFailure` and `Last` to `IntensityBuckets`; extend `IntensityColumn` with `Last`,
  `BreakCount`, `LostCount`, `NoSpectraCount`, `Failed`
- [x] write `FrameContinuityTests` of Acceptance Evidence item 1
- [x] extend the bucket tests: `Last` is the latest value of the column; breaks and failures fold into their columns
  and outside-range moments are ignored; the store series columns stay equal apart from the new fields
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 2: Store points read
**Files:**
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Core/Store/SpectrumHistory.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Core/Store/IntensityChunk.cs` (written in `Core/Trend/`, moved to
  `Core/Store/` in review phase 2)
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Core/Store/SpectrumHistoryPointsTests.cs`

- [x] extract the row loop of `ReadSeries` (`SpectrumHistory.cs:79-109`) into one private loop over
  `(receivedAt, device, windowBytes)` that both reads use; `ReadSeries` keeps its signature and result
- [x] add `IntensityPointsAsync(wavelengthNm, delta, from, to, token)` returning `Result<IntensityChunk>`: the series
  statement unchanged, one `TrendPoint` per row in receive order, NaN for an uncovered device or a corrupt row; no
  heartbeat read
- [x] write `SpectrumHistoryPointsTests` of Acceptance Evidence item 2 over `SpectrumHistoryTestBase`
- [x] run the existing `SpectrumHistorySeriesTests` unchanged; run `dotnet test SpectrumVisualizer.slnx` - must pass
  before next task

### Task 3: Intensity cache and history plan
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.Core/Trend/IntensityCache.cs`, `TrendHistoryPlan.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Core/Trend/IntensityCacheTests.cs`, `TrendHistoryPlanTests.cs`

- [x] write `IntensityCache` as Solution Overview describes (the post-slice check cut the previous key's block,
  `CompleteHistory` and the no-read request named here): one lock, key and generation, the previous key's block,
  recent spectra, breaks and marks kept for the cache's life, retention against a `TimeProvider`, `Add`,
  `Fold(IntensityQuery)` with a binary-searched start, `FoldCurrent`, `Extent`, `LandHistory`, `CompleteHistory`,
  `SpectrumAt`, `Changed`, `HistoryLanded`, `ReReadRequested` (with a no-read request for a return to the previous
  key); the constructor takes the `TimeProvider` only, and `TrendFeed` writes the Debug line per key change with its
  running count; the mean goes through `QueryWindows.MeanOf` over `MemoryMarshal.AsBytes` of the window's pixels
- [x] write `TrendHistoryPlan.For(visibleFrom, visibleTo, newest, recentStart)`
- [x] write `IntensityCacheTests` of Acceptance Evidence item 1; `TheFoldEqualsTheStoreSeriesOverTheSameFrames` and
  `AReReadAtANewKeyKeepsTheLiveBreaks` over a temporary store file
- [x] write `TrendHistoryPlanTests`
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 4: Benchmark rows for the trend
**Files:**
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tools.StoreBenchmark/BenchmarkRun.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Tools/StoreBenchmarkRunTests.cs`

- [x] add the four rows of Acceptance Evidence item 10 after the `SpectrumHistory` rows (`BenchmarkRun.cs:84`): fill an
  `IntensityCache` from the file's frames, time `Fold` over 24 h and 10 min to 2048 columns; time the plan at a
  wavelength other than `options.WavelengthNm` inside the device's table range, to the visible landing and to the last
  chunk. `BenchmarkFile` gained `WindowFrames()` and `FrameAfterWindow(index, wavelengthNm)` so the cache sees the
  same frames the file holds; the cache runs on a clock stopped at the newest row; each re-read run changes the key
  once (stored + 50 nm and + 100 nm in turn) and is timed from that frame's `Add` through `LandHistory` of each chunk
- [x] extend `ASmallRunPrintsEveryMeasurementAndDeletesItsFile` with the four rows
- [x] run the tool with the defaults and record the four rows in this checkbox. Measured 2026-11-04 on the dev laptop
  (Intel Core i7-1355U, SSD, OS file cache not dropped), 172 800 ATP-2000 rows, re-read at 700 and 750 nm; first run,
  then warm median (min to max) of 5:

  | Row | Fresh file | Churned file | Target (warm median) |
  | --- | --- | --- | --- |
  | Trend cache fold, 24 h (172 800 points) to 2048 columns | 1.3; 1.2 (1.2 to 1.2) ms | 1.3; 1.3 (1.3 to 1.3) ms | at most 20 ms, met |
  | Trend cache fold, 10 min to 2048 columns | 0.1; 0.1 (0.1 to 0.1) ms | 0.1; 0.1 (0.1 to 0.1) ms | at most 2 ms, met |
  | Trend re-read, visible 10 min first, to its landing | 14.4; 4.8 (4.4 to 13.2) ms | 5.2; 4.6 (4.5 to 5.4) ms | at most 100 ms, met |
  | Trend re-read, the whole day in its plan, to the last chunk | 706.5; 705.0 (686.3 to 748.1) ms | 704.1; 698.6 (693.3 to 715.9) ms | at most 2 000 ms, met |
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 5: Navigation model, column mapping and overview geometry
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.Core/Trend/TrendNavigationModel.cs`, `TrendColumns.cs`,
  `OverviewGeometry.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Core/Trend/TrendNavigationModelTests.cs`,
  `TrendColumnsTests.cs`, `OverviewGeometryTests.cs`

- [x] copy `TrendNavigationModel` (`SemiPlot/SemiPlot.Core/Trends/TrendNavigationModel.cs`) with widths 10 s .. 24 h,
  on `DateTimeOffset` in UTC. `SetWindow` (the stripped calendar form) and the argument guards are left out;
  `SetWidth` while sticky keeps the right edge with no clamp, so a preset keeps following like `OnLiveEdge`
- [x] write the column mapping and `ColumnWidth` per Technical Details. The mapping takes the time-to-X conversion
  as a `Func<DateTimeOffset, double>`, and the chart passes `LocalTimeAxis.ToAxis`. Review phase 2 moved
  `EnvelopeColumn` and the mapping to `UI/Trend/EnvelopeColumn.cs` as `EnvelopeColumn.Map`, since chart X is a UI
  concern; `TrendColumns` in Core keeps `ColumnWidth` and `QueryThrough`
- [x] copy the parts of `MinimapGeometry` the overview uses (`WindowFraction`, `FractionAt`, `TimeAtFraction`, band
  figures over `IntensitySeries`) without markers and pens; `MarkerSpan` (the 6 px window highlight) is copied as
  `HighlightSpan` with `SpanLeftWithin`; the band takes column `Start` as X and `Last` as the centre
- [x] copy the navigation tests named in Acceptance Evidence item 1 and the geometry tests at `MinimapGeometryTests.cs:57`
  (underscores dropped, as the spectrum-view slice did); write `TrendColumnsTests`
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task (752 of 752)

### Task 6: Switching spectrum source and the historical chart rules
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/SwitchingSpectrumSource.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/IDisplayedSpectrumSource.cs`, `SpectrumChartViewModel.cs`,
  `SpectrumViewModel.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/UiServiceCollectionExtensions.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Spectrum/SwitchingSpectrumSourceTests.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Spectrum/SpectrumChartViewModelScaleTests.cs`,
  `SpectrumViewModelTests.cs`, `UI/Di/CompositionRootTests.cs`

- [x] add `IsHistorical` to `DisplayedSpectrum` (`IDisplayedSpectrumSource.cs:8`)
- [x] write `SwitchingSpectrumSource` over an `IDisplayedSpectrumSource` live source, with `ShowMoment`, `ReturnToLive`
  and `ShownMoment`; the mode subject is private and exposed with `AsObservable()`
- [x] in `SpectrumChartViewModel`: guard the `LastNonDark` write (`:198-201`) with `!IsHistorical`; record the frozen
  device in `ChangeScale`; apply the device rule on a historical spectrum and on the first live one after it
- [x] `SpectrumViewModel` takes `SwitchingSpectrumSource`; adds `BackToLiveCommand` (`canExecute` from `ShownMoment`) and
  `HistoricalText` (`SpectrumHistoricalFormat` over the local `HH:mm:ss` of `ShownMoment`, empty while live)
- [x] register `LiveSpectrumSource` as itself and `SwitchingSpectrumSource` over it, both singletons, replacing
  `UiServiceCollectionExtensions.cs:42`. Review phase 2 removed the `IDisplayedSpectrumSource` registration, because
  no consumer resolves the interface. `SpectrumHistoricalFormat` is added to both resx sets here, because
  `HistoricalText` reads it; the other keys stay with Task 8
- [x] write the tests of Acceptance Evidence item 3; existing view model tests build the switching source over
  `FakeDisplayedSpectrumSource`; `CompositionRootTests.TheDistributorAndTheSpectrumSourcesAreOneInstanceEach` covers
  the registrations
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task (766 of 766)

### Task 7: Envelope line and failure strip
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Trend/EnvelopePath.cs`, `EnvelopeLine.cs`, `TrendMarkerStrip.cs`,
  `LocalTimeAxis.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Trend/EnvelopePathTests.cs`, `EnvelopeLineTests.cs`,
  `TrendMarkerStripTests.cs`, `LocalTimeAxisTests.cs`

- [x] copy `EnvelopePath` (`SemiPlot/SemiPlot.UI/Chart/EnvelopePath.cs:18-148`) without stepped mode, hold and unread
  vertices, and the segment-distance helpers (no hit test here)
- [x] copy `EnvelopeLine` (`SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs:12-124`, `:202-225`) with `ReplaceColumns` only:
  no append, fold, hold, resume gap, log projection, pen style or active width
- [x] write `TrendMarkerStrip`, an `IPlottable` that fills a 4 px bar at the bottom of the data area over each failed
  column; copy `LocalTimeAxis` (`SemiPlot/SemiPlot.UI/Chart/LocalTimeAxis.cs:8-20`)
- [x] copy the tests named in Acceptance Evidence item 1 and `EnvelopeLineTests.cs:49` (render while the UI thread
  replaces columns); write `TrendMarkerStripTests` and the gap render check of `ChartGapRenderTests.cs:57`
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task (790 of 790)

### Task 8: Trend chart view model and readout
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Trend/TrendChartViewModel.cs`, `TrendReadout.cs`,
  `TrendPresets.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Chart/ChartPalette.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Trend/TrendChartViewModelTests.cs`,
  `TrendChartViewModelRenderTests.cs`, `TrendReadoutTests.cs`

- [x] write `TrendChartViewModel` per Solution Overview: plot, envelope, strip, moment line, `DateTimeAutomatic` on the
  existing bottom axis, the navigation model, `Refresh`, `ZoomAt`, `PanBy`, `SetWidth`, `FollowLive`, `IsFollowing`,
  `HasPoints`, `RedrawRequested`, `ApplyPalette`, the last fold for the readout
- [x] write `TrendPresets` (1 min, 10 min, 1 h, 6 h, 24 h; labels from the two format keys) and `TrendReadout.For(column,
  key)`, written as `For(series, index, key)`: an empty column between drawn ones reads the gap reason of the column
  that ends the gap, or the column the line comes from, with the failures of the whole empty run
- [x] add `ChartColors` members for the envelope, the moment line and the strip to `ChartPalette.Colors`
- [x] add the resx keys of Technical Details in both sets
- [x] write `TrendChartViewModelTests`, `TrendChartViewModelRenderTests` and `TrendReadoutTests` of Acceptance Evidence
  item 1
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task (826 of 826)

### Task 9: Trend feed and moment reads
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Trend/TrendFeed.cs`, `TrendErrors.cs`, `TrendViewModel.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Messages/ErrorMessageMapper.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Trend/TrendFeedTests.cs`, `TrendViewModelTests.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Messages/ErrorMessageMapperTests.cs`

- [x] write `TrendFeed` with `Start`, `StopAsync`, `Dispose`, the re-read pipeline and the moment pipeline; its readers
  are two delegates, `Func<ushort, byte, DateTimeOffset, DateTimeOffset, CancellationToken, Task<Result<IntensityChunk>>>`
  and `Func<DateTimeOffset, CancellationToken, Task<Result<StoredSpectrum?>>>`, bound to `SpectrumHistory` in the
  container, so tests pass gated fakes without an interface
- [x] at each trigger, take the `TimeRange(From, To)` snapshot on the UI scheduler and pass only that value to the
  read on the thread pool; a no-read request maps to `Observable.Empty` (cut with the return path in the post-slice
  check)
- [x] write `TrendViewModel`: owns `TrendChartViewModel` and the overview view model, the presets and the selected
  preset, `FollowLiveCommand` (`canExecute` = not following), `ShowMoment`, which hands the moment to the feed's
  moment pipeline. The overview view model joins in Task 11, where it is written; `TrendViewModel` takes the chart
  and the feed from the container, so Task 12 registers `TrendChartViewModel` as a singleton too, and `TrendFeed`
  takes a second scheduler for the throttle, passed in a factory lambda like `SerialLink`'s
- [x] add `TrendHistoryError` and `TrendMomentError` (Warning) to the mapper
- [x] write `TrendFeedTests`, `TrendViewModelTests` (a click inside the recent spectra needs no store read; a second
  click cancels the first; none found changes nothing; a failure reports once) and the mapper rows
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task (844 of 844)

### Task 10: Trend chart view
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Trend/TrendChartView.axaml`, `TrendChartView.axaml.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Trend/TrendChartViewTests.cs`, `TrendChartViewTestBase.cs`

- [x] load the `dev:desktop-ui` skill before writing the AXAML
- [x] lay out the view as `SpectrumChartView.axaml:14-30` plus SemiPlot's overlay canvas
  (`SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml:82-96`): the cached `PlotLayer`, the empty-state line, the hover
  line and the readout border, `IsHitTestVisible="False"`
- [x] copy the frame-paced redraw and theme repaint from `SpectrumChartView.axaml.cs`; disable ScottPlot's input
  processor and wheel handling; copy the wheel, press, move, release (tunnel), capture-lost and exit handlers of
  `TrendChartView.axaml.cs:422-472`, `:597-723`, `:757` without markers, delta mode and pen picks; a release inside
  4 px calls `TrendViewModel.ShowMoment`. The view binds to `TrendViewModel` (the plot is `Chart.Plot`); the hover
  time and the drag stay in the code-behind as overlay state, and the wheel routing is inlined (no pen or axis arms)
- [x] place the overlay with a copy of `ChartCursorOverlay.Project`; hide it while dragging and on exit; written as
  a private method of the view, then moved to `TrendCursorOverlay` (`ShowAt`, `Hide`) in review phase 2
- [x] write `TrendChartViewTests` of Acceptance Evidence item 1 with headless mouse events
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task (856 of 856)

### Task 11: Overview band
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Trend/OverviewViewModel.cs`, `OverviewView.axaml`,
  `OverviewView.axaml.cs`, `OverviewBand.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Trend/OverviewViewModelTests.cs`, `OverviewViewTests.cs`

- [x] copy `MinimapView.axaml` (`:16-39`) without the marker ticks, `MinimapBand` (`:19`, `:39-57`) and the pointer
  handlers of `MinimapView.axaml.cs:185-226`. The view is the 36 px strip only; the 1 px top border belongs to the
  `OverviewPane` row of Task 13. The hover line stays in the code-behind (no hover label row)
- [x] write `OverviewViewModel`: band from `cache.Fold` at 250 columns over the cache extent, the window fractions from
  the trend's navigation, `NavigateToFraction` (stops following), the refold cadence and the refold after a landing.
  `IntensityCache.HistoryLanded` signals a landed chunk; a key change refolds too. `TrendChartViewModel.CentreOn` moves
  the window and stops following. `OverviewGeometry.Connected` applies the trend's gap rule to the band, so sparse
  points over a short extent draw one band instead of isolated 2 px pieces. `TrendViewModel` takes the overview
- [x] write the tests of Acceptance Evidence item 1
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task (873 of 873)

### Task 12: Composition root and lifecycle
**Files:**
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/UiServiceCollectionExtensions.cs`, `App.axaml.cs`,
  `MainWindow/MainWindowViewModel.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Di/CompositionRootTests.cs`,
  `UI/MainWindow/MainWindowViewModelTests.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Di/AppLifecycleTrendTests.cs`

- [x] register `SpectrumHistory` over `StoreFile.Path`, `IntensityCache`, `TrendFeed` and `TrendViewModel`, all
  singletons
- [x] ➕ register `TrendChartViewModel` and `OverviewViewModel` as singletons too: `TrendViewModel(chart, overview,
  feed)` takes both from the container
- [x] start the feed between the store and the distributor (`App.axaml.cs:74-80`); stop it after the distributor and
  before the store in `StopLinkDistributorThenStoreAsync` (`:146-164`), each stop in a `finally` of the one before; add
  it to `ConfiguredServices`
- [x] add `TrendViewModel Trend` to `MainWindowViewModel`
- [x] write the Di tests of Acceptance Evidence item 5
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task (879 of 879)

### Task 13: Main window layout and the side pane sections
**Files:**
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/MainWindow/MainWindow.axaml`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Spectrum/SpectrumPanel.axaml`, `SpectrumChartView.axaml`
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Trend/TrendPanel.axaml`, `TrendPanel.axaml.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/MainWindow/MainWindowTrendLayoutTests.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/MainWindow/MainWindowViewTests.cs`,
  `MainWindowLayoutTestBase.cs`, `MainWindowSignalPaneTests.cs`

- [x] load the `dev:desktop-ui` skill before changing the AXAML
- [x] `ContentArea` (`MainWindow.axaml:34-55`) gets the rows and panes of Solution Overview; the side pane spans the
  rows and wraps a `StackPanel` of `SpectrumPanel` and `TrendPanel` in a vertical `ScrollViewer`
- [x] add the "Back to live" row to `SpectrumPanel.axaml` after `:127`; write `TrendPanel` on the same form grid (label
  column, 12 px gap, field column, 8 px rows, header 16 above and 8 below)
- [x] add the historical line to `SpectrumChartView.axaml` beside the empty-state line, bound to
  `HistoricalText`; `HistoricalText` moved to `SpectrumChartViewModel` (the view's DataContext), written by
  `SpectrumViewModel` through `BindTo` from `ShownMoment`
- [x] write `MainWindowTrendLayoutTests`; update the view and signal pane tests for the new rows
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task (886 of 886)

### Task 14: Desktop UI pre-handover checklist
**Files:**
- Modify (only to fix a miss): `MainWindow.axaml`, `SpectrumPanel.axaml`, `TrendPanel.axaml`, `TrendChartView.axaml`,
  `OverviewView.axaml`, the resx pair
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/MainWindow/MainWindowTextFitTests.cs`,
  `MainWindowTrendLayoutTests.cs`

- [x] every gap and inset reads off the spacing table, measured on the arranged cells (pane padding 16, label to field
  12, rows 8, the Trend header 16 above and 8 below); the `ui-theme.md:90` grep prints nothing; record the numbers
  (measured: the trend form starts at 1 + 16, right inset 16, label to field 12, rows 8, the header 16 above and 8
  below; the grep prints nothing)
- [x] every field width reads off the width table or carries its reason: the preset box 120 (short code); the buttons
  at their measured width; record the en and ru widths of "Back to live", "Follow live", "Time window" and the widest
  preset label (en: 102.3, 96.6, 89.3 and "10 min" 46 px in the box's 76 px slot; ru: 167.9, 169.9, 102.2 and "10 мин"
  49 px in 76 px)
- [x] measure through `RenderedText` in English and Russian the new labels, the buttons, the Y label at `delta` 255 and
  wavelength 2461 ("Интенсивность на 2461 нм ±255 нм"), the historical line and the widest readout line; recompute
  `PaneWidth` = 1 + 16 + label + 12 + field + 16 + 24, rounded up to 8; it stays 392 unless the Russian field column
  passes 205.5 px, and `MinimumWidth` stays 1256 while `PaneWidth` + 400 is under 1256; record the result (ru: labels
  117.5, field column 198.9, pane needs 385.4, rounded 392; en needs 300.6, rounded 304; `PaneWidth` 392 and
  `MinimumWidth` 1256 unchanged. The one-line Y label took 256.4 px (ru) and 212 px (en) against 154.8 px of room at
  the trend's floor, so `TrendAxisIntensityFormat` breaks after "Intensity": 133.4 px (ru), 137.6 px (en). Historical
  line 133.3 (ru) and 162.3 (en) in an 864 px chart; widest readout box 243.9 (ru) and 238.1 (en), under half of 864)
- [x] at the 1256 px minimum the trend keeps at least four labelled time ticks over a 10 min window (`RenderInMemory`
  of the trend plot at the chart width the window gives, 1256 - 392 = 864 px; and at a 400 px floor); record the count
  (864 px: 10 ticks, one a minute; 400 px: 2 ticks, five minutes apart, recorded only: the window never gives the
  charts less than 864 px)
- [x] at 2560 px only the charts and the overview grow; the side pane keeps its width and left-aligned content
- [x] every label and button fits on one line in English and Russian; the readout box sizes to its content
- [x] every state keeps the window size and the pane bounds: no data, live, following and not, historical, a failed
  re-read, the message panel shown (the pane scrolls); the `ui-theme.md:103` grep prints nothing; record the measured
  pane height and the scroll extent at 1280 x 800 with the message panel shown (one entry, 124 px: side pane 562 px,
  scroll extent 530 px in a 530 px viewport; the grep prints nothing)
- [x] measure the menu, connection row and status bar heights and both chart rows, their data areas and the overview at
  1280 x 800, 1366 x 768 (client 1366 x 689) and 1280 x 1024, with and without the message panel; replace the
  ASSUMPTION heights in Solution Overview with the measured table; adjust `MinHeight` so each data area keeps at least
  200 px at 768, and record whether the status bar stays inside the window at 768 with the message panel shown
  (⚠️ 200 px does not fit 768, see Solution Overview; `MinHeight` 192 and 208 keep 120 px, the window's `MinHeight` is
  688, the message list caps at 64 px; at 768 with the message panel the rows need 686.6 of 689 px and the status bar
  stays inside the window)
- [x] ➕ the hover readout flips to the left of the line when it would run past the plot's right edge, so it never
  covers the side pane (`TrendChartView.axaml.cs`; `TrendChartViewTests.AtTheRightEdgeTheReadoutFlipsLeftOfTheLineAndStaysInsideTheView`)
- ⚠️ owner decision, not done here: the trend's time ticks repeat the date on every tick ("04.11.2026" over
  "20:48:00"); the date once (or only at a day change) would return about 16 px of height to the trend and give the
  400 px floor more than two ticks
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task (904 of 904)

### Task 15: Verify acceptance criteria
- [x] run every command of Acceptance Evidence and record each result with its count (per Area, full suite against the
  baseline, none skipped; Release build 0 warnings; format and terse exit 0; old solution 13 of 13; both greps print
  nothing; the four benchmark rows against their targets). Measured 2026-11-04: Area=Trend 213, Store 121, Spectrum 123,
  MainWindow 95, Di 31, Resources 9, all passed, none skipped; full suite 907 of 907 against the 635 baseline; Release
  build 0 warnings; `dotnet format --verify-no-changes` and `dotnet terse SpectrumVisualizer` exit 0; the
  `ui-theme.md:90` and `:103` greps print nothing; `dotnet build SpectrumVisualizer.sln` succeeds with 15 warnings that
  predate the slice (no diff under `SpectrumVisualizer.Uart`, `SpectrumVisualizer.Tests` or the `.sln` since
  `9070dfb`), `dotnet test SpectrumVisualizer.Tests` 13 of 13. Benchmark: a short run (`--rows 20000 --churn-rounds 2`)
  prints the four trend rows (churned warm medians: fold 2.78 h 0.2 ms, fold 10 min 0.1 ms, visible landing 3.6 ms,
  whole plan 74.7 ms) and deletes its files; the 24 h numbers of Task 4 stand, all four targets met. At `ea86598`,
  after the review phases (2026-11-04): Area=Trend 227, Store 121, Spectrum 123, MainWindow 96, Di 30, Resources 9;
  full suite 921 of 921; old solution 13 of 13; the 24 h re-run is in Acceptance Evidence item 10
- [x] verify every Overview item maps to a named passing test: intensity at the key
  `ASpectrumAddsOnePointAtTheFrameMean`, `TheFoldEqualsTheStoreSeriesOverTheSameFrames`; dark frames at zero
  `ADarkSpectrumAddsAPointAtZero`; envelope `AColumnWithValuesBecomesAnEnvelopeColumnAtItsCentre`, `EnvelopePathTests`;
  gaps: loss `ASeqStepOfThreeBreaksWithTwoLost`, silence `AGapOverTenSecondsWithoutAnyFrameBreaksAndIgnoresTheSeqStep`,
  uncovered `AnUncoveredEmptyColumnBecomesAGap`, break in a value column `ABreakInAColumnWithValuesPutsAGapBeforeIt`;
  failure strip `TheMeasuringDevicesFailedBitMarksAFailure` (renamed in the post-slice check),
  `AFailureColumnDrawsTheStrip`; live follow
  `ANewFrameMovesTheWindowWhileFollowing`, `FollowLiveReturnsToTheNewestFrame`; zoom
  `AWheelStepZoomsAroundTheCursorTime`; pan `ADragPastFourPixelsPansAndStopsFollowing`; presets
  `ThePresetSetsTheWidthAndKeepsFollowing`, `ThePresetsAreFiveWidthsWithTenMinutesSelected`; overview
  `TheBandSpansTheCacheExtent`, `APressNavigatesTheTrendToThatTimeAndStopsFollowing`; readout `TrendReadoutTests`,
  `TheHoverOverlayFollowsThePointerAndHidesOnExit`; re-read `AKeyChangeReadsTheVisibleRangeFirst`,
  `ASecondKeyChangeCancelsTheReadInFlight`; click and Back to live `AClickWithinFourPixelsShowsTheMoment`,
  `AClickBeforeTheRecentSpectraShowsTheStoredSpectrum`, `BackToLiveIsEnabledOnlyWhileHistorical`,
  `ReturnToLiveShowsTheLatestLiveSpectrum`; chart rules `AHistoricalSpectrumDoesNotWriteLastNonDark`,
  `AHistoricalSpectrumOfTheOtherDeviceReturnsToFollowing`
- [x] verify edge cases: a key change during a re-read cancels it; a click during a re-read; a re-read before the store
  file exists reports once; a day-old point leaves the cache; both devices alternating at a covered wavelength draw one
  line; a heartbeat-only stretch adds no point and, past 10 s since the last spectrum, breaks the line at the next
  spectrum with "No spectra" (`HeartbeatsOnlyForOverTenSecondsBreakAtTheNextSpectrum`); a return to the previous key
  inside the throttle starts no read (the post-slice check cut this rule and its test). Tests: `ASecondKeyChangeCancelsTheReadInFlight`;
  ➕ `AClickDuringARereadShowsTheMomentAndLeavesTheRereadRunning`; ➕
  `ARereadBeforeTheStoreFileExistsReportsOnceAndCreatesNoFile` (the real `SpectrumHistory` over a missing path);
  `PointsBreaksAndMarksOlderThanADayBeforeNowAreDropped`; ➕ `BothDevicesAlternatingAtAWavelengthBothCoverDrawOneLine`;
  `AHeartbeatAddsNoPoint`, `HeartbeatsOnlyForOverTenSecondsBreakAtTheNextSpectrum`,
  `ANoSpectraGapReadsNoSpectraAndTheFailureAHeartbeatInItCarried`; `AReturnToThePreviousKeyCancelsThePendingReadAndStartsNone`.
  The `ObjectDisposedException` of `TrendChartView.ReportDataAreaWidthTo` after dispose (Task 14) was not hit
- [x] run the full suite: `dotnet test SpectrumVisualizer.slnx`; confirm the old solution builds and passes 13 of 13
  (907 of 907; 13 of 13). A test run leaves no file in `%LOCALAPPDATA%\SpectrumVisualizer\Spectra` and no
  `spectrum-visualizer-*` entry in temp
- [x] start the app with `dotnet run --project SpectrumVisualizer/SpectrumVisualizer.UI` and confirm the window shows
  the trend's empty state and the log gains its start line; leave the port-pair checklist to the owner (the built exe
  ran under `timeout 25`: the log gained "SpectrumVisualizer starting" and the store file line; the empty state was not
  seen on screen, a screenshot was not permitted, `TheEmptyStateShowsUntilTheFirstPoint` covers it headless; the hard
  kill left that run's `spectra-47652.db` with its WAL and SHM, removed by hand; the port-pair checklist needs com0com
  and stays with the owner, skipped here)

### Task 16: [Final] Update documentation
- [x] `docs/architecture/overview.md`: the purpose paragraph (`:3-15`); the components table (`:34-92`) gains
  `FrameContinuity`, `IntensityCache`, `TrendHistoryPlan`, `TrendNavigationModel`, `TrendColumns`, `OverviewGeometry`,
  `IntensityChunk`, `SwitchingSpectrumSource`, `EnvelopeLine`, `TrendMarkerStrip`, `TrendChartViewModel`, `TrendFeed`,
  `TrendViewModel`, `OverviewViewModel` and the two trend errors; `AddUi` and `App` rows; startup and close order
  (`:124-137`); the main window rows and the content row (`:156-206`); the queries section (`:501-525`) gains the
  points read and the shared row loop; the schema section (`:396-425`) states that the heartbeat table has no reader
  in the app and stays written for diagnostics; a new "Trend" section: the cache and its recent spectra, the continuity rule,
  the re-read and its cancellation, the column mapping, navigation, readout, overview, click and the chart rules
- [x] `docs/architecture/README.md`: a Trend row (cache fed by the distributor, store re-read only on a key change,
  session rule); the Frame distribution row names the trend feed as a subscriber
- [x] `docs/architecture/board-protocol.md`: "Sequence counter" (`:123-146`) notes the trend's 10 s session rule
- [x] `docs/architecture/ui-theme.md`: the consumers of the keys in the table (`:44-59`); "The plot" section names the
  trend plot
- [x] `docs/architecture/ui-text.md`: owners Trend pane, Trend chart, Trend readout, Spectrum chart (historical line),
  Chart controls (`ChartBackToLive`), and the trend errors in the `ErrorMessageMapper` row
- [x] `docs/architecture/testing-strategy.md`: the Area list (`:23-25`) gains `Trend`; a Trend row in the table
  (`:46-62`); the benchmark row names the trend timings
- [x] `CLAUDE.md`: add `Trend` to the Area list (`:41-43`); the Layout line for `SpectrumVisualizer.UI/` names the
  trend; `SpectrumVisualizer.Core/` names the trend rules
- [x] `readme.md`: the paragraph "The new app shows the live spectrum ..." (`readme.md:18`) names the trend, the
  overview and the click to a past spectrum

## Post-Completion
**Manual verification:**
- the smoke checklist of Acceptance Evidence over a com0com pair, during the owner's final assembly check.
- the real board after the firmware-frame-sender slice: the same checklist against the board. The device-alternation
  ASSUMPTION is resolved: the firmware picks the device from the wavelength (`main.c:394-402`, `SelectedDevice = wave
  < 904 ? DEV_2000 : DEV_8000`), so both devices never alternate at one key; a PLC device select (`main.c:1351`) sets
  a new wavelength and is an ordinary key change; `main.c:1194-1195` clamps `WavePLC` to 189-2460 nm and `delta` to
  0-100.
- ask Optosky whether the idle spectrometer's measurement-failed bit should clear when the other device is selected
  (OptoskyProj#20 follow-up): the firmware sets it at `main.c:502` and clears it only on that device's own successful
  spectrum at `main.c:1064`. The trend shows only the measuring device's bit; no firmware change in this slice.
- the operator PC's display scaling: at 125 % on 1366 x 768 the window's 688 DIP `MinHeight` and 1256 DIP
  `MinimumWidth` exceed the screen; the owner decides the floors once the scaling is known.
- the trend collapses to the last 2 min for about 0.5 s on every SCADA key change, until the visible chunk lands (the
  500 ms throttle plus the first read); the owner judges whether that is acceptable.
- a PLC-triggered dark frame pins the Y axis to 0 while it is in the window, because dark points draw at zero; the
  owner accepted dark points on the trend.
- the store benchmark on the operator PC (cold disk): the four trend rows against their targets.
- the owner judges the 3 : 2 split of the spectrum and trend rows and the 120 px data floor of each chart on the
  site's monitors (smoke step 15).
- the owner judges the date every time tick repeats (at 10 min each label reads date over time, for example
  "04.11.2026" over "22:10") and decides whether the trend should show it once.
- correct the dark-spectrum sentence of Semiteq/SpectrumVisualizer#3 before the PR closes it: the issue still says dark
  spectra are not drawn, but dark measurements show on the trend as a point at zero, as the roadmap resolved.

**PR body note:**
- Closes Semiteq/SpectrumVisualizer#3

**Remaining slices:**
- firmware-frame-sender: the OptoskyProj#20 frame sender in the board firmware, verified on the board against the app.
- release-3-0: the 3.0.0 installer over the self-contained publish folder, the release workflow, and removal of the old
  app.

**Executed by exec:**
- branch: intensity-trend

Run note: 30 commits on `intensity-trend` (`9cedb6c`..`ea86598`) before this record, stacked on `spectrum-store` at
`9070dfb`; 21 of them add or revise this plan. Review phase 1 (5 agents) found 0 critical, 4 major and about 49 minor
findings, all fixed in fix iteration 1 (`f521a51`, `a13cb54`, `25f6c32`, `450c88f`, `7fcabee`); a second-agent fact
check of the docs then corrected 7 plan mismatches in `d69cccb`. Its critical re-check found 1 critical and 1 major
finding: a moment read could still land after Back to live, and a zoom or pan while following detached on the window
drawn up to a second ago. Both were fixed in `8568185`. Phase 2 (smells) fixed 18 minor findings in `556a4bf`,
`1c694fa` and `5e57846`. Phase 3 (codex) was skipped because codex is not installed. Phase 3b (comments) fixed 3 major
and 18 minor comment findings in `ea86598`. Phase 4 (critical) found nothing at `ea86598`. Final state at `ea86598`:
921/921 tests in `SpectrumVisualizer.slnx`, 13/13 in `SpectrumVisualizer.sln`, Release build 0 warnings, format and
terse clean, the 24 h benchmark re-run meets all four trend targets. The com0com smoke checklist was not run: this
machine has no com0com pair. The app start was checked through its log only, because a screenshot was not permitted.

**Decisions made autonomously:**
- `FrameContinuity` returns a `Continuity` record struct with separate Loss, NoSpectra and Failure slots, so one
  spectrum after a heartbeat-only stretch carries both breaks. A break with 0 lost is a NoSpectra break; there is no
  `BreakKind`. The 10 s limit is `LinkTiming.SilenceLimit` (`Core/Protocol`), shared with `LinkHealth`.
- `IntensityColumn` gains `Last`, `BreakCount`, `LostCount`, `NoSpectraCount` and `Failed`, appended without defaults.
  `Failed` is the state masked to the two measurement-failed bits. `Last` is the value with the latest receive time.
- The store's shared row loop is a lazy iterator of `TrendPoint`; `ReadSeries` folds it and maps a NaN point to
  `AddUncovered`. `TrendPoint` and `IntensityChunk` live in `Core/Store/IntensityChunk.cs`.
- Core references System.Reactive and no logging package; `IntensityCache(TimeProvider)` exposes `Changed`,
  `HistoryLanded` and `ReReadRequested`, and `TrendFeed` logs each key change at Debug. All three emit through one
  `_emitGate`, which `Dispose` also takes, so no signal leaves a disposed cache.
- `TrendTimeline` holds the points, breaks and marks and the fold; `IntensityCache` keeps the key and the recent
  spectra. `Extent` and `FoldCurrent(queryFor)` replace separate first and newest reads, so the
  chart builds its query under the cache lock.
- The post-slice check cut the return-to-previous-key path (`KeyBlock`, `ReturnToPrevious`, `CompleteHistory`,
  `ReReadRequest.ReadsHistory`): every key change re-reads, and the recent spectra always hold 120 s.
- `FrameContinuity` remembers the device of the last spectrum and masks a frame's state to that device's failed bit;
  a failed heartbeat before the first spectrum marks nothing, because no device has measured.
- `EnvelopeLine` fills a 3 px square at a lone vertex (`EnvelopePath.IsLone`) in the line's colour;
  `OverviewGeometry`'s lone column is at least 3 px square, centred on its values and kept inside the band.
- The benchmark cache runs on `FixedTimeProvider` stopped at the newest row, because churned rows lie one span ahead
  of the real clock. Each re-read run changes the key once, stored + 50 nm and + 100 nm in turn.
- `TrendNavigationModel` has no `SetWindow` and no argument guards. `SetWidth` while following keeps the right edge
  with no clamp. While following, the window ends at now, refreshed by a 1 s tick; `ZoomAt` and `PanBy` first snap the
  drawn window onto now and shift the zoom anchor by the snap. A zoom or pan that leaves now outside stops following.
- `EnvelopeColumn`, `EnvelopeColumn.Map` and `VerticalRange` live in `UI/Trend/`; `TrendColumns` in Core keeps
  `ColumnWidth` and `QueryThrough`. `LocalTimeAxis` works on `DateTimeOffset`.
- `EnvelopeLine.ReplaceColumns` replaces the whole list and sets line width 1, since ScottPlot's default is 0.
  `TrendMarkerStrip` draws a span narrower than a pixel 1 px wide; failed columns under 5 s apart join into one span.
- `SwitchingSpectrumSource.ShowMoment` copies the measurement with `with { }` around the same pixel array. The
  interface event is `CurrentChanged`, so a caller on the concrete type cannot bind to `ReactiveObject.Changed` by
  mistake. The container has no `IDisplayedSpectrumSource` registration.
- The frozen scale records its device whenever `ChangeScale` ends frozen. `HistoricalText` lives on
  `SpectrumChartViewModel`, written by `SpectrumViewModel` from `ShownMoment`, because the chart view binds to the
  chart view model.
- `TrendChartViewModel` takes the cache, the switching source, the message panel, a `TimeProvider`, the UI scheduler
  and a logger. `cache.Changed.Sample(100 ms, uiScheduler)` needs no `ObserveOn`. The fold query ends at `To` + 1 ms.
  Y limits pad 5 %, take v +/- 1 when flat and start at 0..1.
- `TrendPlotLayers` owns the plot layers, `TrendCursorOverlay` the hover line and readout, `ChartFrames` the redraw
  pacing of both chart views, `DeviceLine` one spectrometer's spectrum line, `ClockText.TimeOfDay` the readout time.
- `TrendReadout.For(series, index, key)`: an empty column reads the gap reason of the column that ends the gap, or of
  the column the line comes from, with the failures of the whole empty run.
- `TrendPresets.All` builds its labels per read, so they follow the current culture. `SelectedPreset` is an OAPH
  derived from `Chart.Width`; its setter only calls `SetWidth`.
- `TrendFeed` takes a separate throttle scheduler (`DefaultScheduler.Instance` in the container). Each moment read hops
  to the UI scheduler before `Switch`, and a `_requestedMoment` guard drops a read that lands after Back to live. Back
  to live reaches the feed through `ShownMoment` turning null. `StopAsync` awaits every read still running, superseded
  ones included. The feed logs its start and stop at Debug for the lifecycle tests.
- `TrendViewModel(chart, overview, feed)` takes all three from the container; `TrendChartViewModel` and
  `OverviewViewModel` are singletons.
- The overview band applies the trend's gap rule through `OverviewGeometry.Connected`, refreshes its fractions on
  `RedrawRequested`, and refolds after a landed chunk, after a key change and on its cadence. `CentreOn` moves the
  trend window and stops following. The overview's 1 px top border belongs to `OverviewPane`.
- Chart rows `MinHeight` 192 and 208 keep a 120 px data floor; the window's `MinimumHeight` is 688; the message list
  caps at 64 px. The opening size is clamped to the screen's work area in `OnOpened`. The side pane shares one label
  column across `SpectrumPanel` and `TrendPanel` through `SharedSizeGroup`.
- `TrendAxisIntensityFormat` breaks after "Intensity". The hover readout flips left of the line at the plot's right
  edge.
- Russian texts: `TrendReadoutUncoveredFormat` is "{0} нм вне диапазона спектрометра"; `SpectrumHistoricalFormat` is
  "Спектр от {0}". `TrendHistoryError` has no remedy key, because `AppResourcesTests` rejects an empty value; the
  mapper passes an empty remedy.
- ScottPlot.Avalonia 5.1.59 works in DIPs on an Avalonia-scaled canvas (decompiled), so hover, zoom anchor and click
  hold at 125 % with no fix.
- Large test classes split over abstract bases: `IntensityCache{Point,KeyChange,History,Dispose}Tests`,
  `TrendFeed{Reread,Moment,Failure}Tests`, `TrendChartViewModel{Navigation,Display}Tests`,
  `TrendNavigationModel{Follow,Width,Zoom}Tests`, `OverviewGeometry{Band,Window}Tests`,
  `SpectrumViewModel{Command,Signal}Tests`. Feed tests run their hops on the headless dispatcher;
  `TrendStand` gives the main window tests an idle trend graph.

## Verify it yourself

Automated, from the repo root on branch `intensity-trend`:

1. `dotnet build SpectrumVisualizer.slnx -c Release` gives 0 warnings, 0 errors.
2. `dotnet test SpectrumVisualizer.slnx` passes 923 of 923. Per area with `--filter "Area=X"`: Trend 229,
   Spectrum 123, Store 121, MainWindow 96, Link 80, Protocol 56, Emulator 56, Chart 52, Messages 41, Di 30,
   Calibration 15, Resources 9, Paths 8, Logging 7; the areas sum to 923.
3. `dotnet format SpectrumVisualizer.slnx --verify-no-changes`, then `dotnet tool restore` and
   `dotnet terse SpectrumVisualizer`; both gates exit 0.
4. `dotnet build SpectrumVisualizer.sln` gives 0 errors, and `dotnet test SpectrumVisualizer.Tests` passes 13 of 13.
5. `dotnet run -c Release --project SpectrumVisualizer/SpectrumVisualizer.Tools.StoreBenchmark` fills 24 h of
   ATP-2000 spectra, runs the churn and prints one table; it runs for minutes. Key churned trend numbers on the dev
   laptop at `ea86598` (Acceptance Evidence item 10): cache fold 24 h 1.4 ms and 10 min 0.1 ms, re-read to the visible
   landing 4.4 ms and to the last chunk 720.4 ms. The OS cache held the file, so every number is warm. Cold-cache
   numbers on the operator PC are unmeasured: run the tool there. `--rows 2000 --churn-rounds 2` is a quick run.
6. The trend is pinned by these test classes:
   - `FrameContinuityTests`: the 10 s session reset, Loss and NoSpectra breaks, failure marks.
   - `IntensityCachePointTests`, `IntensityCacheKeyChangeTests`, `IntensityCacheHistoryTests`,
     `IntensityCacheDisposeTests`: points, the key change rule, landed chunks, the fold equal to the
     store series, retention, dispose.
   - `TrendHistoryPlanTests`, `SpectrumHistoryPointsTests`: the read order and the store's points read.
   - `TrendNavigationModelFollowTests`, `TrendNavigationModelWidthTests`, `TrendNavigationModelZoomTests`: follow,
     widths, zoom and the first-point clamp.
   - `TrendColumnsTests`, `EnvelopeColumnTests`, `EnvelopePathTests`, `EnvelopeLineTests`, `TrendMarkerStripTests`:
     column width, gaps, the envelope path and the failure strip.
   - `TrendChartViewModelNavigationTests`, `TrendChartViewModelDisplayTests`, `TrendChartViewModelRenderTests`,
     `TrendReadoutTests`: the window, the Y limits, the marker and the readout texts.
   - `TrendFeedRereadTests`, `TrendFeedMomentTests`, `TrendFeedFailureTests`, `TrendViewModelTests`: re-reads and
     their cancellation, click reads, Back to live, failure reports, the stop.
   - `TrendChartViewTests`, `OverviewViewModelTests`, `OverviewViewTests`, `OverviewGeometryBandTests`,
     `OverviewGeometryWindowTests`: input, hover, the overview band and its navigation.
   - `SwitchingSpectrumSourceTests`, `SpectrumChartViewModelHistoricalScaleTests`, `SpectrumViewModelCommandTests`:
     the historical spectrum and its scale rules.
   - `MainWindowTrendLayoutTests`, `MainWindowChartHeightTests`, `MainWindowTextFitTests`: rows, floors and text fit.
   - `AppLifecycleTrendTests`, `CompositionRootTests`: start before the distributor, stop before the store, one
     instance each.
   - `StoreBenchmarkRunTests`: the four trend rows on a small run.

By hand: run the manual smoke checklist in Acceptance Evidence over the com0com pair. The highest-value checks, with
the emulator on `COM10` and the app on `COM11`:

1. Steps 2 and 8: a point per second at the right edge, the window ending at now; a 15 s stop leaves an empty span
   that becomes a "No spectra" break.
2. Step 7: restart the emulator with `--wavelength 400 --delta 25`; within about 1 s the Y label reads 400 nm ±25 nm
   and the visible history redraws at 400 nm, older history after.
3. Step 6: click a moment; the spectrum chart shows it with "Spectrum from HH:mm:ss" ("Спектр от HH:mm:ss") and a
   marker on the trend; Back to live returns to the latest spectrum.
4. Steps 9 and 12: `--bad-crc-every 5` gives "1 spectra lost" breaks; `--device 2000 --both-connected --failed 8000`
   shows no failure strip; `--device 2000 --rate 0 --failed 2000` shows one strip and "Measurement failed: ATP-2000".
5. Steps 14 and 15: on a 1366 x 768 screen, maximised with the message panel shown, both charts keep a readable data
   area, the side pane scrolls and the status bar stays inside the window.
