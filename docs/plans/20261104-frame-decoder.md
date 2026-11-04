# Frame decoder

## Overview
- Pure Core code for the board data: the frame decoder for the board-to-PC protocol of Semiteq/OptoskyProj#20, the
  frame model, `seq` gap tracking, the CRC, the frame encoder, and the per-spectrometer wavelength calibration.
- A console tool, `SpectrumVisualizer.Tools.BoardEmulator`, writes protocol frames to a COM port for manual runs over
  crossed USB-UART adapters or com0com.
- No UI and no serial port code in Core; the serial-link slice wires a port to this decoder.
- Scope guard: no serial port in Core, no UI, no storage, no change to the old app.

## Context (from discovery)
- Roadmap: docs/plans/roadmaps/20261104-avalonia-rewrite-roadmap.md, slice frame-decoder (a local planning file, kept
  out of the repository)
- Stacking base: local branch `avalonia-skeleton` at `6769d23`; this slice works on the local branch `frame-decoder`
  created from it. Local commits only.
- The protocol specification is issue Semiteq/OptoskyProj#20 (Russian). Its rules, restated in Technical Details, are
  the contract; this plan does not change them.
- Calibration coefficients: `SpectrumVisualizer.Uart/SpectrumJobs/SpectrumCalc.cs:6` (ATP-2000:
  185.89856, 0.390762, 1.126E-5, -8.969E-10) and `:7` (ATP-8000: 895.373169, 3.00962138, 9.568E-5, 4.519E-8), constant
  term first.
- Firmware reference for the wavelength-to-pixel mapping: OptoskyProj `origin/master` (`df5201f`),
  `OptoskyEncoder/OptoskyEncoder/main.c:166` (`PixelOrder_8000`, index = nm - 898, 1564 entries), `:169`
  (`PixelOrder_2000`, index = nm - 189, 840 entries, regenerated from the ATP-2000 polynomial by nearest pixel), and
  `PixelForWave()` at `:403`, which clamps the wavelength to the table range.
- Firmware CRC reference: `main.c:370` `crc16()` (CRC-16/MODBUS, init 0xFFFF, reflected poly 0xA001, no final XOR).
- The skeleton's Core holds only `AppDataPaths.cs`; `SpectrumVisualizer.Core.csproj` references FluentResults only.
  `System.IO.Pipelines` and `System.Threading.Channels` are part of the net10.0 shared framework, so they need no pin.
- Serial library for the emulator: `RJCP.SerialPortStream` 3.0.5 (Locked decision in `docs/architecture/README.md`).
- Tool convention: SemiPlot's `SemiPlot.Tools.ArchiveSeeder` uses `System.CommandLine` 2.0.2 (pinned in SemiPlot's
  `Directory.Packages.props:37`).

## Development Approach
- **testing approach**: TDD for the decoder, CRC, seq tracker and calibration (pure functions with an exact spec);
  regular for the emulator.
- complete each task fully before moving to the next; every task includes its tests; all tests pass before the next.
- update this plan when scope changes.

## Testing Strategy
- **unit tests** in `SpectrumVisualizer.Tests.Unit`, plain `[Fact]`/`[Theory]` (no ReactiveUI, no Avalonia), traits
  `Component` (`Core` or `Tools`), `Area` (`Protocol`, `Calibration` or `Emulator`), `Category=Unit`.
- `FrameDecoderTests` decodes every frame form whole and split at every byte boundary into a two-segment
  `ReadOnlySequence`; `FrameReaderTests` writes frames through a `System.IO.Pipelines.Pipe` in random chunk sizes from
  a fixed seed.

## Acceptance Evidence
Automatable:
1. `dotnet test SpectrumVisualizer.slnx --filter "Area=Protocol"` passes, including:
   - the CRC check value: ASCII `123456789` gives `0x4B37`;
   - the heartbeat `AA 55 02 00 03 2A 80 B3` decodes to state 0x03, seq 42, no measurement;
   - an encoded ATP-2000 frame (4120 bytes) and ATP-8000 frame (1048 bytes) round-trip through encoder and decoder,
     whole, split at every byte boundary, and in random chunks;
   - a false `AA 55` inside pixel data never hides the real next frame;
   - invalid `len`, bad CRC and device/len mismatch each drop one byte and resynchronise;
   - `seq` rules: the first frame seen is the baseline (0 lost, also when it is 57), difference 1 next, k > 1 lost
     k - 1, 0 on a spectrum frame lost 256, wrap 255 to 0, heartbeat repeating the last spectrum's seq, heartbeat with a
     different seq reporting a loss and the following spectrum not reporting it again, `Reset()` starting a new
     baseline;
   - two whole frames in one `Pipe` write without completing the writer are both yielded;
   - when the writer completes, the frames behind a stalled false candidate are still yielded.
2. `dotnet test SpectrumVisualizer.slnx --filter "Area=Calibration"` passes: the ATP-2000 wavelength-to-pixel mapping
   equals the firmware `PixelOrder_2000` table in all 840 entries; the ATP-8000 mapping is within 1 pixel of
   `PixelOrder_8000` in all 1564 entries; clamping and the inclusive ±delta window follow `PixelForWave()`.
3. `dotnet build SpectrumVisualizer.slnx -c Release` 0 warnings; `dotnet format SpectrumVisualizer.slnx
   --verify-no-changes` and `dotnet terse SpectrumVisualizer` exit 0.
4. `dotnet run --project SpectrumVisualizer/SpectrumVisualizer.Tools.BoardEmulator -- --help` prints its options.

Manual smoke checklist (owner, with a port pair):
1. Run the emulator on one end of a crossed pair and a serial terminal on the other; frames arrive at the chosen rate,
   and a heartbeat arrives every 3 s with `--rate 0`.

## Progress Tracking
- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix

## Solution Overview
- `SpectrumVisualizer.Core/Protocol/`:
  - `Crc16Modbus`: static, one public `Compute` over a span; the per-byte step is private.
  - `BoardFrame` record: `BoardState State`, `byte Seq`, `SpectrumMeasurement? Measurement`. `BoardState` is a
    `[Flags]` enum (bits 0-3 as in #20). `SpectrumMeasurement` record: `Spectrometer Device`, `ushort WavelengthNm`,
    `byte Delta`, `float Amplitude`, `float Snr`, `float Q`, `ushort[] Pixels`; its equality compares `Pixels` element
    by element. `Spectrometer` enum (`Spectrometer.cs`): `Atp2000 = 1`, `Atp8000 = 2`, with the `PixelCount` extension
    property giving 2048 and 512.
  - `FrameLayout`: internal offsets and sizes of the frame, shared by the encoder and the decoder.
  - `FrameDecoder`: `bool TryReadFrame(ref ReadOnlySequence<byte> buffer, [NotNullWhen(true)] out BoardFrame? frame,
    out long discarded)` over a `SequenceReader<byte>`: finds `AA 55`, checks `len` against the three valid values,
    waits for a whole frame, checks the CRC, checks device against `len`; on any failure it consumes exactly one byte
    and keeps scanning inside the same call. `false` means only "a whole frame is not in the buffer yet"; `buffer` is
    then sliced past every dropped byte. Multi-byte fields are read with `BinaryPrimitives` (`ReadUInt16LittleEndian`,
    `ReadSingleLittleEndian`) from a copied span, because `SequenceReader` has no `ushort` or `float` overloads.
    Reserved state bits 4-7 are masked off, so `BoardState` carries only the four defined bits.
  - `FrameReader` wraps the decoder over a `PipeReader` as
    `IAsyncEnumerable<BoardFrame> ReadAllAsync(PipeReader, CancellationToken)`: it decodes every whole frame in the
    buffer, calls `AdvanceTo(consumed, buffer.End)` (marking the rest examined, so the next `ReadAsync` waits for new
    bytes instead of spinning), then yields the frames, so no pipe buffer is held across a `yield`. When the writer
    completes, no more bytes will come, so a pending candidate is invalid: the reader drops one byte at each pending
    candidate and decodes again until the buffer is empty, yields the frames this finds, and ends. Cancellation
    surfaces as `OperationCanceledException`. `DiscardedBytes` is a running count of every dropped byte, including the
    bytes dropped at completion, for the link status of the next slice.
  - `FrameEncoder`: builds a frame's bytes from a `BoardFrame`; used by the emulator and the tests.
    `SpectrumMeasurement`'s constructor rejects a pixel count that does not match its device, so the encoder needs no
    failure path.
  - `SequenceTracker`: `int Observe(BoardFrame)`, fed every frame, returns how many spectra were lost before it
    (spectrum frames) or since the last spectrum (heartbeats). The first frame it sees is the baseline and reports 0
    lost, so an app started mid-run reports no false loss. A spectrum step of 0 reports 256 lost (OptoskyProj#20). A
    heartbeat with a different `seq` reports `(seq - last) mod 256` lost and moves the baseline to its `seq`, so the
    next spectrum does not report the same gap again. `Reset()` starts a new baseline for a reconnect.
- `SpectrumVisualizer.Core/Calibration/`:
  - `SpectrometerCalibration` has one instance per device, reached through `For(Spectrometer)`. It holds the
    polynomial, the wavelength of each pixel (computed once, read through `WavelengthOf(int)`), the table range
    `FirstNm`..`LastNm` (189-1028 nm for ATP-2000, 898-2461 nm for ATP-8000), `PixelFor(ushort nm)` as nearest
    pixel with the firmware's clamping, and `Window(ushort nm, byte delta)` returning the inclusive `PixelWindow`
    `[PixelFor(max(nm - delta, 0)), PixelFor(nm + delta)]` clamped to `0…P-1` (delta in nm, OptoskyProj#7). The board's
    noise window is built the same way (`main.c:974-976`); the board's Amplitude window is ±delta in pixels
    (`main.c:970-973`), so app and board amplitudes differ on ATP-2000. OptoskyProj#7 tracks moving the board's window
    to nm. ATP-8000 pixel 0 (895.37 nm) lies below the table base, so no wavelength maps to it.
- `SpectrumVisualizer.Tools.BoardEmulator`: console exe, `System.CommandLine` 2.0.2, `RJCP.SerialPortStream` 3.0.5,
  115200 baud. `EmulatorCommand` holds the command line and every range rule; `EmulatorOptions` is the plain record it
  produces. Options: `--port` (required), `--device 2000|8000` (default 8000), `--rate` (spectrum frames per second, 0
  or at least 0.001, default 1; 0 sends heartbeats only), `--wavelength` (189-2460 nm, default 650 for ATP-2000 and
  1550 for ATP-8000), `--delta` (0-100 nm, default 10), `--bad-crc-every <n>` (0 never, default 0). Spectra are a
  synthetic Gaussian peak at the wavelength over a flat floor. `FrameSchedule` sends a heartbeat after 3 s with no
  frame sent and keeps `seq` per the rules; a late tick sends one spectrum and drops the backlog. A bad-CRC frame
  consumes its `seq`, and `--bad-crc-every` counts every frame sent, heartbeats included. A port that fails to open or
  to write prints one line and exits 1.
- Errors in Core are values: `FrameReader` never throws for malformed input; it discards bytes.

## Technical Details
Protocol (OptoskyProj#20), all multi-byte fields little-endian, floats IEEE 754 single:

| Offset | Size | Field |
|---|---|---|
| 0 | 2 | sync `AA 55` |
| 2 | 2 | `len`: bytes from `state` to the end of the measurement part; valid values 2, 4114 (ATP-2000), 1042 (ATP-8000) |
| 4 | 1 | `state`: bit 0 ATP-2000 connected, bit 1 ATP-8000 connected, bit 2 last ATP-2000 measurement failed, bit 3 last ATP-8000 measurement failed, bits 4-7 reserved (ignored) |
| 5 | 1 | `seq`: measurement counter |
| 6 | 16 + 2·P | measurement part, spectrum frames only: device u8 (1/2), wavelength u16 nm, delta u8, amplitude f32, snr f32, q f32, pixels u16 × P |
| 6 + N | 2 | CRC-16/MODBUS over `len`..end of measurement, low byte first |

Frame sizes: ATP-2000 4120 bytes, ATP-8000 1048 bytes, heartbeat 8 bytes. `seq`: the board increments before each
spectrum frame (first spectrum 1); a heartbeat repeats the last spectrum's value (0 before any); wrap 255 → 0. Frame
type is decided by `len` only.

## What Goes Where
- **Implementation Steps**: Core types, tests, the emulator project, docs.
- **Post-Completion**: the manual port-pair run.

## Implementation Steps

### Task 1: CRC-16/MODBUS
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.Core/Protocol/Crc16Modbus.cs`
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Core/Protocol/Crc16ModbusTests.cs`

- [x] confirm `System.IO.Pipelines.dll` is in `Microsoft.NETCore.App.Ref/10.*/ref/net10.0`; if not, pin the package (present in 10.0.8, 10.0.11, 10.0.13, 10.0.14; no pin)
- [x] write tests first: `123456789` → `0x4B37`; the heartbeat bytes `02 00 03 2A` → `0xB380`
- [x] implement `Crc16Modbus` (init 0xFFFF, reflected 0xA001, no final XOR) as one public `Compute` over a span
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 2: Frame model and encoder
**Files:**
- Create: `SpectrumVisualizer.Core/Protocol/BoardFrame.cs` (with `BoardState`, `SpectrumMeasurement`)
- ➕ Create: `SpectrumVisualizer.Core/Protocol/Spectrometer.cs` (`Spectrometer` and `PixelCount`, used by the frame
  model, calibration and the emulator)
- Create: `SpectrumVisualizer.Core/Protocol/FrameEncoder.cs`
- Create: `SpectrumVisualizer.Tests.Unit/Core/Protocol/FrameEncoderTests.cs`

- [x] write tests first: a heartbeat encodes to `AA 55 02 00 03 2A 80 B3`; an ATP-2000 frame is 4120 bytes and an
  ATP-8000 frame 1048 bytes with the fields at the offsets of Technical Details
- [x] implement the model types and `FrameEncoder`; `SpectrumMeasurement`'s constructor rejects a pixel count that does
  not match its device
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 3: Frame decoder
**Files:**
- Create: `SpectrumVisualizer.Core/Protocol/FrameDecoder.cs`, `FrameReader.cs`
- ➕ Create: `SpectrumVisualizer.Core/Protocol/FrameLayout.cs` (frame offsets shared by encoder and decoder)
- Create: `SpectrumVisualizer.Tests.Unit/Core/Protocol/FrameDecoderTests.cs`, `FrameReaderTests.cs`
- ➕ Create: `SpectrumVisualizer.Tests.Unit/Core/Protocol/ProtocolSamples.cs` (sample frames shared by the protocol tests)

- [x] write decoder tests first: heartbeat example; both spectrum forms round-trip (compare with `BeEquivalentTo`,
  because `Pixels` is an array); split at every byte boundary; random chunks from a fixed seed through a `Pipe`; false
  `AA 55` inside pixels; invalid `len`, bad CRC, device/len mismatch (patch the device byte and recompute the CRC) each
  consume one byte and resynchronise; reserved state bits are masked off; garbage before a frame is counted as
  discarded
- [x] implement `FrameDecoder.TryReadFrame` over `SequenceReader<byte>` with the semantics of Solution Overview
- [x] implement `FrameReader.ReadAllAsync(PipeReader, CancellationToken)`: decode every whole frame before reading
  again, `AdvanceTo(consumed, buffer.End)` when more data is needed, drain pending candidates on writer completion,
  cancellation as `OperationCanceledException`; keep the discarded-byte count
- [x] write reader tests: frames across many `Pipe` writes; two whole frames in one write with the writer still open are
  both yielded; completion ends the enumeration and counts a partial frame as discarded; completion yields the frames
  behind a stalled false candidate; a partial frame makes the next read wait for new bytes; cancellation throws
  `OperationCanceledException`; every async test has a timeout so a spinning reader fails instead of hanging
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 4: Sequence tracker
**Files:**
- Create: `SpectrumVisualizer.Core/Protocol/SequenceTracker.cs`
- Create: `SpectrumVisualizer.Tests.Unit/Core/Protocol/SequenceTrackerTests.cs`

- [x] write tests first for every `seq` rule in Acceptance Evidence item 1, including the baseline, the
  heartbeat-then-spectrum follow-up and `Reset()`
- [x] implement `SequenceTracker`
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 5: Wavelength calibration
**Files:**
- Create: `SpectrumVisualizer.Core/Calibration/SpectrometerCalibration.cs`
- Create: `SpectrumVisualizer.Tests.Unit/Core/Calibration/SpectrometerCalibrationTests.cs`
- Create: `SpectrumVisualizer.Tests.Unit/Core/Calibration/FirmwarePixelTables.cs` (the two firmware tables from OptoskyProj
  `df5201f` `main.c:166` and `:169`, as test data)

- [x] write tests first: ATP-2000 `PixelFor` equals `PixelOrder_2000` for all 840 entries; ATP-8000 within 1 pixel of
  `PixelOrder_8000` for all 1564; wavelengths below and above the table range clamp; `Window` is inclusive and clamped
  to `0…P-1`; pixel 0 of ATP-2000 is 185.89856 nm
- [x] implement `SpectrometerCalibration` with the coefficients from `SpectrumCalc.cs:6-7` and the table ranges
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 6: Board emulator tool
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tools.BoardEmulator/SpectrumVisualizer.Tools.BoardEmulator.csproj`,
  `Program.cs`, `EmulatorOptions.cs`, `SpectrumGenerator.cs`, `FrameSchedule.cs`
- ➕ Create: `SpectrumVisualizer.Tools.BoardEmulator/EmulatorCommand.cs` (the command line; `EmulatorOptions` is the
  plain record it produces)
- Create: `SpectrumVisualizer.Tests.Unit/Tools/SpectrumGeneratorTests.cs`, `FrameScheduleTests.cs`,
  ➕ `EmulatorCommandTests.cs`
- Modify: `SpectrumVisualizer.slnx`, `SpectrumVisualizer/Directory.Packages.props` (pin `RJCP.SerialPortStream` 3.0.5 and
  `System.CommandLine` 2.0.2)

- [x] create the console project referencing Core, with the options of Solution Overview
- [x] implement `SpectrumGenerator` (Gaussian peak at the wavelength, flat floor, pixel count per device) and
  `FrameSchedule` (which frame to send at a tick: spectrum at the rate, heartbeat after 3 s of silence, bad CRC every
  nth frame, `seq` per the rules)
- [x] write tests for both with a fake time source, and for the option parsing and ranges through
  `EmulatorCommand.Build`; the port write is not unit-tested
- [x] run `dotnet test SpectrumVisualizer.slnx` and `dotnet run --project SpectrumVisualizer/SpectrumVisualizer.Tools.BoardEmulator -- --help`

### Task 7: Docs
**Files:**
- Create: `docs/architecture/board-protocol.md`
- Modify: `docs/architecture/README.md`, `docs/architecture/overview.md`, `CLAUDE.md` (layout line for the tool)

- [x] write `board-protocol.md`: the frame (from OptoskyProj#20, in English), decoder rules, `seq` rules, calibration and
  the window (stating that the board's Amplitude window is in pixels and OptoskyProj#7 tracks moving it to nm),
  the emulator (the `--help` command plus what `--help` does not say), and the known stall: a corrupted header whose
  `len` reads as a valid value makes the decoder wait for that many bytes before the CRC can fail; present tense, no
  version numbers
- [x] update the index, the overview components table and the CLAUDE.md layout

### Task 8: Verify acceptance criteria
- [x] run every command of Acceptance Evidence and record the results (2026-11-04, at `c38786a`):
  `--filter "Area=Protocol"` 47/47 pass (every sub-item of item 1 has a named test); `--filter "Area=Calibration"`
  15/15 pass; `--filter "Area=Emulator"` 43/43 pass; `dotnet build SpectrumVisualizer.slnx -c Release` 0 warnings,
  0 errors; `dotnet format --verify-no-changes` exit 0; `dotnet terse SpectrumVisualizer` exit 0; emulator `--help`
  prints `--port`, `--device`, `--rate`, `--wavelength`, `--delta`, `--bad-crc-every` with their ranges and defaults,
  exit 0; full `dotnet test SpectrumVisualizer.slnx` 172/172 pass; manual port-pair smoke check left to the owner (not
  automatable)
- [x] confirm the old solution still builds and passes 13 tests: `dotnet build SpectrumVisualizer.sln` 0 errors, 15
  warnings (nullable CS86xx and CS0618 in `SpectrumVisualizer.Uart` and `SpectrumVisualizer.Tests`, which this slice does not touch:
  `git diff --stat 6769d23..HEAD` over the old projects is empty); `dotnet test SpectrumVisualizer.Tests` 13/13 pass

### Task 9: [Final] Update documentation
- [x] update `readme.md` with the emulator's run command (`--help` runs, exit 0; `--port COM5 --device 2000 --rate 0`
  parses and exits 1 with `Cannot open COM5: Port not found: COM5` on a machine with no COM ports)
- [x] update `CLAUDE.md` if new patterns appeared (no new pattern; added the emulator `--help` command to Build)
- [x] leave this plan in `docs/plans/` (left in place: archiving is delivery work)

## Post-Completion
**Manual verification:**
- the port-pair smoke check above, during the owner's final assembly check.

**Remaining slices:** serial-link, spectrum-view, spectrum-store, intensity-trend, firmware-frame-sender, release-3-0.

**Executed by exec:**
- branch: frame-decoder

Run note: 13 commits on `frame-decoder` (`43aa493`..`c38786a`), stacked on `avalonia-skeleton` at `6769d23`. Review
phase 1 found 1 major and 22 minor findings, all fixed in `fc3b7c2` in one fix iteration; its critical recheck was
clean. Phase 2 (smells) findings were fixed in `68b1a9c`. Phase 3 (codex) was skipped because codex is not installed.
Phase 3b (comments) cut two summaries in `12bce0c`. Phase 4 (critical) found one major: `FrameReader` dropped the
frames behind a stalled false candidate when the writer completed; `c38786a` fixed it. Final state: 172/172 tests in
`SpectrumVisualizer.slnx`, 13/13 in `SpectrumVisualizer.sln`, Release build 0 warnings, format and terse clean.

**Decisions made autonomously:**
- Work ran in place on the stacked branch `frame-decoder`, created from `avalonia-skeleton`; this plan entered the
  repository with the Task 1 commit, because the commit script requires the plan file.
- `Crc16Modbus` exposes only `Compute`; `Update` is private. The decoder copies a multi-segment frame with `ToArray`
  instead of folding the CRC over segments.
- `FrameLayout` (internal) holds the frame offsets for `FrameEncoder` and `FrameDecoder`; `ProtocolSamples` keeps its
  own `LengthOffset`/`CrcSize`, because Core grants no `InternalsVisibleTo`.
- `Spectrometer.PixelCount` is a C# 14 extension property; an undefined device value throws
  `ArgumentOutOfRangeException`, so it cannot build a `SpectrumMeasurement`.
- `SpectrumMeasurement` stays a record with `Equals`/`GetHashCode` over `Pixels`, so `BoardFrame` record equality works.
- `TryReadFrame` returns the frame as `[NotNullWhen(true)] out BoardFrame?`, because nullable is on.
- `FrameReader` collects a buffer's frames, calls `AdvanceTo`, then yields, so an early-disposed enumeration leaves
  the reader clean. At completion it counts leftover bytes as discarded and drains pending candidates in a loop
  (`DrainAfterCompletion`) instead of a final-mode flag on `TryReadFrame`.
- The decoder stall stays: OptoskyProj#20 rule 3 says to wait for the whole frame. `board-protocol.md` documents it,
  and the serial-link slice drops the decoder buffer when the 10 s no-valid-frame status fires.
- `SequenceTracker.Observe(BoardFrame)` returns `int`; a heartbeat with a different `seq` reports
  `(seq - last) mod 256` lost; a spectrum step of 0 reports 256, per the OptoskyProj#20 table.
- `SpectrometerCalibration` instances are reached through `For(Spectrometer)`; pixel wavelengths through
  `WavelengthOf(int)` (CA1819); the `above == 0` branch of `PixelFor` is deleted, because both table bases lie above
  pixel 0. ATP-8000 pixel 0 is unreachable, and `board-protocol.md` says so.
- A Python script generated `FirmwarePixelTables.cs` from OptoskyProj `df5201f` `main.c` and reflowed the ATP-2000
  block; the script stays out of the repository.
- The emulator hard-codes 115200 baud (old app `DeviceManager.cs:23`), no `--baud`; defaults: `--device 8000`,
  `--rate 1`, `--delta 10`, `--wavelength` 650/1550 nm per device.
- A bad-CRC frame consumes its `seq`, and `--bad-crc-every` counts heartbeats; a late tick drops the backlog.
- The generator uses sigma = max(delta, 1)/2 nm, floor 1000, peak 30000, `Amplitude` = peak, `Snr` = peak/floor,
  `Q` = 0.
- `FrameScheduleTests` fakes time with a small `TimeProvider` subclass instead of adding `FakeTimeProvider`.
- `--wavelength`, `--delta` and `--bad-crc-every` keep the custom `ParseWhole`: `System.CommandLine` 2.0.2 runs
  validators after a failed conversion, and `GetValueOrDefault` then throws `InvalidOperationException`.
- Parsing lives in `EmulatorCommand.Build`, tested by `EmulatorCommandTests`; `DefaultWavelength` stays on
  `EmulatorOptions`, as SemiPlot keeps `Default*` on `SeederOptions`.
- Tool tests carry `Component=Tools`, `Area=Emulator`, `Category=Unit`.
- `docs/architecture/README.md` gained a Tools row in Locked decisions and a reworded Serial library row;
  `board-protocol.md` documents `Reset()` as "for a reconnect" and points to `--help` for the options.
- The 15 old-solution warnings stay unfixed: the scope guard forbids changes to the old app.
- The readme run command is verified only up to port opening; this machine has no COM ports.
- Review-fix commits use the scope `protocol`, the main area they touch.

## Verify it yourself

Automated, from the repo root on branch `frame-decoder`:

1. `dotnet build SpectrumVisualizer.slnx -c Release` gives 0 warnings, 0 errors.
2. `dotnet test SpectrumVisualizer.slnx` passes 172 of 172. `--filter "Area=Protocol"` runs 47, `"Area=Calibration"`
   15, `"Area=Emulator"` 43.
3. `dotnet format SpectrumVisualizer.slnx --verify-no-changes` and `dotnet terse SpectrumVisualizer` both exit 0.
4. `dotnet build SpectrumVisualizer.sln` and `dotnet test SpectrumVisualizer.Tests` build the old app and pass 13 of 13.
5. `dotnet run --project SpectrumVisualizer/SpectrumVisualizer.Tools.BoardEmulator -- --help` lists `--port`,
   `--device`, `--rate`, `--wavelength`, `--delta`, `--bad-crc-every` with ranges and defaults.
6. The protocol contract is pinned by these test classes:
   - `Crc16ModbusTests`: check value `0x4B37`, heartbeat CRC `0xB380`.
   - `FrameEncoderTests`: the heartbeat example bytes, field offsets, frame sizes 4120 and 1048.
   - `FrameDecoderTests`: round-trip, split at every byte boundary, false `AA 55`, the one-byte resync rules.
   - `FrameReaderTests`: random chunks, no spin on a partial frame, discarded count,
     `WriterCompletionYieldsTheFramesBehindAStalledFalseCandidate`.
   - `SequenceTrackerTests`: every `seq` rule, including baseline, 256 lost and `Reset()`.
   - `SpectrometerCalibrationTests`: the firmware pixel tables, clamping, the window.
   - `FrameScheduleTests`, `EmulatorCommandTests`: emulator timing, `seq`, bad CRC, option ranges.

By hand (needs a com0com pair or two crossed USB-UART adapters, here `COM10` and `COM11`):

1. Open a serial terminal in hex mode on `COM11` at 115200 baud, 8N1.
2. Run `dotnet run --project SpectrumVisualizer/SpectrumVisualizer.Tools.BoardEmulator -- --port COM10 --device 2000`.
   Expect one 4120-byte frame per second, each starting `AA 55 12 10 01` (`len` 4114, state ATP-2000 connected), and
   `seq` at offset 5 counting up from 01.
3. Stop with Ctrl+C, run again with `--device 8000`. Expect 1048-byte frames starting `AA 55 12 04 02`.
4. Run with `--device 8000 --rate 0`. Expect the 8-byte heartbeat `AA 55 02 00 02 00 00 FC` every 3 s, the first one
   3 s after start; with `--device 2000` it is `AA 55 02 00 01 00 00 0C`.
5. Run with `--device 2000 --rate 0 --bad-crc-every 2`. Expect every second heartbeat to end in `F3` instead of `0C`:
   the emulator flips the last CRC byte, and `seq` stays `00`.
6. Run with `--delta 101` or `--rate 0.0001`. Expect a line naming the option and its range, the
   help text, and exit code 1.
