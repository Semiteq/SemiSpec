# One running copy

## Overview
- Run one copy of SemiSpec per Windows session. A second launch asks the first copy to restore and activate its
  main window, then exits without a window, a port or a store.
- A first copy that does not answer within 2 s, after one retry, gets a message box in the second copy: "SemiSpec
  уже запущена, но не отвечает. Закройте её в диспетчере задач и запустите снова" (resx, en and ru); the second copy
  exits. The second copy logs one line.
- A copy whose close has started stops answering and gives up the claim, so a relaunch during the close starts a
  new copy.
- Why one copy is enough: the board has one data source, one COM port, and the history file of
  `20261106-persistent-history.md` takes one writer.
- Scope guard: no UI layout change, no new setting, no change to the store, the link or the trend.

## Context (from discovery)
- Verified 2026-11-06 at `261335e` (master): `dotnet test SemiSpec.slnx` passes 944 of 944.
- Base count, 2026-11-07 at `1fb7b06` (master `2e2714b` plus this plan): `dotnet test SemiSpec.slnx` passes 1045 of
  1045, none skipped.
- This plan executes on master after `ui-redesign` (tip `84b9e06`) and then `20261106-persistent-history.md` merge.
  Expected conflict points:
  - `SemiSpec.UI/Program.cs`: on `ui-redesign` the logger comes from `AppLogging.Create(AppLogging.SiteLogFolder,
    paths.Value.FallbackLogFolder, AppLogging.CanWrite)` and the start line logs `logging.Value.Folder`
    (`git diff master...ui-redesign -- SemiSpec/SemiSpec.UI/Program.cs`). The claim goes after whatever line creates
    the logger.
  - Both resx files and `docs/architecture/overview.md` change on both earlier branches; this plan cites
    `overview.md` by section name only. Code line numbers below are read at `261335e`; re-read them at the base.
- Startup: `Program.Main` resolves the paths, creates the logger, logs "SemiSpec starting", builds the container and
  runs the app, returning 0 or 1 (`Program.cs:14`, `:19-58`). The file sink is opened with `shared: true`, so two
  processes write one log file (`AppLogging.cs:251-259`).
- `App.Run` builds the app; `AfterSetup` subscribes `Dispatcher.UIThread.UnhandledException`, which exists only
  once the platform is set up, then calls `Configure` (`App.axaml.cs:181-198`).
- Close: the first `Closing` starts `StopBackgroundServicesAsync`, cancels the close, awaits the stop and closes
  again; a later `Closing` passes once the stop completes (`App.axaml.cs:92-121`). The stop has one 5 s limit
  (`:29`, `:124-144`).
- Link: a session reopens the port every second after a failed open or a loss (`LinkSession.cs:26`, `:77-103`); a
  port another process holds maps to `PortBusyError` (`RjcpSerialPortProvider.cs:70-71`), shown through
  `ErrorMessageMapper.cs:21-25`.
- Interop: the repo has no `DllImport` or `LibraryImport` yet (`git grep`). Build gates: `TreatWarningsAsErrors`,
  `AnalysisMode=Recommended`, `EnforceCodeStyleInBuild` (`SemiSpec/Directory.Build.props`); the UI project sets no
  `AllowUnsafeBlocks` (`SemiSpec.UI.csproj`).
- Tests: `CultureScope` switches the resx culture in tests (`Tests.Unit/UI/CultureScope.cs`); resx parity is pinned by
  `AppResourcesTests` (`AppResourcesTests.cs:41-62`). No test calls `App.Run`; `AppBuilderCompositionTests` calls
  `App.BuildAvaloniaApp`, whose signature stays (`AppBuilderCompositionTests.cs:30`).

## Development Approach
- **testing approach**: Regular (code first, then tests in the same task).
- complete each task fully before moving to the next; small, focused changes.
- every task includes new or updated tests for its code; all tests pass before the next task.
- update this plan when scope changes during implementation.

## Testing Strategy
- **unit tests** in `SemiSpec.Tests.Unit`, Area `Instance` (new), `Component=UI`. Every test claims a fresh name
  (`Guid.NewGuid()`), so tests run in parallel and leave nothing behind. The claim reads `createdNew` from the
  `Mutex` constructor and never waits on the mutex, so thread affinity and re-entrance play no part, and two claims
  on one test thread behave as two processes.
- The primary's UI scheduler is a test double: an `EventLoopScheduler` that runs, or a `TestScheduler` never
  advanced to stand for a hung UI thread. Limits are parameters, so no-answer tests wait 200 ms, not 2 s.
- The window activation and the P/Invokes run in the manual smoke checklist only: headless Avalonia has no Win32
  handle.

## Acceptance Evidence
Automatable (baseline: the count at the base commit, recorded in Task 1):
1. `dotnet test SemiSpec.slnx --filter "Area=Instance"` passes:
   - `SingleInstanceTests`: `TheFirstClaimOfANameIsPrimary`; `ASecondClaimOfAHeldNameIsSecondary`;
     `AClaimAfterThePrimaryReleasedIsPrimary`; `AReleasedPrimaryNoLongerAnswers` (release, then an ask times out
     and runs no activation); `ReleaseTwiceIsANoOp`; `ASignalRunsTheActivationOnThePrimarySchedulerAndIsAnswered`
     (answered under 2 s, activation ran once); `APrimaryWhoseSchedulerNeverRunsIsNoAnswer` (a `TestScheduler`
     never advanced, a 200 ms limit: no answer, no activation); `AStaleAnswerDoesNotAnswerTheNextSignal` (the answer
     event set beforehand, the scheduler never advanced: still no answer); `AnActivationThatThrowsIsStillAnswered`.
   - `SecondLaunchTests`: `AnAnsweredSignalExitsWithZeroLogsOneLineAndShowsNothing`;
     `NoAnswerAfterTheRetryShowsTheNotRespondingTextAndExitsWithOne` (two asks made, the text equals the resx value
     under `en` and under `ru` through `CultureScope`); `AnAnswerToTheRetryExitsWithZero` (the first ask
     unanswered, the second answered); `TwoSecondLaunchesAtOnceBothGetAnAnswer` (two threads run `SecondLaunch.Run`
     against one running primary at the same moment: both exit 0, no message shown); each case logs exactly one
     line.
2. `dotnet test SemiSpec.slnx --filter "Area=Resources"` passes with `SingleInstanceNotResponding` in both sets.
3. `dotnet test SemiSpec.slnx` passes every test, none skipped; Task 4 records the count.
4. `dotnet build SemiSpec.slnx -c Release` gives 0 warnings, which shows the `DllImport` declarations pass the
   analyzers; `dotnet format SemiSpec.slnx --verify-no-changes` and `dotnet terse` over every touched `.cs` file
   exit 0.

Manual smoke checklist (com0com pair `COM10`-`COM11`, emulator on `COM10`, app on `COM11`; `emu` stands for
`dotnet run --project SemiSpec/SemiSpec.Tools.BoardEmulator --`; the installed or published `SemiSpec.exe`):
1. Start SemiSpec, minimise it, launch a second copy: the first window restores and comes to the front; no second
   window opens; the log has one line with the second process id.
2. Maximise the first window, put another window over it, launch a second copy: it comes to the front maximised.
   If the taskbar button flashes instead, record it against the `AllowSetForegroundWindow` ASSUMPTION.
3. Launch three copies within one second (select `SemiSpec.exe` three times in Explorer and press Enter, or
   `1..3 | % { Start-Process SemiSpec.exe }`): one window, no message box; the log has two second-copy lines.
4. Suspend the first copy in Resource Monitor (CPU tab, right-click, Suspend Process), launch a second copy: after
   about 4 s (2 s and one retry) a message box shows the not-responding text in the Windows UI language; the
   second process exits with code 1. Resume the first copy.
5. With `emu --port COM10 --rate 1` running, Connect, close the window and launch SemiSpec again within 2 s: a new
   window appears. Press Connect in it: the status shows the port unavailable until the closing copy releases
   `COM11`, then it receives; the log of the new copy shows the reopen attempts one second apart.
6. Note the log timestamps of "SemiSpec starting" and of the first listener line in step 1: the difference is the
   margin behind the 2 s ASSUMPTION on the start-up.

## Progress Tracking
- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update plan if implementation deviates from original scope

## Solution Overview
- **Claim in `Program.Main`.** The claim runs after the logger exists, so a second copy can log, and before
  `AddUi`, so a second copy builds no container, opens no window, no port and no store. The claim is a named `Mutex`
  in the `Local\` namespace, `new Mutex(false, name, out createdNew)`: `createdNew` true makes the primary, false a
  secondary. The OS destroys the object when the last handle closes, also after a crash, so a dead copy never
  blocks a start. A claim that throws (`UnauthorizedAccessException`, `WaitHandleCannotBeOpenedException`) logs a
  Warning and runs as primary: a lost guard is better than an app that cannot start.
- **`Local\`, not `Global\`.** `Local\` is the session namespace. A copy in another Windows session has its own
  desktop, where the first copy could not show its window, and a `Global\` name would make that launch wait and show
  a false "not responding". Such a copy opening the port the first copy holds meets the existing `PortBusyError`
  route. ASSUMPTION: one user runs one interactive session at a time on the operator PC, so one history file has
  one writer.
- **Activation by two named events.** `Local\<name>-activate` (auto-reset) asks, `Local\<name>-activated`
  (auto-reset) answers. Both copies create-or-open both events in the claim, so their creation order does not
  matter. The primary waits with `ThreadPool.RegisterWaitForSingleObject` on the ask; the callback schedules the
  activation on the UI scheduler and sets the answer after the activation ran there, so the answer proves the UI
  thread is alive. A secondary resets the answer, sets the ask, and waits 2 s on the answer; the reset means a late
  answer to an earlier launch cannot answer this one. Two events need no thread of their own, no protocol and no
  serialisation, and the payload is one bit each way; a named pipe would need a server loop and a message format
  for the same bit.
- **One retry for concurrent launches.** Two secondaries at once can lose an answer: their two asks may coalesce
  into one signal of the auto-reset ask event, one secondary's reset can clear the other's answer, and one answer
  releases one waiter. So a secondary without an answer asks once more and waits 2 s again before it shows the
  message box. A hung primary therefore shows the message after about 4 s. More secondaries at the same instant
  than a retry absorbs is not a case an operator produces; smoke step 3 checks three.
- **Listening starts in `AfterSetup`.** `Dispatcher.UIThread` exists only there, so `App.Run` takes the claim and
  starts `Listen` after `Configure`; this subscription lives until the close begins, which the release below
  declares in code. An ask that arrives earlier stays set and runs once the listener starts. ASSUMPTION: the primary
  reaches `AfterSetup` within 2 s of its launch on the operator PC, so only a second launch inside that time can
  see a false "not responding"; smoke step 6 measures the margin.
- **Release when the close starts.** The first `Closing` that starts `StopBackgroundServicesAsync` calls
  `instance.Release()` before it starts the stop: the wait registration is unregistered and the events and the mutex
  handle are disposed. From then on a launch claims the name as a new primary instead of activating a window that
  is about to close. The closing copy may hold `COM11` for up to the 5 s stop limit; Connect in the new copy fails
  with `PortBusyError` and the link reopens the port every second until the old copy releases it. The two copies
  also share `spectra.db` for those seconds: SQLite serialises their writes, and the old copy's closing checkpoint
  may report Busy, which it already logs. `Release` is idempotent; `Main` disposes the claim again at exit.
- **Activation.** On the UI thread: when the main window exists, `ShowWindow(handle, SW_RESTORE)` on its Win32
  handle (`TryGetPlatformHandle()`), then `Activate()`. `SW_RESTORE` brings a minimised window back to its state
  before the minimise, maximised included, which setting `WindowState = Normal` would lose. Before it asks, a
  secondary calls `AllowSetForegroundWindow(ASFW_ANY)`: Windows lets only the foreground process hand the
  foreground on, and the just-launched copy is that process. ASSUMPTION: this lets `Activate()` bring the window to
  the front instead of flashing its taskbar button; smoke step 2 checks it. An activation that throws is logged and
  still answered: the UI thread ran it.
- **Second launch.** `SecondLaunch.Run` takes the claim, the 2 s limit, a `showMessage` action and a logger.
  Answered on the first or the retry ask: one Information line "SemiSpec already runs; its window was brought to
  the front", exit code 0. No answer: one Warning line "SemiSpec already runs but did not answer within 2 s twice",
  the message box, exit code 1. Program wires `showMessage` to `MessageBoxW` (no owner window, caption "SemiSpec",
  `MB_OK | MB_ICONWARNING`), since no Avalonia window may open. Text key `SingleInstanceNotResponding`: en "SemiSpec
  is already running but does not respond. Close it in Task Manager and start it again."; ru "SemiSpec уже запущена,
  но не отвечает. Закройте её в диспетчере задач и запустите снова".
- **Classic `DllImport`.** The three Win32 calls (`MessageBoxW`, `AllowSetForegroundWindow`, `ShowWindow`) are the
  repo's first P/Invokes and sit in one `internal static class NativeMethods` under `UI/Instance/`. They use
  `[DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]` with
  `[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]`. `LibraryImport` with a string parameter generates
  unsafe code and needs `AllowUnsafeBlocks` in the project (`SYSLIB1062` otherwise); three calls made once per
  launch gain nothing from source-generated marshalling, so the project keeps unsafe code off. `SYSLIB1054`
  ("use `LibraryImport`") is an info-level suggestion and does not fail the build; the explicit `CharSet` and
  search path answer the string-marshalling and DLL-hijacking analyzer rules. Acceptance item 4's 0-warning Release
  build confirms the declarations pass `AnalysisMode=Recommended`.

## Technical Details
Start-up order after this plan:

| Step | Where | What |
| --- | --- | --- |
| 1 | `Program.Main` | paths, logger |
| 2 | `Program.Main` | `SingleInstance.Claim(ProductionName)`; secondary: `SecondLaunch.Run`, return its exit code |
| 3 | `Program.Main` | "SemiSpec starting", container, `App.Run(services, logger, instance)` |
| 4 | `App.Run` `AfterSetup` | dispatcher subscription, `Configure`, `instance.Listen(uiScheduler, activate, logger)` |
| 5 | first accepted `Closing` | `instance.Release()`, then the stop chain |

Members:
- `SingleInstance` (`UI/Instance/`): `static SingleInstance Claim(string name, ILogger)`, `bool IsPrimary`,
  `void Listen(IScheduler, Action activate, ILogger)`, `bool AskPrimary(TimeSpan limit)`, `void Release()`,
  `Dispose()` (calls `Release`); `ProductionName` is a fixed string with a GUID suffix.
- `SecondLaunch` (`UI/Instance/`): `static int Run(SingleInstance, TimeSpan limit, Action<string> showMessage,
  ILogger)`; `AnswerLimit` is 2 s; two asks at most.
- `NativeMethods` (`UI/Instance/`): `MessageBoxW`, `AllowSetForegroundWindow`, `ShowWindow`.
- `App`: `Run(IServiceProvider, ILogger, SingleInstance)`; `ActivateMainWindow()`; the release in
  `OnMainWindowClosing`.

## What Goes Where
- **Implementation Steps**: the claim, the events, the second launch, the P/Invokes, the activation, the release,
  the resx key, tests and docs in this repo.
- **Post-Completion**: the smoke run on the operator PC.

## Implementation Steps

### Task 1: Claim, ask and answer
**Files:**
- Create: `SemiSpec/SemiSpec.UI/Instance/SingleInstance.cs`
- Create: `SemiSpec/SemiSpec.Tests.Unit/UI/Instance/SingleInstanceTests.cs`

- [x] record the test count of `dotnet test SemiSpec.slnx` at the base commit in Context
- [x] `SingleInstance`: claim, listen, ask with the answer reset, release, dispose per Solution Overview
- [x] write `SingleInstanceTests` of Acceptance Evidence item 1
- [x] run `dotnet test SemiSpec.slnx` - must pass before next task

### Task 2: Second launch and its message
**Files:**
- Create: `SemiSpec/SemiSpec.UI/Instance/SecondLaunch.cs`, `NativeMethods.cs`
- Modify: `SemiSpec/SemiSpec.UI/Program.cs`
- Modify: `SemiSpec/SemiSpec.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Create: `SemiSpec/SemiSpec.Tests.Unit/UI/Instance/SecondLaunchTests.cs`

- [x] `SecondLaunch.Run` with one retry; the `SingleInstanceNotResponding` key in both sets
- [x] `NativeMethods` with the three `DllImport` declarations
- [x] `Program.Main` claims after the logger; a secondary calls `AllowSetForegroundWindow(ASFW_ANY)` and returns
  `SecondLaunch.Run`'s exit code before "SemiSpec starting"
- [x] write `SecondLaunchTests` of Acceptance Evidence item 1, the concurrent case included
- [x] run `dotnet test SemiSpec.slnx` - must pass before next task

### Task 3: Activation and release in the app
**Files:**
- Modify: `SemiSpec/SemiSpec.UI/App.axaml.cs`, `SemiSpec/SemiSpec.UI/Program.cs`
- Modify: `SemiSpec/SemiSpec.Tests.Unit/UI/Instance/SingleInstanceTests.cs`

- [x] `App.Run` takes the claim and starts `Listen` in `AfterSetup` with `ActivateMainWindow` on the UI scheduler
- [x] `OnMainWindowClosing` releases the claim before it starts the stop
- [x] write `AReleasedPrimaryNoLongerAnswers` and `ReleaseTwiceIsANoOp` if Task 1 did not cover them (covered in
  Task 1)
- [x] run `dotnet test SemiSpec.slnx` - must pass before next task

### Task 4: Verify acceptance criteria
- [x] verify every Overview item and the scope guard (`git diff` touches no `.axaml` file and nothing under
  `Store/`, `Link/` or `Trend/`): `git diff --stat 2e2714b..HEAD` lists only `Instance/`, `App.axaml.cs`,
  `Program.cs`, both resx files, `Di/` test helpers and this plan; a secondary returns before `AddUi`
- [x] run Acceptance Evidence items 1 to 4; record the final count (2026-11-07):
  1. `--filter "Area=Instance"`: 17 of 17 pass (`SingleInstanceTests` 10, `SecondLaunchTests` 5,
     `AppInstanceTests` 2)
  2. `--filter "Area=Resources"`: 9 of 9 pass; the parity test covers `SingleInstanceNotResponding`
  3. `dotnet test SemiSpec.slnx`: 1062 of 1062 pass, none skipped (base 1045)
  4. `dotnet build SemiSpec.slnx -c Release --no-incremental`: 0 warnings; `dotnet format --verify-no-changes` and
     `dotnet terse` over every touched `.cs` file exit 0
- [x] verify edge cases: a claim that throws runs as primary with one Warning
  (`AClaimTheOsRefusesRunsAsAPrimaryWithOneWarning`, added here); a second launch during the primary's close starts
  a new copy (`AppInstanceTests.ALaunchDuringTheCloseClaimsTheNameAsANewPrimary`)
- [x] manual smoke checklist steps 1 to 6 (manual, for the owner: Win32 activation, the foreground rule and the
  start-up margin need the operator PC)

### Task 5: [Final] Update documentation
- [ ] `docs/architecture/overview.md`, Startup: the claim, the second launch, the listener in `AfterSetup`, the
  release at the close; Components: a row for `SingleInstance` and `SecondLaunch`
- [ ] `docs/architecture/README.md`, Locked decisions, the Instances row: one copy per Windows session, a second
  launch activates the first, a closing copy releases the claim
- [ ] `docs/architecture/ui-text.md`, The keys: a row for `SingleInstanceNotResponding`
- [ ] `CLAUDE.md`, Test, and `docs/architecture/testing-strategy.md`: the Area `Instance`
- [ ] `readme.md`: change it only if a sentence becomes false; none names running two copies today
- [ ] move this plan to `docs/plans/completed/`

## Post-Completion
*Items requiring manual intervention or external systems - no checkboxes, informational only*

**Manual verification**:
- the smoke checklist of Acceptance Evidence on the operator PC, steps 2 and 6 in particular, since the foreground
  rule and the start-up time depend on that machine.
