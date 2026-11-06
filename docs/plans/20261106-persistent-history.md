# Persistent history

## Overview
- Keep one history file, `%LOCALAPPDATA%\SemiSpec\Spectra\spectra.db`, across restarts. The start-up wipe and the
  sweep of other instances' files go; the `spectra-<pid>.db` files of older builds are deleted at start.
- Keep the 24 h retention: rows older than 24 h are deleted at start and every minute; a file whose every row has
  expired is emptied by the fast path.
- On start, the trend loads the stored last 24 h through the existing history read, and the time the app was closed
  shows as a NoSpectra break.
- Version the schema with `PRAGMA user_version`; a file of another version is replaced by a fresh one.
- Set aside a file SQLite cannot read, at open or after a write or read reported damage, as
  `spectra.broken-<yyyyMMdd-HHmmss>.db`; start a fresh one and say so in the message panel; keep at most 2.
- Scope guard: no UI layout change, no new setting, no change to the board protocol or the link. One running copy is
  the separate plan `20261106-single-instance.md`, executed after this one merges. Loss breaks and failure marks of
  earlier runs are not rebuilt from the stored `seq` and `state` columns.

## Context (from discovery)
- Verified 2026-11-06 at `261335e` (master): `dotnet test SemiSpec.slnx` passes 944 of 944.
- Baseline at the `ui-redesign` merge `4a158fb` (run at `bb89a7f`, which adds this plan only): `dotnet test
  SemiSpec.slnx` passes 992 of 992, none skipped.
- This plan executes on master after the branch `ui-redesign` (tip `84b9e06`) merges. Expected conflict points, from
  `git diff --stat master...ui-redesign`:
  - `docs/architecture/overview.md` gains about 370 changed lines; this plan cites its sections by name only.
  - `SemiSpec.UI/Program.cs` becomes `AppLogging.Create(AppLogging.SiteLogFolder, paths.Value.FallbackLogFolder,
    AppLogging.CanWrite)`, and `AppDataPaths.LogFolder` becomes `FallbackLogFolder`; this plan does not edit
    `Program.cs` and keeps `AppDataPaths.StoreFolder`.
  - Both resx files, `UI/Messages/MessagePanelViewModel.cs`, `UI/Trend/TrendChartViewModel.cs`,
    `UI/Trend/OverviewViewModel.cs`, `Tests.Unit/UI/Di/AppLifecycleTestBase.cs` and `CompositionRootTests.cs`.
  - Code line numbers below are read at `261335e`; re-read them at the merge commit. Task 1 records the test count
    there as the baseline.
- Store files today (`SemiSpec.Core/Store/StoreFiles.cs`):
  - `InstanceFile(folder, processId)` names `spectra-<pid>.db` (`:16-19`); `Prepare` creates the folder and wipes
    the own file (`:25-40`, wipe `:31`), then `Sweep` deletes every other `spectra-<digits>.db` no live process
    holds open (`:46-92`).
  - `DatabaseOf` recognises `spectra-<digits>.db` and its `-wal`/`-shm` sidecars and returns null for any other name
    (`:97-117`); `DeleteWithSidecars` (`:120-127`); `TryDelete` treats `IOException` and
    `UnauthorizedAccessException` as "a live process holds it" (`:129-140`); `StoreSweep` (`:147`).
- `StoreRetention.Run` deletes rows older than 24 h and sweeps once an hour (`StoreRetention.cs:23-36`);
  `LogSweep` (`:38-66`); the delete keeps its own incident logging (`:68-93`).
- `StoreWriter`:
  - `OpenDatabase` runs `Prepare` then `SpectrumDatabase.Open` (`StoreWriter.cs:125-133`); a failed open reports one
    `StoreOpenError` (`:83-87`, `:135-138`); the log line "Spectrum store file {Path}" follows the open (`:90`).
  - The first retention pass runs one minute after the open, not at it (`:145`, `:154-163`).
  - `WriteBatch` counts a failed batch and reports the first failure of an incident (`:191-228`); the closing
    checkpoint logs a failure at Warning (`:268-279`).
- `SpectrumDatabase`:
  - `Open` runs the pragmas through `StoreConnection.Open`, then plain `CREATE TABLE` statements
    (`SpectrumDatabase.cs:16-36`, `:82-98`), so it fails on a file that already has the schema. A `SqliteException`
    or `IOException` becomes one `ExceptionalError` (`:93-97`); `Write` and `DeleteBefore` do the same
    (`:101-127`, `:130-147`).
  - `DeleteBefore` runs `DELETE ... WHERE received_at < $cutoff` on both tables in one `BEGIN IMMEDIATE`
    transaction (`:47-50`, `:130-147`).
  - `StoreConnection.Open` runs the writer pragmas as its first statement and disposes the connection when they throw
    (`StoreConnection.cs:14-18`, `:39-62`); `Pooling` is off (`:44`), so a closed connection releases the file.
- `SpectrumStore`: `StoreFile(string Path)` (`SpectrumStore.cs:15`); `Start` subscribes, then runs the writer task
  (`:85-102`); `StopAsync` (`:109-126`); `Dispose` (`:139-149`). `AddUi` registers the instance file and builds
  `SpectrumHistory` over the same path (`UiServiceCollectionExtensions.cs:42-44`).
- Startup: `App.Configure` resolves `MainWindowViewModel` and `TrendFeed` (`App.axaml.cs:200-209`); `Opened` starts
  the store, the trend feed, then the distributor (`:75-81`). The close chain stops link, distributor, trend feed,
  store (`:151-176`).
- Trend history path:
  - `IntensityCache` sets the first key silently and raises `ReReadRequested` only on a key change
    (`IntensityCache.cs:184-213`, first key `:191-195`). `LandHistory` drops chunks of an older generation (`:97-112`).
    `DropExpired` runs only in `Add` and `LandHistory` (`:83`, `:107`, `:215-218`), not in `FoldCurrent`
    (`:127-139`). The cache is a singleton (`UiServiceCollectionExtensions.cs:45`).
  - `TrendFeed` throttles `ReReadRequested` by 500 ms, reads the visible window at the UI dispatch and runs one
    history read per request under `Switch`, through `Observable.Create` with the disposal token
    (`TrendFeed.cs:119-138`). `ReadHistoryAsync` walks `TrendHistoryPlan.For` and lands each chunk under the
    request's generation; the first failed chunk reports one `TrendHistoryError` (`:141-168`). Every read is tracked
    for `StopAsync` (`:88-100`, `:264-281`). The feed already holds `readSpectrum`, which is
    `SpectrumHistory.SpectrumAtAsync` (`:31-32`, `UiServiceCollectionExtensions.cs:55-69`).
  - `TrendHistoryPlan.For` reads the visible window first, then hour chunks newest first, within
    `[newest - 24 h, recentStart)` (`TrendHistoryPlan.cs:12-28`).
  - `SpectrumHistory.SpectrumAtAsync` returns the latest row at or before a moment (`SpectrumHistory.cs:20-45`).
    `IntensityPointsAsync` yields one point per stored spectrum, NaN for an uncovered device or a corrupt row
    (`:78-90`, `:123-139`). Every read opens its own reader connection and turns a `SqliteException` into a failed
    result (`:167-184`); a reader opens with `Mode=ReadWrite` and fails on a missing file (`StoreConnection.cs:34-37`).
- Breaks: `FrameContinuity` raises a NoSpectra break at a spectrum more than `LinkTiming.SilenceLimit` (10 s) after
  the previous spectrum (`FrameContinuity.cs:13`, `:32-41`; `LinkTiming.cs:7`); `TrendTimeline.Observe` keeps it
  (`TrendTimeline.cs:24-41`). `_lastSpectrumAt` starts null, so the first spectrum after a restart raises none.
  `TrendTimeline.Land` inserts points only (`:48-51`). The cache adds one point per live spectrum, NaN for an
  uncovered device (`IntensityCache.cs:201`, `RecentSpectra.cs:18-29`, `IntensityOf`). The chart cuts the line
  only at a break or an uncovered column and connects across empty columns (`TrendColumns.cs:39-52`,
  `EnvelopeColumn.cs:19-37`). `overview.md`, Trend, Continuity, states that every stored frame passed through the
  cache first; that stops being true here.
- Chart at start: the window opens at now with the 10 min preset (`TrendChartViewModel.cs:58-59`,
  `TrendPresets.cs:11-20`); `HasPoints` drives the empty-state overlay (`TrendChartViewModel.cs:93-97`, `:211`,
  `TrendChartView.axaml:23`); the first fold with points jumps the window to now (`TrendChartViewModel.cs:226-238`).
- Messages: `ErrorMessageMapper.Map` switches on error types, store errors at `:51-65`; `TryReportFailure` logs on
  the calling thread and posts the panel entry to the UI scheduler (`ResultReporting.cs:62-86`). Resx parity is
  pinned by `AppResourcesTests.TheRussianSetCarriesEveryKeyTheNeutralSetCarries` and
  `BothSetsResolveEveryKeyToANonEmptyValue` (`AppResourcesTests.cs:41-62`).
- Benchmark tool: options `--rows`, `--device`, `--churn-rounds`, `--folder`, `--keep`
  (`BenchmarkCommand.cs:15-43`). Rows are 500 ms apart and end at the tool's start time (`BenchmarkCommand.cs:17`,
  `BenchmarkRun.cs:31`); the churn writes rows one span later, into the future (`BenchmarkFile.cs:78-88`);
  `BenchmarkFile.Create` deletes the file first and opens it with `SpectrumDatabase.Open` (`BenchmarkFile.cs:59-68`).
  The tool does not drop the OS file cache (`BenchmarkRun.cs:67-68`).
- Recorded warm numbers (dev laptop, OS cache holding the file): whole-day trend re-read to the last chunk 720.4 ms
  and visible 10 min to its landing 4.4 ms, churned (`20261104-intensity-trend.md`, Acceptance Evidence, the trend
  rows table); the 24 h `substr` series 647.3 ms churned against a budget of at most 10 s, a 24 h file of about
  765 MB, and one retention pass over 120 rows 0.3 ms (`20261104-spectrum-store.md`, Acceptance Evidence item 8).
  Cold numbers are unmeasured.
- Tests: `AppLifecycleTestBase` gives each test its own root folder and the production container
  (`AppLifecycleTestBase.cs:23-46`). Traits per `CLAUDE.md`, Test, and `testing-strategy.md`.

## Development Approach
- **testing approach**: Regular (code first, then tests in the same task).
- complete each task fully before moving to the next; small, focused changes.
- every task includes new or updated tests for its code; all tests pass before the next task.
- update this plan when scope changes during implementation.

## Testing Strategy
- **unit tests** in `SemiSpec.Tests.Unit`. Store tests use `TempFolder` (`Tests.Unit/TempFolder.cs`) with real SQLite
  files. Damaged files are made on disk: random bytes for "not a database", and a valid file whose page 1 after
  its 100-byte header (the schema table) is overwritten with random bytes for "corrupt". ASSUMPTION: SQLite reports
  the first as `SQLITE_NOTADB` (26) and the second as `SQLITE_CORRUPT` (11), both at the first pragma, which loads
  the schema; the two `SpectrumDatabaseTests` below pin both codes.
- Damage after the open is made with a valid file whose table pages (past page 1) are overwritten while the schema
  page stays intact. ASSUMPTION: the open succeeds and the first read or write of those pages fails with code 11;
  `ADamagedTablePageFailsAReadWithCorrupt` pins it.
- Time: `RetentionClock` (`FakeTimeProvider`) drives retention and the set-aside stamp; the cache's run start reads
  the same provider.
- Traits: `Component=Core, Area=Store` and `Area=Trend` for Core; `Component=UI` with `Area=Store`, `Trend`,
  `Messages`, `Resources`, `Di`.
- The 24 h benchmark, the retention timing and the cold read run outside the unit suite.
- No e2e framework exists; headless tests take that role. The restart test uses the production container twice.

## Acceptance Evidence
Automatable (baseline: the count at the `ui-redesign` merge commit, recorded in Task 1):
1. `dotnet test SemiSpec.slnx --filter "Area=Store"` passes, including:
   - `StoreFilesTests`: `TheHistoryFileIsSpectraDbInTheStoreFolder`; `PrepareCreatesAMissingFolder`;
     `PrepareKeepsTheHistoryFileAndItsRows`; `PrepareDeletesLegacyInstanceFilesAndTheirSidecars` (`spectra-1234.db`,
     `-wal`, `-shm` gone; the result counts 1); `PrepareKeepsALegacyFileAnotherProcessHoldsOpen` (a second
     `SqliteConnection` holds it); `PrepareKeepsNamesOutsideTheLegacyPattern` (`spectra-1234 - Copy.db`,
     `spectra.broken-20261106-101500.db`, `spectra.db.damaged` stay); `PrepareFailsWhenTheFolderPathIsAFile`;
     `SetAsideRenamesTheFileWithTheLocalStampAndMovesItsWal` (bytes unchanged, `-wal` moved beside it);
     `SetAsideDeletesTheSharedMemorySidecarAndTheMarker`; `SetAsideKeepsTheTwoNewestSetAsideFiles` (three older
     stamps exist; afterwards the new file and the newest older one remain, with their `-wal`);
     `MarkDamagedCreatesTheMarkerOnceAndNeverThrows` (a second call and a call on a missing folder change nothing).
   - `SpectrumDatabaseTests`: `OpenOfANewFileCreatesTheSchemaAndSetsVersionOne` (`PRAGMA user_version` reads 1);
     `ReopenKeepsTheRows`; `OpenOfAnotherVersionFailsWithASchemaMismatch` (`user_version` 2: a
     `StoreSchemaMismatchError` naming 2); `OpenOfAVersionZeroFileWithTablesFailsWithASchemaMismatch`;
     `OpenOfRandomBytesFailsAsUnreadable` (a `StoreFileUnreadableError` whose `SqliteErrorCode` is 26);
     `OpenOfADamagedSchemaPageFailsAsUnreadable` (code 11); `OpenOfADirectoryIsNotUnreadable` (a plain failure,
     no `StoreFileUnreadableError`); `DeleteBeforeACutoffPastEveryRowEmptiesBothTables` (fast path);
     `DeleteBeforeACutoffInsideTheRowsKeepsTheNewerRows` (row-wise path); `DeleteBeforeOnAnEmptyFileSucceeds`;
     `AWriteToADamagedTablePageFailsWithTheDamageCode` (the failed `Result` carries code 11);
     the existing pragma, round-trip, retention and checkpoint tests unchanged.
   - `SpectrumHistory*Tests`: `ADamagedTablePageFailsAReadWithCorrupt` and `ADamagedReadMarksTheFile`
     (`spectra.db.damaged` exists after the failed read); `AReadOfAMissingFileDoesNotMarkIt`.
   - `SpectrumStore*Tests`: `TheHistorySurvivesAStoreRestart` (store A writes 10 frames and stops; store B over the
     same file starts, its `Opened` completes, it writes 5 more; the file holds 15 rows in order);
     `RetentionAtOpenDeletesRowsOlderThanADay` (rows at now - 25 h and now - 23 h written before the start; once
     `Opened` completes, the 25 h row is gone and the 23 h row stays, the clock not advanced);
     `RetentionAtOpenEmptiesAFullyExpiredFile`; `RetentionRunsOnceAMinuteAlsoWithoutFrames` (adjusted: the pass at
     the open, then one per minute); `ASchemaMismatchStartsAFreshFileAndLogsOneWarning` (no panel entry; the new
     file has version 1); `AnUnreadableFileIsSetAsideAndReportedOnce` (random bytes: the set-aside file holds the
     same bytes, the fresh file stores frames, one Warning panel entry titled `StoreFileReplacedTitle`, one Warning
     log line naming both paths); `ADamagedBatchMarksTheFile` (a batch failing with code 11 creates the marker;
     a batch failing on a held lock does not); `AMarkedFileIsSetAsideAtTheNextOpen` (a valid file with rows and
     the marker: the next store sets it aside, removes the marker, reports once, starts empty);
     `LegacyFilesAreDeletedAtOpen`; `OpenedCompletesTrueAfterTheRetentionAtOpen`;
     `OpenedCompletesFalseWhenTheOpenFails` (the folder path is a file); `OpenedCompletesFalseOnDisposeBeforeTheOpen`.
2. `dotnet test SemiSpec.slnx --filter "Area=Trend"` passes, including:
   - `IntensityCacheStoredDayTests`: `TheRunStartIsTheClockAtConstruction`;
     `AStoredDayRequestSeedsTheKeyAndAsksForEverythingBeforeTheRunStart` (`ReReadRequest(1, stored key, run start)`);
     `AStoredDayRequestAfterALiveSpectrumAsksAtTheLiveKey`; `AStoredDayRequestAfterAKeyChangeAsksNothing`;
     `FoldCurrentDropsPointsOlderThanADayWithoutNewFrames` (a landed chunk, the clock advanced 25 h, no `Add`).
   - `IntensityCacheGapTests`: `AGapOfMoreThanTenSecondsBetweenPointsFoldsANoSpectraBreak` (in a landed chunk);
     `TheGapBetweenTheStoredDayAndTheFirstLiveSpectrumFoldsANoSpectraBreak`; `AGapOfExactlyTenSecondsIsNoBreak`;
     `AGapAcrossTheFoldStartBreaksAtTheFirstPointInRange`; `AHeartbeatOnlyStretchStillBreaksAtTheNextSpectrum`;
     `UncoveredPointsCountAsSpectraForTheGapRule` (NaN points of an uncovered device every 5 s over 30 s: no
     NoSpectra break); `ASpectrumAfterAHeartbeatStretchWithASeqGapCarriesBothBreaks` (Loss from `FrameContinuity`,
     NoSpectra from the fold). `FrameContinuityTests` keeps the Loss, reset and failure cases; its NoSpectra
     assertions move here.
   - `IntensityCacheHistoryTests`: the fold-equals-store-series test runs on rows without a gap over 10 s, since the
     store's series adds no breaks.
   - `TrendFeedStartReadTests`: `TheStartReadWaitsForTheStoreToOpen` (no reader call before `Opened` completes);
     `TheStartReadLandsTheStoredDayAtTheLastStoredKey` (`readSpectrum` at run start - 1 ms, then the plan's chunks
     at that key); `AnEmptyStoreAsksNoHistoryRead`; `AFailedStoreOpenAsksNoHistoryRead`;
     `StopDuringTheStartReadCancelsItAndAwaitsIt`, as a theory over two phases: `Opened` never completes (the stop
     ends within 1 s and no reader is called), and a key read held at a gate (the stop cancels it and awaits it);
     `AHistoryReadLogsItsLandingTimes` (one Information line with the points, the visible landing and the last
     chunk in ms).
3. `dotnet test SemiSpec.slnx --filter "Area=Di"` passes, including
   `AppLifecycleTrendTests.TheTrendShowsTheStoredPointsAfterARestart` (container A on a root: connect through
   `FakePortStream`, feed spectra, close the window, dispose; container B on the same root: open the window; within
   5 s the cache's fold holds every spectrum A fed, at A's receive times) and
   `CompositionRootTests.TheStoreIsOneInstanceOverTheHistoryFileUnderTheStoreFolder`.
4. `dotnet test SemiSpec.slnx --filter "Area=Messages|Area=Resources"` passes, including
   `ErrorMessageMapperTests.AStoreFileReplacedErrorMapsToAWarning` and the resx parity tests over the new keys.
5. `dotnet test SemiSpec.slnx` passes every test, none skipped; Task 10 records the count. The count is not the
   baseline plus the added tests, since Tasks 1 and 7 remove and rewrite tests.
6. `dotnet build SemiSpec.slnx -c Release` gives 0 warnings; `dotnet format SemiSpec.slnx --verify-no-changes` and
   `dotnet terse` over every touched `.cs` file exit 0.
7. Warm benchmark, regression only: `dotnet run -c Release --project SemiSpec/SemiSpec.Tools.StoreBenchmark`
   prints the trend rows; the churned warm medians stay inside the intensity-trend targets: cache fold 24 h at most
   20 ms (the fold now scans point gaps), whole-day re-read at most 2 000 ms (`20261104-intensity-trend.md`,
   Acceptance Evidence, the trend rows targets). Record the numbers with the date and commit.
8. Retention timing on a full day (`sqlite3` from `winget install SQLite.SQLite`):
   1. `dotnet run -c Release --project SemiSpec/SemiSpec.Tools.StoreBenchmark -- --rows 172800 --device 2000
      --churn-rounds 0 --folder C:\Temp\semispec-retention --keep`.
   2. Copy `spectra.db` to `rowwise.db` and `fast.db` in that folder.
   3. `sqlite3 rowwise.db ".timer on" "BEGIN IMMEDIATE;" "DELETE FROM spectrum WHERE received_at < 9e15;"
      "DELETE FROM heartbeat WHERE received_at < 9e15;" "COMMIT;"`: the row-wise path today.
   4. `sqlite3 fast.db ".timer on" "BEGIN IMMEDIATE;" "SELECT max(received_at) FROM spectrum;" "SELECT
      max(received_at) FROM heartbeat;" "DELETE FROM spectrum;" "DELETE FROM heartbeat;" "COMMIT;"`: the fast path.
   5. Record both real times, the file size and the date. Expected: the fast path well under the row-wise one, since
      a `DELETE` without `WHERE` on a table without triggers takes SQLite's truncate optimisation and frees whole
      pages instead of visiting each row. A fast path that is not faster is a blocker with ⚠️.
   6. Recorded 2026-11-06 on `persistent-history` (Task 2, over `7cf1765`), dev laptop, warm OS cache, file
      799 457 280 bytes (762.4 MB), 172 800 spectra. The statements ran through Python's `sqlite3` module (SQLite
      3.50.4) instead of the `sqlite3` CLI, one fresh copy per run, three rounds: row-wise 830.2, 965.1, 908.5 ms
      (median 908.5 ms); fast path 77.9, 82.0, 75.4 ms (median 77.9 ms), about 11 times faster.
9. Cold read of the start path (a reboot drops the OS file cache; the tool alone cannot measure cold):
   1. `dotnet run -c Release --project SemiSpec/SemiSpec.Tools.StoreBenchmark -- --rows 172800 --device 2000
      --churn-rounds 0 --folder C:\Temp\semispec-cold --keep`. `--churn-rounds 0` keeps every row in the past; the
      file has `user_version` 1, since the tool opens it through `SpectrumDatabase.Open`.
   2. Close SemiSpec. Delete `%LOCALAPPDATA%\SemiSpec\Spectra\spectra.db*`, then copy `spectra.db` and, if present,
      `spectra.db-wal` from `C:\Temp\semispec-cold` there.
   3. Restart Windows, start SemiSpec, do not connect.
   4. The log line "The trend history read for generation 1 landed N points: visible window after X ms, last chunk
      after Y ms" gives the cold numbers. Expected: N about 172 800 less the rows the retention at open deleted; Y at
      most 10 s, the 24 h budget of the spectrum-store plan; warm reference Y 720.4 ms and X 4.4 ms. Record X, Y,
      the disk model and the date. A Y over 10 s is a blocker with ⚠️, not a silent pass.

Manual smoke checklist (com0com pair `COM10`-`COM11`, emulator on `COM10`, app on `COM11`; `emu` stands for
`dotnet run --project SemiSpec/SemiSpec.Tools.BoardEmulator --`; the store folder is
`%LOCALAPPDATA%\SemiSpec\Spectra`):
1. Close SemiSpec, copy any SQLite file into the store folder as `spectra-1234.db` and `spectra-1234.db-wal`. Start:
   both are gone, `spectra.db` exists, the log names the legacy files deleted.
2. Run `emu --port COM10 --rate 1`, Connect, wait 2 min: the trend draws a line.
3. Close the app, start it again within one minute, do not connect: the empty-state overlay shows until the stored
   points land, then the last 2 min appear in the 10 min window.
4. Connect: the line resumes after a cut at the first new spectrum; the readout over that column says no spectra.
5. With `emu --rate 1` running, `taskkill /F /IM SemiSpec.exe`, start again: no message in the panel, the trend
   shows the points up to the kill (less at most the last unwritten batch) and a cut after them.
6. Close the app, overwrite `spectra.db` with random bytes
   (`[IO.File]::WriteAllBytes("$env:LOCALAPPDATA\SemiSpec\Spectra\spectra.db", (1..8192 | % { [byte](Get-Random
   -Max 256) }))`), start: one Warning entry in the message panel, `spectra.broken-<stamp>.db` beside a fresh
   `spectra.db`, and history works after Connect. Repeat twice more: at most 2 `spectra.broken-*.db` files remain.
7. Close the app, create an empty `spectra.db.damaged` beside `spectra.db`, start: the same Warning entry, the file
   set aside, the marker gone.
8. Test machine only: close the app, move the clock 25 h forward, start: the trend shows nothing stored, and
   `sqlite3 spectra.db "select count(*) from spectrum"` prints 0. Move the clock back.

## Progress Tracking
- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update plan if implementation deviates from original scope

## Solution Overview
- **One history file.** `StoreFiles.HistoryFile(folder)` names `spectra.db`; `AddUi` registers it as the
  `StoreFile`. `Prepare` creates the folder and deletes the legacy files: each `spectra-<digits>.db` that
  `DatabaseOf` recognises, with its sidecars, and orphan legacy sidecars. `spectra.db`, `spectra.db.damaged` and
  `spectra.broken-*.db` carry no digits after `spectra-`, so `DatabaseOf` never matches them. A legacy file a running
  3.0 copy holds open fails its delete and stays, as the sweep did. The cleanup runs at every start; after the first
  it finds nothing. `Sweep`, `InstanceFile`, `StoreSweep` and the hourly sweep in `StoreRetention` go. The result
  carries the count of deleted legacy files, which the writer logs at Information when it is not 0.
- **Retention at open.** The writer runs one retention pass right after the open, then once a minute as before.
  A file closed for days loses its expired rows before the trend reads it.
- **Retention fast path.** `DeleteBefore` reads `max(received_at)` of both tables inside its `BEGIN IMMEDIATE`
  transaction (one index seek each). When both are below the cutoff or null, it runs `DELETE FROM spectrum;
  DELETE FROM heartbeat;` without `WHERE`; otherwise the row-wise statements as today. The tables have no
  triggers, so SQLite applies its truncate optimisation and drops whole pages; the row-wise delete visits every row
  and its index entry, which on a fully expired day of about 765 MB is the slowest statement the writer ever runs,
  and it runs before `Opened` completes. Freed pages stay in the file and are reused, as today (no `VACUUM`).
  Acceptance item 8 measures both paths.
- **Schema version.** Version 1 is the current schema. `SpectrumDatabase.Open` reads `PRAGMA user_version` and the
  table count from `sqlite_master`:

  | `user_version` | Tables | Open |
  | --- | --- | --- |
  | 0 | none | creates the schema and sets `user_version = 1` in one transaction |
  | 1 | any | uses the file |
  | 0 | some | fails with `StoreSchemaMismatchError(found: 0)` |
  | other | any | fails with `StoreSchemaMismatchError(found)` |

  On a mismatch the writer deletes the file with its sidecars and the damage marker, opens again, and logs one
  Warning naming both versions; no panel entry, since an app update causes it and the operator can do nothing about
  it. Delete, not rename: the rows are disposable 24 h history, no build reads another version's file, and a renamed
  file would hold up to about 765 MB of disk for nothing. The set-aside slots stay for damaged files.
- **Downgrade.** An older build with a lower `SchemaVersion` installed over a newer one sees the newer version,
  deletes the history on the mismatch and logs one Warning; the operator starts with an empty trend. 3.0 builds do
  not read `spectra.db` at all: their sweep matches `spectra-<digits>.db` only, so under 3.0 the file stays on disk
  unread until a build of this plan or later runs again.
- **Damaged file at open.** A `SqliteException` with `SqliteErrorCode` 26 (`SQLITE_NOTADB`) or 11 (`SQLITE_CORRUPT`)
  during `Open` (pragmas, version read, schema read) becomes `StoreFileUnreadableError` carrying the exception; any
  other failure (locked, cannot open, I/O) stays a plain failure, because the file may be intact. On that error the
  writer calls `StoreFiles.SetAside(path, localNow)` (the connection is already disposed), which renames the file to
  `spectra.broken-<yyyyMMdd-HHmmss>.db` (local time, the clock the operator reads), moves its `-wal` beside it as
  `<new name>-wal`, deletes the `-shm` (rebuilt from the `-wal` on any open) and the damage marker, and deletes the
  oldest set-aside files beyond 2, ordered by name (the stamp sorts). Then it opens a fresh file and reports
  `StoreFileReplacedError` (Warning) through `TryReportFailure`; the log line names both paths. Two slots keep the
  latest damage and show whether it repeats, and cap the extra disk at about 1.5 GB. A rename or second open that
  fails ends in the existing `StoreOpenError`. No `PRAGMA quick_check` at open: it reads every page, about 765 MB,
  before the trend can read. `synchronous=NORMAL` in WAL mode loses at most the last transactions on a power cut and
  leaves the file intact (`20261104-spectrum-store.md`, Solution Overview, Connection settings), so a damaged file
  means a disk fault or a foreign write, not a routine crash.
- **Damage past page 1.** The open reads only the header and the schema page, so damage deeper in the file shows
  later, as a failed write or read. `StoreDamage.IsDamage(Exception)` is true for a `SqliteException` with code 11
  or 26 anywhere in the cause chain. On such a failure the failing party calls `StoreFiles.MarkDamaged(path)`,
  which creates the empty file `spectra.db.damaged` and swallows its own `IOException` and
  `UnauthorizedAccessException`: the writer after a failed batch, a failed retention pass or a failed checkpoint;
  `SpectrumHistory` after a failed read. The existing failure routes report as today (`StoreWriteError`,
  `TrendHistoryError`, `TrendMomentError`). The next open finds the marker before it opens the file and sets the
  file aside exactly as an unreadable one, with the same message. The running copy keeps its connection: a rename
  under an open connection fails on Windows, and the writer can still append to the intact pages. A marker, not a
  rename at the failure: the reader and the writer run on different threads with their own connections, and only the
  open has the file to itself.
- **Store ready signal.** `SpectrumStore.Opened` is a `Task<bool>` from a `TaskCompletionSource<bool>` with
  `RunContinuationsAsynchronously`. The writer completes it true after the open, the set-aside or replace, and the
  retention at open; false when the open fails or a cancel ends the writer before the open; a `StopAsync` before
  `Start` completes it false. The trend's start read waits for it, so a reader never opens a file the writer is
  about to delete, rename or create.
- **Trend start read.** The cache records `RunStart = timeProvider.GetUtcNow()` in its constructor. `Configure`
  resolves the cache before `Opened` starts the distributor (`App.axaml.cs:75-81`, `:200-209`), so every live frame
  is received after `RunStart`, and every stored row before it belongs to an earlier run.
  `TrendFeed.Start` subscribes the start read as one `Observable.Create((observer, token) => ...)` tracked like every
  read: `await storeOpened.WaitAsync(token)`; on true, `readSpectrum(RunStart - 1 ms, token)`; on a spectrum,
  `cache.RequestStoredDay(new TrendKey(nm, delta))`. Disposing the subscription in `StopAsync` or `Dispose` cancels
  the token, so a stop ends the wait before `Opened` completes as well as the key read after it. Under the cache's
  lock `RequestStoredDay` does nothing when a key change already raised a request (`generation != 0`), since that
  re-read covers the stored day up to its own recent start. Otherwise it keeps a live key if a live spectrum set
  one, else takes the stored key, raises the generation and emits `ReReadRequest(generation, key, RunStart)` on
  `ReReadRequested`. From there the existing path runs: 500 ms throttle, the window at the dispatch, `Switch`,
  `TrendHistoryPlan.For` with `newest = RunStart` when no live point exists, so the plan covers
  `[RunStart - 24 h, RunStart)`, the visible window first. A later key change cancels it through `Switch`. A failed
  last-spectrum read reports one `TrendHistoryError`; an empty store or `Opened` false reads nothing.
- **UI while it runs.** No new UI. The chart's empty-state overlay stays until the first chunk with points lands.
  The visible window is the last 10 min before now, so when the app was closed for longer the window is empty and
  the overview band fills as the hour chunks land, newest first. The spectrum view and Connect work throughout,
  since the read runs on the pool.
- **NoSpectra breaks from point gaps.** Every spectrum, live or stored, is one point, NaN for an uncovered device,
  so the fold derives the NoSpectra rule from the points: a point more than `SilenceLimit` after the point before it
  adds a break with 0 lost at its time. NaN points count, as their spectra did in `FrameContinuity`.
  `TrendTimeline.Fold` compares each point in range with its predecessor, also the one just before the range.
  `FrameContinuity` drops `_lastSpectrumAt` and the `NoSpectra` member; it keeps the `seq` reset, Loss and failure
  marks. One rule covers a live silence, the time the app was closed, and the closed periods inside the stored day,
  which `FrameContinuity` could not see because it watches the current run's frames only.
  - While a key change's chunks land, a not-yet-read hour shows as a break until it lands.
  - Frames the store dropped (full queue, failed batch) are absent from the file (`20261104-intensity-trend.md`,
    Solution Overview, the re-read paragraph), so in loaded history a dropped stretch over 10 s folds as a NoSpectra
    break although the board sent spectra; in the live run the same stretch still draws as a line.
- **Expiry while idle.** `FoldCurrent` runs `DropExpired` before it folds, so an app left running without frames
  stops showing points older than 24 h; before this, loaded history would stay on screen past a day.
- **History read log line.** `ReadHistoryAsync` logs one Information line per read that ends: generation, points
  landed, ms to the first chunk (the visible window), ms to the last chunk. Key changes are rare, and the line is
  the cold-read measurement. The key-change Debug line becomes "The trend re-reads the stored history at {nm} nm,
  delta {Delta}, generation {Generation}", since the start read raises the same request.

## Technical Details
Start-up order after this plan:

| Step | Where | What |
| --- | --- | --- |
| 1 | `App.Run` `AfterSetup` | `Configure`; the cache records `RunStart` |
| 2 | `Opened` | store `Start`, trend feed `Start` (start read waits on `Opened`), distributor `Start` |
| 3 | writer task | `Prepare` (legacy delete), marker check, `Open` (version, set-aside), retention, `Opened` |
| 4 | trend feed | last stored spectrum, `RequestStoredDay`, throttled history read, chunks land |

New and changed members:
- `StoreFiles`: `HistoryFile(folder)`; `Prepare(path)` returns `Result<int>`, the legacy files deleted;
  `SetAside(path, localNow)` returns `Result<string>`, the new path; `MarkDamaged(path)`; `IsMarkedDamaged(path)`;
  `DeleteWithSidecars` also deletes the marker.
- `StoreDamage` (`Core/Store/`): `IsDamage(Exception)`, `IsDamage(IEnumerable<IError>)`.
- `SpectrumDatabase`: `SchemaVersion = 1`; `Open` is version-gated and fails with `StoreSchemaMismatchError` or
  `StoreFileUnreadableError`; `DeleteBefore` takes the fast path when every row has expired.
- `SpectrumHistory`: marks the file on a damage failure.
- `StoreOpening` (`UI/Store/`): the writer's open sequence: prepare, marker, open, replace or set aside, reopen,
  report. It keeps `StoreWriter` (280 lines today) under the 300-line class limit of `csharp.md`.
- `SpectrumStore`: `Task<bool> Opened`. `StoreRetention`: `Run` deletes expired rows only, marks on damage.
- `IntensityCache`: `DateTimeOffset RunStart`, `void RequestStoredDay(TrendKey storedKey)`, `DropExpired` in
  `FoldCurrent`.
- `TrendFeed`: the constructor gains `Task<bool> storeOpened`; the start read; the history read log line.
- `FrameContinuity`, `Continuity`: no `NoSpectra`. `TrendTimeline.Fold`: NoSpectra breaks from point gaps.

New resx keys (en and ru): `StoreFileReplacedTitle` en "Spectrum history file was damaged" / ru "Файл истории
спектров был повреждён"; `StoreFileReplacedDetail` en "The damaged file was set aside and a new empty file started.
The history before this start is not shown." / ru "Повреждённый файл отложен, начат новый пустой файл. История до
этого запуска не отображается."; `StoreFileReplacedRemedy` en "No action is needed. If this repeats, send the log
file to the developer." / ru "Действий не требуется. Если это повторится, передайте файл журнала разработчику."

## What Goes Where
- **Implementation Steps**: the store file rules, retention, the schema version, the set-aside path, the damage
  marker, the ready signal, the break rule, the start read, tests and docs in this repo.
- **Post-Completion**: the cold read and the smoke run on the operator PC.

## Implementation Steps

### Task 1: One history file, legacy cleanup, retention at open
**Files:**
- Modify: `SemiSpec/SemiSpec.Core/Store/StoreFiles.cs`
- Modify: `SemiSpec/SemiSpec.UI/Store/StoreRetention.cs`, `StoreWriter.cs`
- Modify: `SemiSpec/SemiSpec.UI/UiServiceCollectionExtensions.cs`
- Modify: `SemiSpec/SemiSpec.Tests.Unit/Core/Store/StoreFilesTests.cs`
- Modify: `SemiSpec/SemiSpec.Tests.Unit/UI/Store/SpectrumStoreWriteTests.cs`, `SpectrumStoreTestBase.cs`
- Modify: `SemiSpec/SemiSpec.Tests.Unit/UI/Di/CompositionRootTests.cs`, `AppLifecycleTestBase.cs` (the instance
  file comment)

- [x] record the test count of `dotnet test SemiSpec.slnx` at the `ui-redesign` merge commit in Context
- [x] replace `InstanceFile` with `HistoryFile`; `Prepare` creates the folder and deletes legacy files and orphan
  legacy sidecars, returning the count; remove `Sweep` and `StoreSweep`
- [x] `StoreRetention`: remove the sweep and `LogSweep`; `StoreWriter`: log the legacy count, run one retention pass
  right after the open
- [x] `AddUi` registers `StoreFile(StoreFiles.HistoryFile(paths.StoreFolder))`
- [x] rewrite the `Prepare` and name tests of `StoreFilesTests` per Acceptance Evidence item 1
- [x] write `RetentionAtOpenDeletesRowsOlderThanADay` and `LegacyFilesAreDeletedAtOpen`; adjust
  `RetentionRunsOnceAMinuteAlsoWithoutFrames`; rename the composition-root test
- [x] run `dotnet test SemiSpec.slnx` - must pass before next task
- ➕ `SpectrumDatabase.Schema` uses `CREATE TABLE IF NOT EXISTS` and `CREATE INDEX IF NOT EXISTS`: without the
  start-up wipe, a plain `CREATE TABLE` fails the second start. Task 3 replaces it with the version gate.

### Task 2: Retention fast path
**Files:**
- Modify: `SemiSpec/SemiSpec.Core/Store/SpectrumDatabase.cs`
- Modify: `SemiSpec/SemiSpec.Tests.Unit/Core/Store/SpectrumDatabaseTests.cs`
- Modify: `SemiSpec/SemiSpec.Tests.Unit/UI/Store/SpectrumStoreWriteTests.cs`

- [x] `DeleteBefore`: read both maxima in the transaction; empty both tables when every row has expired
- [x] write the three `DeleteBefore` tests of Acceptance Evidence item 1 and `RetentionAtOpenEmptiesAFullyExpiredFile`
- [x] run Acceptance Evidence item 8 and record both timings in this plan (item 8.6: row-wise 908.5 ms, fast path
  77.9 ms, medians)
- [x] run `dotnet test SemiSpec.slnx` - must pass before next task (996 of 996)

### Task 3: Schema version
**Files:**
- Modify: `SemiSpec/SemiSpec.Core/Store/SpectrumDatabase.cs`
- Create: `SemiSpec/SemiSpec.Core/Store/StoreOpenErrors.cs` (`StoreSchemaMismatchError`, `StoreFileUnreadableError`)
- Create: `SemiSpec/SemiSpec.UI/Store/StoreOpening.cs`
- Modify: `SemiSpec/SemiSpec.UI/Store/StoreWriter.cs`
- Modify: `SemiSpec/SemiSpec.Tests.Unit/Core/Store/SpectrumDatabaseTests.cs`
- Modify: `SemiSpec/SemiSpec.Tests.Unit/UI/Store/SpectrumStoreFailureTests.cs`, `SpectrumStoreWriteTests.cs`

- [x] `SpectrumDatabase.Open`: read `user_version` and the table count, apply the table of Solution Overview, create
  the schema and set the version in one transaction
- [x] create `StoreOpening` from `StoreWriter.OpenDatabase`: prepare, open, on a mismatch delete with sidecars,
  log one Warning and open again
- [x] write the version tests of Acceptance Evidence item 1 (`OpenOfANewFile...`, `ReopenKeepsTheRows`, the two
  mismatch tests) and `ASchemaMismatchStartsAFreshFileAndLogsOneWarning`
- [x] write `TheHistorySurvivesAStoreRestart`, the first test that reopens a file
- [x] run `dotnet test SemiSpec.slnx` - must pass before next task (1002 of 1002)
- ➕ `Schema` is back to plain `CREATE TABLE` and `CREATE INDEX`: the gate creates the schema only in a file
  without tables, so the Task 1 `IF NOT EXISTS` stopgap is gone. `StoreFileUnreadableError` moves to Task 4,
  its first producer.

### Task 4: Damaged file set aside at open
**Files:**
- Modify: `SemiSpec/SemiSpec.Core/Store/SpectrumDatabase.cs`, `StoreFiles.cs`, `StoreOpenErrors.cs`
- Modify: `SemiSpec/SemiSpec.UI/Store/StoreOpening.cs`, `StoreErrors.cs`
- Modify: `SemiSpec/SemiSpec.UI/Messages/ErrorMessageMapper.cs`
- Modify: `SemiSpec/SemiSpec.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiSpec/SemiSpec.Tests.Unit/Core/Store/SpectrumDatabaseTests.cs`, `StoreFilesTests.cs`
- Modify: `SemiSpec/SemiSpec.Tests.Unit/UI/Store/SpectrumStoreFailureTests.cs`
- Modify: `SemiSpec/SemiSpec.Tests.Unit/UI/Messages/ErrorMessageMapperTests.cs`

- [x] `Open` turns codes 26 and 11 into `StoreFileUnreadableError`; other failures stay plain
- [x] `StoreFiles.SetAside`: rename with the local stamp, move the `-wal`, delete the `-shm`, keep the 2 newest
- [x] `StoreOpening`: on unreadable, set aside, reopen, report `StoreFileReplacedError` (Warning) with both paths in
  the log line; a failed rename or reopen ends in `StoreOpenError`
- [x] add `StoreFileReplacedError`, its mapper arm and the three resx keys in both sets
- [x] write the unreadable and set-aside tests of Acceptance Evidence item 1 and the mapper test; the two damage
  tests pin the ASSUMPTION on codes 26 and 11
- [x] run `dotnet test SemiSpec.slnx` - must pass before next task (1011 of 1011)
- ➕ the report writes two Warning lines: `StoreOpening` logs the one naming both paths, and `TryReportFailure`
  logs the panel text with the SQLite stack. `StoreWriter` passes the panel, scheduler and clock to `StoreOpening`.
  The marker half of `SetAsideDeletesTheSharedMemorySidecarAndTheMarker` waits for Task 5.

### Task 5: Damage marker
**Files:**
- Create: `SemiSpec/SemiSpec.Core/Store/StoreDamage.cs`
- Modify: `SemiSpec/SemiSpec.Core/Store/StoreFiles.cs`, `SpectrumHistory.cs`
- Modify: `SemiSpec/SemiSpec.UI/Store/StoreWriter.cs`, `StoreRetention.cs`, `StoreOpening.cs`
- Modify: `SemiSpec/SemiSpec.Tests.Unit/Core/Store/StoreFilesTests.cs`, `SpectrumDatabaseTests.cs`,
  `SpectrumHistorySpectrumTests.cs`
- Modify: `SemiSpec/SemiSpec.Tests.Unit/UI/Store/SpectrumStoreFailureTests.cs`

- [x] `StoreDamage.IsDamage`; `StoreFiles.MarkDamaged` and `IsMarkedDamaged`; `SetAside` and
  `DeleteWithSidecars` delete the marker
- [x] mark after a damaged batch, retention pass, checkpoint and history read
- [x] `StoreOpening` sets a marked file aside before it opens it
- [x] write the marker tests of Acceptance Evidence item 1, including the held-lock batch that marks nothing; the
  damaged-table test pins the ASSUMPTION on damage past page 1
- [x] run `dotnet test SemiSpec.slnx` - must pass before next task (1020 of 1020)
- ➕ codes 11 and 26 live in `StoreDamage` only; `SpectrumDatabase.Open` filters through it. The page damage helper
  `Tests.Unit/Core/Store/DamagedPages.cs` serves the database, history and store tests. Added beyond item 1:
  `DeleteWithSidecarsDeletesTheMarker` and `ADamagedRetentionPassMarksTheFile`. The set-aside Warning line now
  names the cause: "... was set aside as {SetAsidePath} and a new one started: {Damage}".
- ➕ `Prepare` deletes a damage marker whose `spectra.db` is gone (deleted by hand), so the open no longer tries to
  set aside a missing file and end in `StoreOpenError` at every start. `PrepareKeepsNamesOutsideTheLegacyPattern`
  keeps the marker beside an existing `spectra.db`; `PrepareDeletesADamageMarkerWhoseHistoryFileIsGone` pins the
  orphan case.

### Task 6: Store ready signal
**Files:**
- Modify: `SemiSpec/SemiSpec.UI/Store/SpectrumStore.cs`, `StoreWriter.cs`
- Modify: `SemiSpec/SemiSpec.Tests.Unit/UI/Store/SpectrumStoreWriteTests.cs`, `SpectrumStoreStopTests.cs`

- [x] add `Opened`, completed by the writer per Solution Overview; `StopAsync` before `Start` completes it false
- [x] write `OpenedCompletesTrueAfterTheRetentionAtOpen`, `OpenedCompletesFalseWhenTheOpenFails`,
  `OpenedCompletesFalseOnDisposeBeforeTheOpen`
- [x] run `dotnet test SemiSpec.slnx` - must pass before next task (1024 of 1024)
- ➕ a dispose during the open completes `Opened` false: the writer checks the token after the retention at open,
  before it completes `Opened` true. The dispose test holds the writer inside that retention pass with a clock
  whose reads block. `OpenedCompletesFalseWhenTheOpenFails` sits in `SpectrumStoreFailureTests` beside the other
  failed-open tests. The test base's `OpenedAsync` awaits `Opened` instead of the "Spectrum store file" log line.

### Task 7: NoSpectra breaks from point gaps
**Files:**
- Modify: `SemiSpec/SemiSpec.Core/Trend/FrameContinuity.cs`, `TrendTimeline.cs`
- Modify: `SemiSpec/SemiSpec.Tests.Unit/Core/Trend/FrameContinuityTests.cs`, `IntensityCacheHistoryTests.cs`
- Create: `SemiSpec/SemiSpec.Tests.Unit/Core/Trend/IntensityCacheGapTests.cs`

- [ ] remove `_lastSpectrumAt` and `NoSpectra` from `FrameContinuity` and `Continuity`; `TrendTimeline.Observe`
  inserts Loss breaks and failure marks only
- [ ] `TrendTimeline.Fold` adds a NoSpectra break for each point in range more than `SilenceLimit` after its
  predecessor
- [ ] write `IntensityCacheGapTests` of Acceptance Evidence item 2, `UncoveredPointsCountAsSpectraForTheGapRule`
  included; move the NoSpectra cases out of `FrameContinuityTests`; keep the store-equality test on gap-free rows
- [ ] run `dotnet test SemiSpec.slnx` - must pass before next task; the readout and chart tests that count
  `NoSpectraCount` must pass unchanged

### Task 8: Trend start read
**Files:**
- Modify: `SemiSpec/SemiSpec.Core/Trend/IntensityCache.cs`
- Modify: `SemiSpec/SemiSpec.UI/Trend/TrendFeed.cs`, `SemiSpec/SemiSpec.UI/UiServiceCollectionExtensions.cs`
- Create: `SemiSpec/SemiSpec.Tests.Unit/Core/Trend/IntensityCacheStoredDayTests.cs`
- Create: `SemiSpec/SemiSpec.Tests.Unit/UI/Trend/TrendFeedStartReadTests.cs`
- Modify: `SemiSpec/SemiSpec.Tests.Unit/UI/Trend/TrendFeedTestBase.cs`, `UI/Di/AppLifecycleTrendTests.cs`

- [ ] `IntensityCache`: `RunStart`, `RequestStoredDay`, `DropExpired` in `FoldCurrent`
- [ ] `TrendFeed`: take `storeOpened`, subscribe the tracked start read in `Start` with `WaitAsync(token)`, log the
  history read line, reword the key-change line; `AddUi` passes `SpectrumStore.Opened`
- [ ] write `IntensityCacheStoredDayTests` and `TrendFeedStartReadTests` of Acceptance Evidence item 2, the stop
  theory covering both phases
- [ ] write `TheTrendShowsTheStoredPointsAfterARestart` of Acceptance Evidence item 3
- [ ] run `dotnet test SemiSpec.slnx` - must pass before next task

### Task 9: Verify acceptance criteria
- [ ] verify every Overview item is implemented and the scope guard holds (`git diff` touches no `.axaml` file)
- [ ] run Acceptance Evidence items 1 to 6; record the final count
- [ ] run the warm benchmark of item 7 and record its trend rows with the date and commit
- [ ] verify edge cases: a store open that fails leaves the trend empty with one `StoreOpenError`; a key change
  during the start read cancels it; a close during the start read ends inside the 5 s close limit

### Task 10: [Final] Update documentation
- [ ] `docs/architecture/overview.md`: Startup (the store's history file, the start read after `Opened`);
  Components (the `StoreFiles` row and the store writer row); Trend, Continuity (NoSpectra from point gaps, the
  stored day loaded at start); Trend, Key change and re-read (the start read); Spectrum store, Files and sweep
  (one file, legacy cleanup, schema version, set-aside, the damage marker); Spectrum store, The writer service
  (retention at open and its fast path, `Opened`)
- [ ] `docs/architecture/README.md`, Locked decisions, the Spectrum storage row: one `spectra.db` kept across
  restarts, `user_version`, set-aside of damaged files
- [ ] `docs/architecture/ui-text.md`, The keys, the `ErrorMessageMapper` row: `StoreFileReplaced*`
- [ ] `readme.md`: no sentence becomes false (introduction: "хранит спектры за последние 24 часа"; the feature
  list: the trend for 24 h and the overview band); leave it
- [ ] move this plan to `docs/plans/completed/`

## Post-Completion
*Items requiring manual intervention or external systems - no checkboxes, informational only*

**Manual verification**:
- the smoke checklist of Acceptance Evidence on the dev machine with the port pair.
- the cold read of Acceptance Evidence item 9 on the operator PC, whose disk decides the number; record it in
  `overview.md`, Spectrum store.
- a real-board run: start, close, reopen within a minute; the trend continues after a cut.
