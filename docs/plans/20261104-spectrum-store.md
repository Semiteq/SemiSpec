# Spectrum store

## Overview
- Store every frame the app receives in a SQLite file of its own: every spectrum with its full pixels, its receive
  time, device, wavelength, `delta`, amplitude, SNR, Q, `seq` and `state`, and every heartbeat with its time, `seq` and
  `state`. One file per running instance under the local app data folder; the instance's own file starts empty; the
  data stays across reconnects within one run; rows older than 24 h are deleted.
- Write through one background writer that drains the store's own bounded queue in batched transactions. The frame
  distributor's subscriber only enqueues. A full queue or a failed batch is data loss and is counted.
- Count the frames the link's channel drops as well, and show both counts as one "Dropped frames" segment in the status
  bar. Lost spectra (`seq` gaps) stay their own segment.
- Serve the intensity-trend slice: the stored spectrum at a moment (the nearest row at or before it), the intensity
  series at a wavelength and `delta` over a time range, bucketed to min/max per output column, and the stored time
  range for the 24 h overview. Measure three ways to read the window intensity and keep the fastest.
- Join the app's lifecycle: the store starts when the window opens and stops in the close chain after the distributor,
  with a WAL checkpoint and a close.
- Scope guard: no trend UI, no change to the spectrum screen, no launch keys or config files, nothing sent to the board,
  no change to the old app.

## Context (from discovery)
- Roadmap: docs/plans/roadmaps/20261104-avalonia-rewrite-roadmap.md — slice spectrum-store
- Stacking base: local branch `spectrum-view` at `12bef84`; this slice works on the local branch `spectrum-store`
  created from it. Local commits only.
- Verified 2026-11-04 at `12bef84`: `dotnet test SpectrumVisualizer.slnx` passes 505 of 505.
- The slice brief is the roadmap's spectrum-store scope (`roadmap:192-217`); its named risk is the series query cost
  over about 170 000 rows and WAL growth during long reads (`roadmap:211`). The intensity-trend slice consumes the
  queries: the trend at the SCADA wavelength, a 24 h overview, gaps for lost frames and for frames of the spectrometer
  that does not cover the wavelength, recompute on a wavelength or `delta` change with the visible range first, and a
  click that shows a moment's spectrum through a historical source that rebuilds a `SpectrumMeasurement` per row
  (`roadmap:219-238`). The locked decision row: "SQLite (`Microsoft.Data.Sqlite`), a file per running instance under
  the local app data folder, wiped at start, 24 h retention" (`docs/architecture/README.md:40`). A second running copy
  is allowed (`README.md:32`), and the store keeps a file per instance (`roadmap:291-292`). The SQLite size of about
  700 MB is accepted as disposable data (`roadmap:311-312`).
- Link surface this slice reads and changes:
  - `SerialLink` creates its channel once, bounded 64, `DropOldest`, single writer and reader, with the drop callback
    `OnFrameDropped` (`SerialLink.cs:38-44`). The callback logs the first drop of a session at Debug and counts
    nothing (`SerialLink.cs:130-142`); `Connect` resets that flag (`:77`). `Publish` drops a status equal to the last
    one with `Fault` left out (`:118-128`) and is the one place every status passes, the session's and the
    Disconnected one (`:110`).
  - `LinkSession.Deliver` runs `SequenceTracker.Observe` and adds to `_lostSpectra` before `frames.TryWrite`
    (`LinkSession.cs:244-272`), so a frame the channel drops never reaches `LostSpectra`. The frames of one decode pass
    or drain share one `ReceivedAt` (`:241-251`). `StatusOf` builds every session status (`:322-325`), and the session
    publishes at least once per read or 1 s read timeout (`:150`, `:208-224`, `board-protocol.md:179-181`).
  - `LinkStatus(State, PortName, Board, LostSpectra, Fault)` (`LinkStatus.cs:19`); tests build it positionally
    (`AppLifecycleTests.cs:115`, `SerialLinkStreamTests.cs:36`).
  - `ReceivedFrame(BoardFrame Frame, DateTimeOffset ReceivedAt)` lives in the UI (`ReceivedFrame.cs:1-6`); nine files
    use it (`git grep -l ReceivedFrame`). `ReceivedAt` is the link scheduler's `Now`, UTC on `DefaultScheduler`
    (`board-protocol.md:186-190`, `UiServiceCollectionExtensions.cs:28-31`).
  - `FrameDistributor.Frames` hands every frame to each subscriber on the read task, inside its own try/catch, and
    never completes (`FrameDistributor.cs:53-57`, `:94-143`); a subscriber runs synchronously on that task, so it must
    not block (`overview.md:224-233`).
- App lifecycle: `Opened` refreshes the ports and starts the distributor (`App.axaml.cs:70-74`); `Closing` holds the
  close until `StopBackgroundServicesAsync` completes, inside one 5 s limit (`:27`, `:83-130`);
  `StopLinkThenDistributorAsync` stops the link, then the distributor in `finally` (`:132-145`); `Configure` resolves
  `ConfiguredServices` (`:169-177`, record `:197-202`). The container's dispose cancels what still runs
  (`overview.md:119-122`).
- Core this slice consumes:
  - `BoardFrame(BoardState State, byte Seq, SpectrumMeasurement? Measurement)`, null for a heartbeat
    (`BoardFrame.cs:6`); `SpectrumMeasurement` with `Device`, `WavelengthNm` (u16), `Delta` (byte), `Amplitude`,
    `Snr`, `Q` (float), `Pixels` (`ushort[]`), whose constructor rejects a wrong pixel count (`BoardFrame.cs:18-58`).
  - `SpectrometerCalibration.For(device)`, `FirstNm`/`LastNm` (the firmware table range: ATP-2000 189-1028 nm,
    ATP-8000 898-2461 nm), `PixelFor` clamping to that range, `Window(nm, delta)` returning the inclusive
    `PixelWindow(First, Last)` (`SpectrometerCalibration.cs:10-20`, `:35-39`, `:56-82`, `:98`).
  - `Spectrometer.PixelCount` 2048 and 512 (`Spectrometer.cs:13-18`).
  - A heartbeat is sent when nothing was sent for 3 s (`board-protocol.md:28`); a spectrometer's own failure bit shows
    only in heartbeats and in the other spectrometer's frames (`board-protocol.md:42-44`); a dark frame has every pixel,
    `amplitude` and `snr` zero (`board-protocol.md:58`).
  - `AppDataPaths(string LogFolder)` with `Under(root)` (`AppDataPaths.cs:6-20`), resolved in `Program.Main`
    (`Program.cs:19-27`); tests at `AppDataPathsTests.cs:16-44`.
- UI surface this slice changes:
  - Status bar: `AppStatusBarViewModel` follows `serialLink.StatusUpdates(uiScheduler)` (`AppStatusBarViewModel.cs:49-51`),
    raises every text from the `Status` setter (`:54-67`), and formats the lost count with `Resources.Culture`
    (`:94-95`). The view is a ten-column grid ending with `LostSpectraText` (`AppStatusBar.axaml:29-79`).
  - `MainWindow.MinimumWidth` is 1048 (`MainWindow.axaml.cs:12`), set from the Russian status bar at 1042.4 px
    (`overview.md:139-147`); `MainWindowTextFitTests` sums the status segments at `:73-78` with a six-digit count
    (`:36-37`).
  - `ErrorMessageMapper.Map` switches on error types (`ErrorMessageMapper.cs:14-51`); link errors derive from `Error`
    (`LinkErrors.cs:6`). Both resx sets hold 70 keys (`grep -c "<data"`).
  - `AddUi(LoggingSetup)` registers every service (`UiServiceCollectionExtensions.cs:20-45`); the composition and
    lifecycle tests build the container with `AddUi(_logging)` (`CompositionRootTests.cs:51`, `:166`,
    `AppLifecycleTests.cs:242-247`).
- Packages: `Directory.Packages.props:7-34` pins no SQLite package; the Core project references FluentResults only
  (`SpectrumVisualizer.Core.csproj`); the board emulator is the pattern for a tool project
  (`SpectrumVisualizer.Tools.BoardEmulator.csproj:1-16`, `EmulatorCommand.cs:84`, `Program.cs:13`). Tools sit under
  `SpectrumVisualizer/` on System.CommandLine 2.0.2 (`README.md:39`). `Microsoft.Data.Sqlite` is not in the local
  NuGet cache, so Task 1's restore needs nuget.org.
- SemiPlot (`C:/Users/admin/projects/SemiPlot`, paths relative to its root): it reads a PostgreSQL archive and has no
  SQLite, no local writer, no queue and no drop counting, so the storage and writer side has no SemiPlot source. What
  this slice borrows:
  - The bucket width `(to - from) / targetColumnCount` floored at 1 ms
    (`SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs:334-344`).
  - An empty bucket reads back as NaN, the gap column the chart draws (`PostgresDataProvider.cs:416-421`).
  - A cancelled read throws `OperationCanceledException` and never returns a failed result
    (`PostgresDataProvider.cs:183-186`).
  - The reader already reduces the window to one row per column, so the chart side decimates nothing
    (`SemiPlot/SemiPlot.DataSource.Postgres/BucketedRowFold.cs:5`).
  - For intensity-trend, not built here: the visible window is read first (`SemiPlot/SemiPlot.UI/Chart/ChartHistoryPlanner.cs:47-60`)
    and a superseded read is cancelled by `.Select(Read).Switch()` (`SemiPlot/SemiPlot.UI/Minimap/MinimapBandFeed.cs:112-119`).
    The query service takes a `CancellationToken` so that pipeline can cancel it.
- Test traits: `Component` (`Core|UI|Tools`), `Area`, `Category=Unit` (`CLAUDE.md:39-42`,
  `testing-strategy.md:22-24`). This slice adds the Area `Store`.

## Development Approach
- **testing approach**: Regular (code first, then tests in the same task).
- complete each task fully before moving to the next; small, focused changes.
- every task includes new or updated tests for its code; all tests pass before the next task.
- update this plan when scope changes during implementation.
- the old solution keeps building and passing its 13 tests; this slice does not touch it.

## Testing Strategy
- **unit tests** in `SpectrumVisualizer.Tests.Unit`. Store tests open real SQLite files in a fresh folder under
  `Path.GetTempPath()` per test (an instance field set in the constructor, never `static`) and delete it in
  `Dispose`. Each test disposes its connections and calls
  `SqliteConnection.ClearAllPools()` before the delete. ASSUMPTION: pooling keeps a closed connection's file handle
  open, so a delete fails without the pool clear; the connection string sets `Pooling=False` for the production
  connections in any case.
- Traits: `Component=Core, Area=Store` for `Core/Store/`; `Component=UI, Area=Store` for `UI/Store/`;
  `Component=Tools, Area=Store` for the benchmark tool; `Area=Link` for the link's drop counter; `Area=MainWindow`
  for the status bar segment and widths; `Area=Di` for composition and lifecycle; `Area=Resources` for the keys.
- Time: the writer reads a `TimeProvider`; tests pass a private manual `TimeProvider` subclass, as
  `FrameScheduleTests.ManualTimeProvider` does (`FrameScheduleTests.cs:254`). Every wait on the writer has a 5 s
  real-time limit (`Waits`, `UI/Waits.cs:11`).
- A slow store is made with real SQLite, not a test hook: a second connection on the same file holds
  `BEGIN IMMEDIATE`, so the writer's batch waits for the busy timeout and fails, and the queue fills behind it.
- Frames come from `ProtocolSamples.SpectrumFrame` and `Heartbeat` (`ProtocolSamples.cs:15-29`); its spectrum carries
  1310 nm and `delta` 5, which ATP-8000 covers and ATP-2000 does not (`ProtocolSamples.cs:27`,
  `SpectrometerCalibration.cs:10-20`).
- The 24 h benchmark runs in the tool, never in the unit suite; the suite runs the tool on 2 000 rows.
- no e2e framework exists; headless tests take that role. The port-pair run is manual.

## Acceptance Evidence
Automatable (baseline 505 of 505 at `12bef84`):
1. `dotnet test SpectrumVisualizer.slnx --filter "Area=Store"` passes, including:
   - `StoreFilesTests`: `TheInstanceFileIsNamedForTheProcessId` (`spectra-1234.db` under the store folder),
     `PrepareWipesTheOwnFileAndItsSidecars`, `PrepareDeletesTheFileOfADeadInstance`,
     `PrepareKeepsTheFileOfALiveInstance` (a second `SqliteConnection` holds the file open; it and its `-wal` survive),
     `PrepareDeletesOrphanSidecars`, `PrepareCreatesAMissingFolder`.
   - `SpectrumDatabaseTests`: `OpenSetsWalNormalSyncAndTheJournalLimit` (reads back `journal_mode` = `wal`,
     `synchronous` = 1, `journal_size_limit` = 67108864); `ASpectrumRoundTripsEveryField` (pixels, device, wavelength,
     `delta`, amplitude, SNR, Q, `seq`, `state`, `ReceivedAt` to the millisecond);
     `ANaNAmplitudeRoundTripsAsNaN`; `AHeartbeatGoesToTheHeartbeatTable`; `ABatchIsOneTransaction` (a batch whose
     last frame fails leaves no row of it); `RetentionDeletesRowsOlderThanTheCutoffFromBothTables`;
     `CheckpointTruncatesTheWal` (the `-wal` file is 0 bytes after `CheckpointAndClose`); `TheIntensityColumnHoldsTheMeanOfTheOwnWindow`
     (NULL when the device does not cover its own wavelength), unless Task 4 removes the column.
   - `SpectrumIntensityTests`: `CoversFollowsTheFirmwareTableRange` (ATP-2000 189 and 1028 in, 188 and 1029 out;
     ATP-8000 898 and 2461 in); `MeanAveragesTheInclusiveWindow`; `ADarkSpectrumHasMeanZero`; `ADeltaOfZeroTakesOnePixel`.
   - `IntensityBucketsTests`: `ARowLandsInTheColumnOfItsTime` (half-open columns, a row at `To` excluded);
     `AColumnKeepsMinMaxAndCount`; `AnEmptyColumnIsNaNWithCountZero`; `AnUncoveringRowCountsAsUncoveredOnly`;
     `TheColumnWidthIsTheRangeOverTheColumnsFlooredAtOneMillisecond`.
   - `SpectrumHistoryTests`: `SpectrumAtReturnsTheNearestRowAtOrBefore` (equal stamps resolve to the later id),
     `SpectrumAtBeforeTheFirstRowReturnsNone`, `TheStoredSpectrumRebuildsTheMeasurement`;
     `TheSeriesMeansTheWindowPerSpectrum`; `TheSeriesOfAWavelengthOneDeviceDoesNotCoverLeavesItsColumnsUncovered`;
     `ARowWithADifferentWavelengthReadsItsPixels` (the stored window differs from the queried one, the mean comes from
     the pixels); `DarkFramesPullTheColumnMinimumToZero`; `ACancelledSeriesThrowsOperationCanceled`;
     `TheStoredRangeSpansTheFirstAndLastSpectrum`; `TheHistoryReadsWhileTheWriterWrites` (WAL: a series runs between
     two writer batches without a busy error).
   - `SpectrumStoreTests`: `EveryFrameFromTheDistributorIsStored` (spectra and heartbeats, in order);
     `TheSubscriberOnlyEnqueues` (with the writer held by `BEGIN IMMEDIATE` on a second connection, `OnNext` returns
     within 50 ms); `AFullQueueCountsEachDroppedFrame` (writer held, 1024 + 5 frames, `DroppedFrames` = 5 plus the
     frames of failed batches, `DroppedFramesChanged` signalled); `AFailedBatchCountsItsFramesAndReportsOnce` (two
     failed batches, one Warning entry in a real panel); `AWriteAfterAFailureRunReportsAgainOnlyAfterASuccess`;
     `RetentionRunsOnceAMinuteAlsoWithoutFrames` (manual `TimeProvider`); `DataStaysAcrossLinkSessions` (frames from two
     connects in one file); `StopDrainsTheQueueThenCheckpoints`; `StopBeforeStartCompletesAtOnce`;
     `AFailedOpenReportsOneErrorAndStoresNothing` (the store folder path is a file).
   - `StoreBenchmarkCommandTests` and `StoreBenchmarkRunTests.ASmallRunPrintsEveryMeasurementAndTheStrategiesAgree`
     (`--rows 2000`: exit code 0, every metric row printed, the three read strategies give equal means per column to
     1e-9).
2. `dotnet test SpectrumVisualizer.slnx --filter "Area=Link"` passes, including `SerialLinkDropTests`:
   `AChannelDropCountsInTheStatus` (a reader that does not read, 64 + 3 frames written, `Status.DroppedFrames` = 3
   after the next health publish), `TheDropCountSurvivesAReconnect`, `TheDisconnectedStatusCarriesTheDropCount`,
   `AChannelDropDoesNotCountAsLost` (`LostSpectra` stays 0).
3. `dotnet test SpectrumVisualizer.slnx --filter "Area=MainWindow"` passes, including
   `AppStatusBarViewModelTests.TheDroppedSegmentSumsTheLinkAndTheStore`, `TheDroppedSegmentShowsZeroWhileDisconnected`,
   `AStoreDropOnAnotherThreadReachesTheTextOnTheUiScheduler`, `MainWindowLayoutTests` with the new segment realised,
   and `MainWindowTextFitTests.TheMinimumWidthHoldsTheConnectionRowAndTheLongestStatusTextsOnOneLine` with the
   dropped segment in the sum.
4. `dotnet test SpectrumVisualizer.slnx --filter "Area=Di"` passes, including: the store resolves as one instance
   with the instance file under the test root; `AppLifecycleTests.OpeningTheWindowStartsTheStore` (a frame fed through
   `FakePortStream` lands in the instance file); `ClosingTheWindowStopsTheStoreAfterTheDistributor` (the store's stop
   line follows the distributor's "The frame distributor stopped"); `AfterTheCloseTheStoreFileHasNoWal` (the `-wal`
   file is absent or 0 bytes).
5. `dotnet test SpectrumVisualizer.slnx --filter "Area=Resources"` passes with every new key in the Russian set.
6. `dotnet test SpectrumVisualizer.slnx` passes 505 plus every test this slice adds, none skipped.
7. `dotnet build SpectrumVisualizer.slnx -c Release` 0 warnings; `dotnet format SpectrumVisualizer.slnx
   --verify-no-changes` and `dotnet terse SpectrumVisualizer` exit 0; `dotnet build SpectrumVisualizer.sln` and
   `dotnet test SpectrumVisualizer.Tests` pass 13 of 13.
8. Storage benchmark: `dotnet run -c Release --project SpectrumVisualizer/SpectrumVisualizer.Tools.StoreBenchmark --
   --rows 172800` prints one table. It fills 24 h of ATP-2000 spectra at 2 per second (650 nm, `delta` 10), then
   measures; "warm" is the median of 5 runs after one discarded run. The targets below are ASSUMPTION until Task 4
   and Task 6 record the measured values next to them:

   | Metric | Target |
   | --- | --- |
   | Insert throughput, batches of 256 | at least 2 000 spectra/s |
   | File size per 1000 ATP-2000 spectra, after checkpoint | at most 5 MB (ASSUMPTION: about 4.6 MB, one 4 KB overflow page per row plus an eighth of a leaf page) |
   | File size for 24 h | recorded; ASSUMPTION: about 800 MB at 2 per second, 1.1 GB at the 2.8 per second port cap (`board-protocol.md:334-335`) |
   | Series, 10 min (1 200 rows) to 1 000 columns, each strategy, warm | at most 50 ms for the kept pixel strategy |
   | Series, 24 h (172 800 rows) to 1 000 columns, each strategy, warm | at most 10 s for the kept pixel strategy; at most 500 ms for the precomputed column |
   | `SpectrumHistory.IntensitySeriesAsync`, 10 min and 24 h, warm | within 10 % of its strategy's row |
   | `SpectrumHistory.SpectrumAtAsync`, warm | at most 5 ms |
   | One retention pass over a minute of rows (120) | at most 50 ms |
   | WAL size after the 24 h series runs beside a writer at 2 spectra/s | recorded; stays under the 64 MB journal limit |

   The decision rule of Task 4 picks the kept strategies from these numbers. A target the kept strategy misses is a
   blocker recorded with ⚠️ in Task 4 or Task 6, not a silent pass.
9. The `git grep` commands of `ui-theme.md:86` (insets) and `:99` (colours) print nothing.

Manual smoke checklist (owner; com0com pair `COM10`-`COM11`, the emulator on `COM10`, the app on `COM11`; `emu` stands
for `dotnet run --project SpectrumVisualizer/SpectrumVisualizer.Tools.BoardEmulator --`; `sqlite3` from
`winget install SQLite.SQLite`; `<pid>` is the process id the log line names):
1. Start the app. The log has the line "Spectrum store file ...\Spectra\spectra-<pid>.db", and that file exists.
2. Run `emu --port COM10 --rate 1`, Connect. After 60 s, `sqlite3 spectra-<pid>.db "select count(*) from spectrum"`
   prints about 60, and `select count(*) from heartbeat` prints 0.
3. Restart the emulator with `--rate 0` for 10 s: the heartbeat count rises by about 3. Restart it with `--rate 1`.
4. Press Disconnect, wait 15 s, press Connect. The spectrum count keeps rising from its value before the Disconnect;
   no row is deleted.
5. Close the app. The `-wal` file next to `spectra-<pid>.db` is absent or 0 bytes. Start the app again: a new file
   with the new process id appears, and the previous file is gone; the new file has 0 spectra.
6. Start a second copy of the app while the first runs. Both files exist, and the first app keeps storing (its count
   rises). Close the first copy, start a third: the first copy's file is gone, the second's stays.
7. Run `emu --port COM10 --bad-crc-every 5 --rate 1`: "Lost spectra" rises, "Dropped frames" stays 0.
8. Slow store: run `sqlite3 spectra-<pid>.db`, type `BEGIN IMMEDIATE;`, wait 10 s, type `ROLLBACK;`. Within 3 s of the
   `BEGIN` the message panel shows one Warning "Spectrum history write failed", and "Dropped frames" rises by the
   frames of the failed batches (about 10 at `--rate 1`). After `ROLLBACK` the count stops rising, and no second entry
   appears for the same incident.
9. Retention, on a test machine only: with the app storing, move the Windows clock 25 h forward. Within 70 s
   `select count(*) from spectrum where received_at < (strftime('%s','now') - 86400) * 1000` prints 0. Move the clock
   back.
10. Resize the window to its minimum width with an English and with a Russian Windows UI: every status segment shows
    whole on one line, "Dropped frames: 0" / "Отброшено кадров: 0" last.

## Progress Tracking
- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update plan if implementation deviates from original scope

## Solution Overview
- **Folders.** Core holds the storage code, which needs no UI type: `SpectrumVisualizer.Core/Store/` with the file
  rules, the database (schema, writes, retention, checkpoint), the intensity rules and the query service. The UI holds
  what joins the app: `SpectrumVisualizer.UI/Store/` with the background writer service and its errors. The benchmark
  is a console tool, `SpectrumVisualizer.Tools.StoreBenchmark`, that references Core only.
- **`ReceivedFrame` moves to Core.** `ReceivedFrame(BoardFrame Frame, DateTimeOffset ReceivedAt)` moves unchanged to
  `SpectrumVisualizer.Core/Protocol/ReceivedFrame.cs`, so the database writes the record the link produces and the
  tool needs no UI reference. It holds no UI type.
- **File per instance.** The store folder is `%LOCALAPPDATA%\SpectrumVisualizer\Spectra` (`AppDataPaths.StoreFolder`).
  The instance file is `spectra-<process id>.db`. Two live processes never share a process id, so two running copies
  never share a file. `StoreFiles.Prepare` runs before the open: it creates the folder, deletes the own file and its
  `-wal` and `-shm` sidecars (a dead process with the same id left them; this is the wipe at start), then tries to
  delete every other `spectra-*.db`. A delete that fails with `IOException` or `UnauthorizedAccessException` means a
  live instance holds the file open, and the file stays; a delete that succeeds also deletes that file's sidecars.
  Sidecars whose `.db` is gone are deleted the same way. ASSUMPTION: SQLite on Windows opens the database without
  `FILE_SHARE_DELETE`, so `File.Delete` on a live instance's file fails; `PrepareKeepsTheFileOfALiveInstance` pins
  it. No process-id probing exists, so a reused id cannot keep a dead file alive.
- **The file stays after exit.** A clean exit checkpoints and closes the file and leaves it; the next start's sweep
  deletes it. A crash leaves the same state, so one cleanup path covers both, and a closed run's file can still be
  copied for diagnosis until the next start.
- **Heartbeats are stored.** A heartbeat is the only frame that carries the measuring spectrometer's own failure bit
  (`board-protocol.md:42-44`), and it marks 3 s without a spectrum (`board-protocol.md:28`). The trend needs both
  for failure markers and gaps. It costs 8 bytes at most every 3 s. Heartbeats go to their own table, so the series
  query scans spectra only.
- **Schema.** One file, two tables, no migration code: every start begins with an empty file, so a schema change in a
  later slice needs no upgrade path.

  ```sql
  CREATE TABLE spectrum (
      id INTEGER PRIMARY KEY,
      received_at INTEGER NOT NULL,   -- Unix milliseconds, UTC
      device INTEGER NOT NULL,
      wavelength_nm INTEGER NOT NULL,
      delta INTEGER NOT NULL,
      amplitude REAL, snr REAL, q REAL,   -- NULL stores NaN
      seq INTEGER NOT NULL,
      state INTEGER NOT NULL,
      intensity REAL,                 -- mean of the own window; NULL when the device does not cover its wavelength
      pixels BLOB NOT NULL);          -- 2 x P bytes, little-endian u16, pixel 0 first
  CREATE INDEX spectrum_time ON spectrum (received_at, device, wavelength_nm, delta, intensity);
  CREATE TABLE heartbeat (
      id INTEGER PRIMARY KEY,
      received_at INTEGER NOT NULL,
      seq INTEGER NOT NULL,
      state INTEGER NOT NULL);
  CREATE INDEX heartbeat_time ON heartbeat (received_at);
  ```

  `received_at` is not unique, because the frames of one decode pass share a stamp (`LinkSession.cs:241-251`), so
  `id` orders rows with equal stamps. The float columns are nullable: SQLite stores a NaN double as NULL
  (ASSUMPTION, pinned by `ANaNAmplitudeRoundTripsAsNaN`), and a board float can be NaN. `pixels` is last, so a read of
  the narrow columns never touches the blob's overflow page. `pixels` is written from
  `MemoryMarshal.AsBytes(pixels.AsSpan())` without a copy and read back with `MemoryMarshal.Cast<byte, ushort>`; the
  app runs on x64 Windows only (`README.md:24`), so the bytes are little-endian on both sides.
- **Connection settings.** `Data Source=<file>;Mode=ReadWriteCreate;Pooling=False;Default Timeout=2`. Every connection
  runs `PRAGMA journal_mode=WAL` (persistent in the file), `PRAGMA synchronous=NORMAL` and
  `PRAGMA journal_size_limit=67108864`. `synchronous=NORMAL` in WAL mode skips the fsync per commit, and a power loss
  can lose the last transactions but not corrupt the file; the data is disposable. The journal limit truncates the
  WAL back to 64 MB after a checkpoint, so a WAL that grew during a long read does not keep its size.
  ASSUMPTION: `Default Timeout` bounds Microsoft.Data.Sqlite's retry on `SQLITE_BUSY` to 2 s; the slow-store tests
  depend on it. Reader connections add `PRAGMA query_only=ON`.
- **Database.** `SpectrumDatabase` (Core) owns the writer connection: `Open(path)` (pragmas, schema),
  `Write(IReadOnlyList<ReceivedFrame>)` in one transaction with one reused parameterised command per table,
  `DeleteBefore(DateTimeOffset cutoff)` in its own transaction over both tables, `CheckpointAndClose()` running
  `PRAGMA wal_checkpoint(TRUNCATE)` then closing. Each returns a FluentResults `Result`; a `SqliteException` or
  `IOException` becomes an `ExceptionalError`. `Write` computes `intensity` from the frame's own wavelength and
  `delta` through `SpectrumIntensity`.
- **Background writer.** `SpectrumStore` (UI) is a singleton:
  - `SpectrumStore(FrameDistributor, StoreFile, MessagePanelViewModel, IScheduler uiScheduler, TimeProvider,
    ILogger<SpectrumStore>)`, where `StoreFile(string Path)` is the instance file registered by `AddUi`.
  - Its queue is `Channel.CreateBounded<ReceivedFrame>(1024)` with `FullMode = Wait`, `SingleWriter` and
    `SingleReader`. 1024 frames hold about 6 min of ATP-2000 spectra at the 2.8 per second port cap and about 4 MB of
    pixel arrays, which the frames hold anyway.
  - `Start()` subscribes `distributor.Frames` with an observer whose `OnNext` is one `TryWrite`. A `false` from
    `TryWrite` (queue full) adds 1 to the drop count. The subscriber never touches SQLite, never waits and never
    throws. Then it starts the writer task. `Start` after a start or a stop does nothing.
  - The writer task runs `StoreFiles.Prepare` and `SpectrumDatabase.Open` on the thread pool, so the UI thread does
    no file I/O. Frames that arrive meanwhile wait in the queue. A failed prepare or open logs at Error, reports one
    `StoreOpenError` through `TryReportFailure`, disposes the subscription and ends the task: history is off for the
    run, and no drop is counted, because no history exists to have holes.
  - Loop: wait for the queue or the next retention time, whichever comes first; take up to 256 frames with `TryRead`
    and write them as one batch. A failed batch adds its frame count to the drop count and logs at Error. The first
    failure after a success reports one `StoreWriteError` (Warning) through `TryReportFailure`; later failures stay
    quiet until a batch succeeds again, as the link's one entry per incident (`board-protocol.md:275-277`).
  - Retention: once a minute by the `TimeProvider`, also while no frame arrives, `DeleteBefore(now - 24 h)`. The
    cutoff uses the same wall clock as `ReceivedAt` (`DefaultScheduler.Now` and `TimeProvider.System` both read UTC
    wall time). No `VACUUM`: the freed pages are reused, so the file stays at about one day of data.
  - Drop count: `long DroppedFrames` (an `Interlocked` counter written by the distributor task and the writer task)
    and `IObservable<Unit> DroppedFramesChanged`, a private `Subject<Unit>` behind `Synchronize()`, exposed with
    `AsObservable()`, signalled after each increment. A reader reads `DroppedFrames` after the signal, the shape of
    `IDisplayedSpectrumSource` (`overview.md:57`); a value in the signal could arrive out of order from two threads.
  - `StopAsync()`: dispose the subscription (no more enqueues), complete the queue's writer, await the writer task,
    which writes what is still queued, then `CheckpointAndClose()`. A checkpoint that reports busy (a reader holds an
    old snapshot) logs at Debug and the connection closes anyway; the WAL stays and the next start deletes it with the
    file. `StopAsync` completes at once when nothing started. `Dispose()` cancels without waiting, for the container's
    synchronous dispose.
- **Link drop counter.** `SerialLink` counts the frames its channel drops: `OnFrameDropped` adds 1 to
  `_droppedFrames` before its log-once check. `LinkStatus` gains `public long DroppedFrames { get; init; }`, a
  non-positional member, so the existing positional constructions keep compiling. `Publish` stamps it:
  `status with { DroppedFrames = _droppedFrames }`, before the equality check, so the next session status after a
  drop (at most 1 s later) carries it, and the Disconnected status carries the final count. The count runs for the
  process and survives Connect, because the channel lives across sessions (`SerialLink.cs:44`) and the stored history
  covers the run. `_droppedFrames` is written and read on the session task only: the callback runs inside `TryWrite`
  in `LinkSession.Deliver`, and `Publish` runs on the same task.
- **Status bar segment.** One more segment after Lost spectra: "Dropped frames: {0}" / "Отброшено кадров: {0}", the
  link's count plus the store's. It shows a number in every state, also while disconnected, because both counts
  describe the run's stored history, not the current connection. `DroppedFramesText` is an `ObservableAsPropertyHelper`
  over `CombineLatest(serialLink.StatusUpdates(uiScheduler).Select(s => s.DroppedFrames),
  store.DroppedFramesChanged.ObserveOn(uiScheduler).StartWith(Unit.Default).Select(_ => store.DroppedFrames))`, summed
  and formatted with `Resources.Culture` as `LostSpectraText` is (`AppStatusBarViewModel.cs:94-95`). Its subscription
  joins `_disposables`. Lost spectra keep their meaning: board spectra that never arrived.
- **Query service.** `SpectrumHistory(string path)` (Core) opens a reader connection per call on the thread pool:
  - `SpectrumAtAsync(DateTimeOffset moment, CancellationToken)` returns `Result<StoredSpectrum?>`: the row with the
    largest `received_at <= moment`, ties to the larger `id`. `StoredSpectrum(SpectrumMeasurement Measurement,
    DateTimeOffset ReceivedAt, byte Seq, BoardState State)` rebuilds a new measurement per call, which the historical
    source of intensity-trend wraps in a `DisplayedSpectrum`.
  - `IntensitySeriesAsync(IntensityQuery query, CancellationToken)` returns `Result<IntensitySeries>`, with
    `IntensityQuery(ushort WavelengthNm, byte Delta, DateTimeOffset From, DateTimeOffset To, int Columns)`. For each
    spectrum in `[From, To)` it computes the mean of the pixels in `SpectrometerCalibration.Window(nm, delta)` of the
    row's device, dark frames included, and folds it into the column of its time. A row whose device does not cover
    the wavelength (`FirstNm <= nm <= LastNm` fails) counts as uncovered and adds no value.
  - `StoredRangeAsync(CancellationToken)` returns `Result<StoredRange?>` with the first and last spectrum's
    `ReceivedAt`, the extent of the 24 h overview.
  - Cancellation throws `OperationCanceledException`, never a failed result, as SemiPlot's provider does
    (`PostgresDataProvider.cs:183-186`). The series checks the token every 256 rows.
  - Long reads and WAL growth: a series over more than 1 h runs as one statement per hour of range, each its own read
    transaction, so no reader holds one snapshot for the whole 24 h and the writer's automatic checkpoints keep
    pace. Each slice folds into the same columns.
- **Series result.** `IntensitySeries(DateTimeOffset From, TimeSpan ColumnWidth, IntensityColumn[] Columns)` with
  `IntensityColumn(double Min, double Max, int Count, int UncoveredCount)`. Column `i` covers
  `[From + i x width, From + (i + 1) x width)`, `width = (To - From) / Columns` floored at 1 ms, as SemiPlot's
  `BucketFor` (`PostgresDataProvider.cs:334-344`). A column with `Count` 0 has NaN `Min` and `Max`, the gap SemiPlot
  draws (`PostgresDataProvider.cs:416-421`); `UncoveredCount > 0` with `Count` 0 tells intensity-trend the gap comes
  from the other spectrometer. Gaps from lost frames come from `seq`, `state` and the heartbeat table; intensity-trend
  adds that query, since it owns the marker rules (`roadmap:238` allows query additions).
- **Three read strategies and the decision rule.** The window intensity can come from:
  - the precomputed `intensity` column, read from the covering index `spectrum_time`, valid only for rows whose own
    `wavelength_nm` and `delta` equal the query's (the common case: SCADA rarely changes the wavelength);
  - a blob range read: `SqliteDataReader.GetBytes(ordinal, 2 x First, buffer, 0, 2 x count)` on `pixels`;
  - `substr(pixels, 2 x First + 1, 2 x count)` in the SQL, per device through a `CASE device`.

  The benchmark measures all three on one file. The pixel path keeps the faster of the blob range read and `substr`
  by the 24 h warm median. The `intensity` column and the index's last four columns stay when the precomputed read is
  at most half the kept pixel strategy's 24 h time; otherwise they are removed, and `spectrum_time` indexes
  `received_at` only. A kept column makes the series use it for matching rows and the pixel path for the others. The
  tool keeps all three strategies as raw SQL of its own, so the numbers can be measured again; `SpectrumHistory` holds
  the kept ones only.
- **Lifecycle.** `Opened` starts the store before the distributor, so the store's subscription exists before the
  first frame is read. The close chain becomes link, then distributor, then store, each in a `finally` of the one
  before, all inside the existing 5 s limit: the store's stop drains at most 1024 frames at the measured insert
  rate (target: under 1 s) and checkpoints at most a 64 MB WAL. After the limit the window closes anyway, and the
  container's dispose cancels the writer.

## Technical Details
Write path per frame (writer task):

| Frame | Table | Values |
| --- | --- | --- |
| Spectrum | `spectrum` | `received_at` = `ReceivedAt.ToUnixTimeMilliseconds()`, device byte, wavelength, `delta`, amplitude, SNR, Q (NaN as NULL), `seq`, `state` byte, `intensity`, pixels |
| Heartbeat | `heartbeat` | `received_at`, `seq`, `state` byte |

Intensity of one row for a query (`SpectrumIntensity`):
- covered: `calibration.FirstNm <= nm <= calibration.LastNm` for the row's device;
- window: `calibration.Window(nm, delta)`, inclusive `First..Last`;
- value: `sum(pixels[First..Last]) / (Last - First + 1)` as a double, one pass, no LINQ.

Series fold (`IntensityBuckets`, pure, Core): `column = (int)((t - From).Ticks / width.Ticks)`; rows with
`t < From` or `t >= To` are skipped; a covered row updates `Min`, `Max`, `Count`; an uncovered row adds 1 to
`UncoveredCount`.

Benchmark tool (`SpectrumVisualizer.Tools.StoreBenchmark`, System.CommandLine 2.0.2):

| Option | Default | Meaning |
| --- | --- | --- |
| `--rows <n>` | 172800 | spectra to insert, 500 ms apart, ending at the tool's start time |
| `--device 2000\|8000` | 2000 | the device of every row |
| `--folder <path>` | a new folder under `%TEMP%` | where the file goes |
| `--keep` | off | keep the file after the run |

It writes through `SpectrumDatabase.Write` in batches of 256 (650 nm, `delta` 10, pixels from a fixed-seed random
generator), checkpoints, then prints one row per metric of Acceptance Evidence item 8. The OS file cache cannot be
dropped without administrator rights, so the tool reports the first run after a fresh connection and the warm median
of 5, and says so in its header line.

New resx keys (en / ru):

| Key | English | Russian |
| --- | --- | --- |
| `StatusDroppedFramesFormat` | Dropped frames: {0} | Отброшено кадров: {0} |
| `StoreOpenTitle` | Spectrum history is off | История спектров отключена |
| `StoreOpenDetail` | The application could not create its spectrum history file. The spectrum view works; nothing is stored. | Приложение не смогло создать файл истории спектров. Спектр отображается, но не сохраняется. |
| `StoreOpenRemedy` | Restart the application. If the failure repeats, send the log file to the developer. | Перезапустите приложение. Если ошибка повторится, отправьте файл журнала разработчику. |
| `StoreWriteTitle` | Spectrum history write failed | Не удалось записать историю спектров |
| `StoreWriteDetail` | Received frames could not be written to the history file; they count as dropped frames. | Принятые кадры не записались в файл истории; они учтены как отброшенные. |
| `StoreWriteRemedy` | Check the free space on the system drive. The application keeps trying. | Проверьте свободное место на системном диске. Приложение продолжает попытки. |

Errors (`SpectrumVisualizer.UI/Store/StoreErrors.cs`): `StoreOpenError(string path)` mapped to Error severity,
`StoreWriteError()` mapped to Warning; each carries the cause as an `ExceptionalError` reason. The view leaves the path
out, so repeats coalesce, as the link errors leave the port out (`ErrorMessageMapper.cs:10-11`).

Data flow:
1. The link writes `ReceivedFrame` to its channel; a channel drop adds to the link's count (session task).
2. The distributor reads it and hands it to each subscriber (distributor task).
3. The store's subscriber writes it to the store queue or counts a drop (distributor task).
4. The writer writes up to 256 queued frames in one transaction and runs retention once a minute (writer task).
5. The status bar reads both counts on the UI scheduler.

## What Goes Where
- **Implementation Steps**: the package pin, the record move, the Core store, the benchmark tool, the writer service,
  the link counter, the status bar segment, the lifecycle, tests and docs in this repo.
- **Post-Completion**: the port-pair smoke run, the real-board check, and the benchmark on the operator PC.

## Implementation Steps

### Task 1: SQLite pin, store folder and the record move
**Files:**
- Modify: `SpectrumVisualizer/Directory.Packages.props`, `SpectrumVisualizer/SpectrumVisualizer.Core/SpectrumVisualizer.Core.csproj`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Core/AppDataPaths.cs`
- Move: `SpectrumVisualizer/SpectrumVisualizer.UI/Link/ReceivedFrame.cs` to `SpectrumVisualizer/SpectrumVisualizer.Core/Protocol/ReceivedFrame.cs`
- Modify: the eight files that use `ReceivedFrame` (usings only)
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Core/AppDataPathsTests.cs`

- [ ] pin `Microsoft.Data.Sqlite` 10.0.14 in `Directory.Packages.props` (alphabetical, after
  `Microsoft.CodeAnalysis.ResxSourceGenerator`, `:17`) and reference it from Core; `dotnet restore
  SpectrumVisualizer.slnx` needs nuget.org, because the package is not in the local cache
- [ ] add `StoreFolder` to `AppDataPaths` (`AppDataPaths.cs:6-20`): `SpectrumVisualizer\Spectra` beside `Logs`
- [ ] move `ReceivedFrame` to `SpectrumVisualizer.Core.Protocol` unchanged; fix the usings in `FrameDistributor.cs`,
  `LinkSession.cs`, `SerialLink.cs` and the five test files `git grep -l ReceivedFrame` lists
- [ ] write `AppDataPathsTests.TheStoreFolderSitsInsideTheAppFolder`; the trailing-separator test covers the new member
  through record equality (`AppDataPathsTests.cs:24-30`)
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 2: Instance file and the sweep
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.Core/Store/StoreFiles.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Core/Store/StoreFilesTests.cs`, `TempFolder.cs`

- [ ] implement `StoreFiles.InstanceFile(string folder, int processId)` returning `spectra-<id>.db` under the folder
- [ ] implement `StoreFiles.Prepare(string instanceFile)` returning `Result`: create the folder, delete the own file
  and sidecars, sweep the other `spectra-*.db` files and orphan sidecars by the delete rule of Solution Overview; a
  kept live file logs nothing here (the caller logs the result counts)
- [ ] write `TempFolder` (a fresh folder under `Path.GetTempPath()`, `ClearAllPools` and a recursive delete in
  `Dispose`) for every store test
- [ ] write the `StoreFilesTests` of Acceptance Evidence item 1 (`Component=Core`, `Area=Store`), including
  `PrepareKeepsTheFileOfALiveInstance` with an open `SqliteConnection` on the other file
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 3: Database: schema, batched writes, retention, checkpoint
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.Core/Store/SpectrumDatabase.cs`, `SpectrumIntensity.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Core/Store/SpectrumDatabaseTests.cs`,
  `SpectrumIntensityTests.cs`, `StoreSamples.cs`

- [ ] implement `SpectrumIntensity.Covers(Spectrometer, ushort nm)` and `Mean(ReadOnlySpan<ushort>, PixelWindow)` of
  Technical Details
- [ ] implement `SpectrumDatabase` of Solution Overview: connection string, the three pragmas, the schema, `Write`
  with two reused parameterised commands inside one transaction, `DeleteBefore`, `CheckpointAndClose`; NaN floats
  bind as `DBNull`
- [ ] write `StoreSamples` (spectra at given times, wavelengths, devices and dark, heartbeats) over `ProtocolSamples`
- [ ] write the `SpectrumIntensityTests` and `SpectrumDatabaseTests` of Acceptance Evidence item 1, reading rows back
  with plain SQL on a second connection
- [ ] write `ABatchIsOneTransaction`: a second connection creates `CREATE TRIGGER fail_seq_99 BEFORE INSERT ON
  spectrum WHEN NEW.seq = 99 BEGIN SELECT RAISE(ABORT, 'test'); END`, the batch ends with a frame of `seq` 99,
  `Write` returns a failed `Result`, and no row of the batch exists
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 4: Storage benchmark tool and the read decision
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tools.StoreBenchmark/SpectrumVisualizer.Tools.StoreBenchmark.csproj`,
  `Program.cs`, `BenchmarkCommand.cs`, `BenchmarkOptions.cs`, `ReadStrategies.cs`, `BenchmarkRun.cs`
- Modify: `SpectrumVisualizer.slnx`, `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/SpectrumVisualizer.Tests.Unit.csproj`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Tools/StoreBenchmarkCommandTests.cs`, `StoreBenchmarkRunTests.cs`
- Modify (by the decision): `SpectrumVisualizer/SpectrumVisualizer.Core/Store/SpectrumDatabase.cs`, this plan

- [ ] create the tool project in the shape of the board emulator (`SpectrumVisualizer.Tools.BoardEmulator.csproj:1-16`,
  `EmulatorCommand.cs:84`), referencing Core and System.CommandLine; add it to `SpectrumVisualizer.slnx` and to the
  test project's references
- [ ] implement the options of Technical Details with range checks (`--rows` at least 1 200, so the 10 min series has
  data), the fill through `SpectrumDatabase.Write`, and the three strategies of Solution Overview as raw SQL in
  `ReadStrategies`, each returning the same `IntensityColumn[]`
- [ ] measure and print insert throughput, file size per 1000 spectra, 24 h file size, the 10 min and 24 h series per
  strategy (first run and warm median), one retention pass, and the WAL size after a 24 h series that runs beside a
  writer thread at 2 spectra/s
- [ ] write `StoreBenchmarkCommandTests` (defaults, a rejected `--rows 10`, `--device 8000`) and
  `StoreBenchmarkRunTests.ASmallRunPrintsEveryMeasurementAndTheStrategiesAgree` (`Component=Tools`, `Area=Store`)
- [ ] run `dotnet run -c Release --project SpectrumVisualizer/SpectrumVisualizer.Tools.StoreBenchmark -- --rows
  172800` and record every number in Acceptance Evidence item 8 with the date, the machine (CPU, disk) and the
  commit; also run `--device 8000` once and record its file size per 1000 spectra
- [ ] apply the decision rule of Solution Overview: name the kept pixel strategy and whether the `intensity` column
  stays; if it goes, remove it and the index columns from the schema and from `SpectrumDatabaseTests`; record the
  decision in Solution Overview as the final state
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 5: Series fold
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.Core/Store/IntensityBuckets.cs` (with `IntensitySeries`,
  `IntensityColumn`, `IntensityQuery`)
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Core/Store/IntensityBucketsTests.cs`

- [ ] declare `IntensityQuery`, `IntensitySeries`, `IntensityColumn` of Solution Overview; the query rejects
  `Columns < 1` and `To <= From` with a failed `Result` from a factory, since the trend passes view geometry
- [ ] implement `IntensityBuckets` (column width, `Add(t, value)`, `AddUncovered(t)`, `ToSeries()`) as single-pass
  arithmetic with no allocation per row
- [ ] make `ReadStrategies` in the tool fold through `IntensityBuckets`, so the tool and the service share one fold
- [ ] write the `IntensityBucketsTests` of Acceptance Evidence item 1 as table tests (`Component=Core`, `Area=Store`)
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 6: Query service
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.Core/Store/SpectrumHistory.cs` (with `StoredSpectrum`, `StoredRange`)
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tools.StoreBenchmark/BenchmarkRun.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Core/Store/SpectrumHistoryTests.cs`

- [ ] implement `SpectrumAtAsync`, `IntensitySeriesAsync` (the strategies kept in Task 4, hour slices, the token
  checked every 256 rows) and `StoredRangeAsync` on reader connections with `query_only`
- [ ] map `SqliteException` to a failed `Result`; let `OperationCanceledException` pass
- [ ] add `SpectrumHistory.IntensitySeriesAsync` (10 min, 24 h) and `SpectrumAtAsync` rows to the tool's table; run
  the 172 800-row benchmark again and record those rows in Acceptance Evidence item 8
- [ ] write the `SpectrumHistoryTests` of Acceptance Evidence item 1 (`Component=Core`, `Area=Store`), including
  `TheHistoryReadsWhileTheWriterWrites` with a `SpectrumDatabase` writing on a second thread
- [ ] write `ACancelledSeriesThrowsOperationCanceled` with a token cancelled before the call and one cancelled after
  the first slice of a 2 h range
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 7: Background writer service
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.UI/Store/SpectrumStore.cs` (with `StoreFile`),
  `SpectrumVisualizer/SpectrumVisualizer.UI/Store/StoreErrors.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Messages/ErrorMessageMapper.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Store/SpectrumStoreTests.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Messages/ErrorMessageMapperTests.cs`

- [ ] implement `SpectrumStore` of Solution Overview: queue, enqueue-only subscriber, writer loop with batches of 256,
  retention by the `TimeProvider`, the drop counter and `DroppedFramesChanged`, `Start`, `StopAsync`, `Dispose`; log
  the file path at Information on open, the sweep result at Debug, the stop with the stored and dropped totals at
  Debug
- [ ] add `StoreOpenError` and `StoreWriteError`, their mapping (Error, Warning) and the six `Store*` resx keys of
  Technical Details in both sets
- [ ] write the `SpectrumStoreTests` of Acceptance Evidence item 1 (`Component=UI`, `Area=Store`) over a
  `FrameDistributor` on a test channel with the link's options (as `FrameDistributorTests` does) and a real
  `MessagePanelViewModel`
- [ ] write `TheSubscriberOnlyEnqueues` and `AFullQueueCountsEachDroppedFrame` with a second connection holding
  `BEGIN IMMEDIATE` on the instance file; `AFullQueueCountsEachDroppedFrame` feeds the distributor through an
  unbounded test channel, because a 64-slot `DropOldest` channel would drop before the store queue fills, and this
  test counts the store's drops, not the link's
- [ ] write `ErrorMessageMapperTests` rows for both errors (severity, title, no path in the detail)
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 8: Link drop counter
**Files:**
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Link/SerialLink.cs`, `LinkStatus.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Link/SerialLinkDropTests.cs`

- [ ] add `DroppedFrames` to `LinkStatus` as a non-positional `init` member; count in `OnFrameDropped`
  (`SerialLink.cs:133-142`) before the log-once check; stamp it in `Publish` (`:119-128`) before the comparison
- [ ] write the `SerialLinkDropTests` of Acceptance Evidence item 2 (`Component=UI`, `Area=Link`) on
  `SerialLinkTestBase`, with no reader on `Frames` so the channel fills
- [ ] confirm the existing positional `LinkStatus` assertions (`AppLifecycleTests.cs:115`,
  `SerialLinkStreamTests.cs:36`) still pass with `DroppedFrames` 0
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 9: Dropped-frame segment in the status bar
**Files:**
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/MainWindow/AppStatusBarViewModel.cs`, `AppStatusBar.axaml`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/MainWindow/AppStatusBarViewModelTests.cs`,
  `MainWindowLayoutTests.cs`

- [ ] add `StatusDroppedFramesFormat` to both resx sets
- [ ] take `SpectrumStore` in the view model's constructor and add `DroppedFramesText` of Solution Overview as an
  `ObservableAsPropertyHelper` with its subscription in `_disposables`
- [ ] add a separator and `DroppedFramesText` after `LostSpectraText` (`AppStatusBar.axaml:71-77`; the grid gains two
  `Auto` columns)
- [ ] write `TheDroppedSegmentSumsTheLinkAndTheStore`, `TheDroppedSegmentShowsZeroWhileDisconnected`,
  `AStoreDropOnAnotherThreadReachesTheTextOnTheUiScheduler`; extend the realised-segment assertions in
  `MainWindowLayoutTests`
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 10: Store in the composition root and the window lifecycle
**Files:**
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/UiServiceCollectionExtensions.cs`, `App.axaml.cs`, `Program.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/Di/CompositionRootTests.cs`, `AppLifecycleTests.cs`

- [ ] `AddUi(LoggingSetup, AppDataPaths)` registers `StoreFile` from `StoreFiles.InstanceFile(paths.StoreFolder,
  Environment.ProcessId)` and `SpectrumStore`, both singletons; `Program.Main` passes its paths (`Program.cs:40-42`)
- [ ] add the store to `ConfiguredServices` (`App.axaml.cs:197-202`) and `Configure` (`:169-177`); start it in
  `Opened` before the distributor (`:70-74`)
- [ ] rename `StopLinkThenDistributorAsync` (`:132-145`) to `StopLinkDistributorThenStoreAsync`: link, distributor in
  a `finally`, store in a `finally` of that; the limit stays 5 s (`:27`); every log line names the three services
- [ ] the container tests pass `AppDataPaths.Under(<temp root>)` to `AddUi` (`CompositionRootTests.cs:51`, `:166`,
  `AppLifecycleTests.cs:242-247`), so no test writes under the real local app data. Every test in one process gets
  the same `spectra-<pid>.db` name, so each test has its own temp root (an instance field), and `Dispose` awaits
  `SpectrumStore.StopAsync()` before it deletes the root; otherwise a writer left open by one test would hold the
  file and the next test's `Prepare` could not wipe it
- [ ] write `OpeningTheWindowStartsTheStore`, `ClosingTheWindowStopsTheStoreAfterTheDistributor`,
  `AfterTheCloseTheStoreFileHasNoWal` and the singleton check of Acceptance Evidence item 4
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 11: Status bar pre-handover checklist (`dev:desktop-ui`)
**Files:**
- Modify: `SpectrumVisualizer/SpectrumVisualizer.UI/MainWindow/MainWindow.axaml.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/MainWindow/MainWindowTextFitTests.cs`

- [ ] every gap reads off the spacing table: the new separator reuses `Border.separator` (`AppStatusBar.axaml:22-26`,
  8 px each side, 1 px line); no new inset; the `ui-theme.md:86` inset grep prints nothing
- [ ] every width reads off the width table or carries a reason: the segment is text with no fixed width; the widest
  value is the six-digit count of `MainWindowTextFitTests.cs:36-37`, which bounds the dropped count of a day too
- [ ] measure through `RenderedText` in English and Russian: add the dropped segment with `999999` to the status sum
  (`MainWindowTextFitTests.cs:73-78`); record both widths here. ASSUMPTION: the Russian bar grows from 1042.4 px by
  about 190 px (one separator, 17 px, plus "Отброшено кадров: 999999")
- [ ] set `MainWindow.MinimumWidth` (`MainWindow.axaml.cs:12`) to the larger of the measured status bar and the
  792 px content floor (`overview.md:139-143`), rounded up to 8; record the value. It must stay at or under the
  1280 px opening width (`MainWindow.axaml:15`); if the Russian bar passes 1272 px, the texts become "Dropped: {0}" /
  "Отброшено: {0}" and the measurement repeats
- [ ] the window at its minimum width shows every status segment whole, and at 2560 px the bar stays left-aligned with
  no growing gap (`MainWindowLayoutTests`); every status text is `NoWrap` and fits in both languages; no store or link
  state changes the set of segments or the window size (the segment shows a number in every state); the
  `ui-theme.md:99` colour grep prints nothing
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 12: Verify acceptance criteria
- [ ] run every command of Acceptance Evidence and record each result with its count (per Area, full suite, none
  skipped; Release build 0 warnings; format and terse exit 0; old solution 13 of 13; both greps print nothing)
- [ ] confirm Acceptance Evidence item 8 holds measured numbers for every row, the kept strategies and the decision,
  with no ASSUMPTION label left on a measured row
- [ ] verify every Overview item maps to a named passing test: every frame stored, the file per instance and the wipe,
  the sweep, data across reconnects, retention, WAL settings, the enqueue-only subscriber, both drop counts in one
  segment, lost spectra separate, spectrum at a moment, the series with uncovered gaps, the stored range, the lifecycle
- [ ] verify edge cases: NaN floats, equal stamps, a wavelength neither device covers (every column uncovered), a
  `delta` of 0, a series range with no rows (every column NaN, `Count` 0), a failed open (history off, viewer works),
  a failed batch counted once per frame, a stop with a full queue inside the 5 s limit
- [ ] run the full suite: `dotnet test SpectrumVisualizer.slnx`; confirm the old solution builds and passes 13 of 13
- [ ] start the app with `dotnet run --project SpectrumVisualizer/SpectrumVisualizer.UI`, confirm the instance file
  and its log line, close it and confirm the `-wal` file is absent or 0 bytes; leave the port-pair checklist to the owner

### Task 13: [Final] Update documentation
- [ ] `docs/architecture/overview.md`: the purpose paragraph (`:5-12`) says frames are stored; the Stack list
  (`:14-27`) gains Microsoft.Data.Sqlite 10.0.14; the components table (`:29-75`) gains `StoreFiles`,
  `SpectrumDatabase`, `SpectrumIntensity`, `IntensityBuckets`, `SpectrumHistory`, `SpectrumStore`, the store errors,
  `ReceivedFrame` under Core, `AppDataPaths` with the store folder, `AddUi` with the paths; startup and close
  (`:103-122`) name the store; the status bar table (`:191-196`) gains Dropped frames; the minimum width paragraph
  (`:139-147`) carries the new measurement; a "Spectrum store" section: files and sweep, schema, pragmas, writer,
  retention, drop counts, queries, the read strategy kept and its measured numbers without dates
- [ ] `docs/architecture/board-protocol.md`: "Frames out" (`:283-289`) says the link counts channel drops into
  `LinkStatus.DroppedFrames`; "Status" (`:257-263`) names the new member
- [ ] `docs/architecture/README.md`: the Spectrum storage row (`:40`) names the file per process id, the sweep, WAL with
  `synchronous=NORMAL` and the 64 MB journal limit, the single writer and the kept read strategy; the Tools row (`:39`)
  names both tools
- [ ] `docs/architecture/testing-strategy.md`: the Area list (`:22-24`) gains `Store`; a Store row (temp files, the
  second-connection lock, the manual `TimeProvider`, the benchmark's small run); the Link, View model, Composition root
  and App lifecycle rows gain the drop counter, the segment and the store
- [ ] `docs/architecture/ui-text.md`: the key owners gain the status bar's dropped segment and the store errors
- [ ] `CLAUDE.md`: add `Store` to the Area list (`:41`); the Layout block names `Core/Store`, `UI/Store` and
  `SpectrumVisualizer.Tools.StoreBenchmark`; the Build block gains the benchmark command

## Post-Completion
**Manual verification:**
- the smoke checklist of Acceptance Evidence over a com0com pair, during the owner's final assembly check.
- the benchmark on the operator PC (`--rows 172800`), because its disk decides the 24 h series time; a result above
  the targets goes to intensity-trend as an input for how much history it reads at once.
- the real board after the firmware-frame-sender slice: steps 2 to 8 of the checklist against the board.

**Remaining slices:**
- intensity-trend: the intensity-over-time chart at the SCADA wavelength with SemiPlot trend behavior over this
  slice's queries, the lost-frame and failure markers from `seq`, `state` and the heartbeat table, and a click that
  shows a past spectrum through a historical displayed-spectrum source.
- firmware-frame-sender: the OptoskyProj#20 frame sender in the board firmware, verified on the board against the app.
- release-3-0: the 3.0.0 installer over the self-contained publish folder, the release workflow, and removal of the
  old app.
