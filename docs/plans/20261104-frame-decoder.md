# Frame decoder

## Overview
- Pure Core code for the board data: the frame decoder for the board-to-PC protocol of Semiteq/OptoskyProj#20, the
  frame model, `seq` gap tracking, the CRC, the frame encoder, and the per-spectrometer wavelength calibration.
- A console tool, `SpectrumVisualizer.Tools.BoardEmulator`, writes protocol frames to a COM port for manual runs over
  crossed USB-UART adapters or com0com.
- No UI and no serial port code in Core; the serial-link slice wires a port to this decoder.
- Scope guard: no serial port in Core, no UI, no storage, no change to the old app.

## Context (from discovery)
- Roadmap: docs/plans/roadmaps/20261104-avalonia-rewrite-roadmap.md — slice frame-decoder (a local planning file, kept
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
  `Component`, `Area=Protocol` or `Area=Calibration`, `Category=Unit`.
- the decoder is tested through a `System.IO.Pipelines.Pipe`: every frame form written whole, split at every byte
  boundary, and in random chunk sizes from a fixed seed.

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
   - two whole frames in one `Pipe` write without completing the writer are both yielded.
2. `dotnet test SpectrumVisualizer.slnx --filter "Area=Calibration"` passes: the ATP-2000 wavelength-to-pixel mapping
   equals the firmware `PixelOrder_2000` table in all 840 entries; the ATP-8000 mapping is within 1 pixel of
   `PixelOrder_8000` in all 1564 entries; clamping and the inclusive ±delta window follow `PixelForWave()`.
3. `dotnet build SpectrumVisualizer.slnx -c Release` 0 warnings; `dotnet format SpectrumVisualizer.slnx
   --verify-no-changes` and `dotnet terse SpectrumVisualizer` exit 0.
4. `dotnet run --project SpectrumVisualizer/SpectrumVisualizer.Tools.BoardEmulator -- --help` prints its options.

Manual smoke checklist (owner, with a port pair):
1. Run the emulator on one end of a crossed pair and a terminal or the later app on the other; frames arrive at the
   chosen rate, and a heartbeat arrives every 3 s with `--rate 0`.

## Progress Tracking
- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix

## Solution Overview
- `SpectrumVisualizer.Core/Protocol/`:
  - `Crc16Modbus` — static, per-byte update and span overload.
  - `BoardFrame` record: `BoardState State`, `byte Seq`, `SpectrumMeasurement? Measurement`. `BoardState` is a
    `[Flags]` enum (bits 0-3 as in #20). `SpectrumMeasurement` record: `Spectrometer Device`, `ushort WavelengthNm`,
    `byte Delta`, `float Amplitude`, `float Snr`, `float Q`, `ushort[] Pixels`. `Spectrometer` enum: `Atp2000 = 1`,
    `Atp8000 = 2`, with pixel counts 2048 and 512.
  - `FrameDecoder` — `bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out BoardFrame frame, out long discarded)`
    over a `SequenceReader<byte>`: finds `AA 55`, checks `len` against the three valid values, waits for a whole frame,
    checks the CRC, checks device against `len`; on any failure it consumes exactly one byte and keeps scanning inside
    the same call. `false` means only "a whole frame is not in the buffer yet"; `buffer` is then sliced past every
    dropped byte. Multi-byte fields are read with `BinaryPrimitives` (`ReadUInt16LittleEndian`,
    `ReadSingleLittleEndian`) from a copied span, because `SequenceReader` has no `ushort` or `float` overloads.
    Reserved state bits 4-7 are masked off, so `BoardState` carries only the four defined bits.
  - `FrameReader` wraps the decoder over a `PipeReader` as
    `IAsyncEnumerable<BoardFrame> ReadAllAsync(PipeReader, CancellationToken)`: it yields every whole frame in the
    buffer before reading again, calls `AdvanceTo(consumed, buffer.End)` when the decoder needs more data (marking the
    rest examined, so the next `ReadAsync` waits for new bytes instead of spinning), ends quietly when the writer
    completes, and lets cancellation surface as `OperationCanceledException`. It keeps a running count of discarded
    bytes for the link status of the next slice.
  - `FrameEncoder` — builds a frame's bytes from a `BoardFrame`; used by the emulator and the tests.
    `SpectrumMeasurement`'s constructor rejects a pixel count that does not match its device, so the encoder needs no
    failure path.
  - `SequenceTracker` — fed every frame, returns how many spectra were lost before it (spectrum frames) or since the
    last spectrum (heartbeats). The first frame it sees is the baseline and reports 0 lost, so an app started mid-run
    reports no false loss. A heartbeat that reports a loss moves the baseline to its `seq`, so the next spectrum does
    not report the same gap again. `Reset()` starts a new baseline; the serial-link slice calls it on every reconnect.
- `SpectrumVisualizer.Core/Calibration/`:
  - `SpectrometerCalibration` — per device: the polynomial, the wavelength of each pixel (computed once), the table
    range (189-1028 nm for ATP-2000, 898-2461 nm for ATP-8000), `PixelFor(ushort nm)` as nearest pixel with the
    firmware's clamping, and `Window(ushort nm, byte delta)` returning the inclusive pixel range
    `[PixelFor(max(nm - delta, 0)), PixelFor(nm + delta)]` clamped to `0…P-1` (delta in nm, OptoskyProj#7). The board's
    noise window is built the same way (`main.c:974-976`); the board's Amplitude window is still ±delta in pixels
    (`main.c:970-973`) until the firmware change for #7 lands, so app and board amplitudes differ on ATP-2000 until then.
- `SpectrumVisualizer.Tools.BoardEmulator` — console exe, `System.CommandLine` 2.0.2, `RJCP.SerialPortStream` 3.0.5:
  `--port`, `--device 2000|8000`, `--rate <frames per second, 0 = heartbeats only>`, `--wavelength`, `--delta`,
  `--bad-crc-every <n>`. Spectra are a synthetic Gaussian peak at the wavelength over a flat floor. Heartbeat every 3 s
  when no frame was sent; `seq` per the rules.
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
- [x] write tests first: `123456789` → `0x4B37`; the heartbeat bytes `02 00 03 2A` → `0xB380`; incremental update equals
  the span result
- [x] implement `Crc16Modbus` (init 0xFFFF, reflected 0xA001, no final XOR) with a per-byte `Update` and a span overload
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 2: Frame model and encoder
**Files:**
- Create: `SpectrumVisualizer.Core/Protocol/BoardFrame.cs` (with `BoardState`, `SpectrumMeasurement`, `Spectrometer`)
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
- Create: `SpectrumVisualizer.Tests.Unit/Core/Protocol/FrameDecoderTests.cs`, `FrameReaderTests.cs`

- [x] write decoder tests first: heartbeat example; both spectrum forms round-trip (compare with `BeEquivalentTo`,
  because `Pixels` is an array); split at every byte boundary; random chunks from a fixed seed through a `Pipe`; false
  `AA 55` inside pixels; invalid `len`, bad CRC, device/len mismatch (patch the device byte and recompute the CRC) each
  consume one byte and resynchronise; reserved state bits are masked off; garbage before a frame is counted as
  discarded
- [x] implement `FrameDecoder.TryReadFrame` over `SequenceReader<byte>` with the semantics of Solution Overview
- [x] implement `FrameReader.ReadAllAsync(PipeReader, CancellationToken)`: yield every whole frame before reading again,
  `AdvanceTo(consumed, buffer.End)` when more data is needed, end on writer completion, cancellation as
  `OperationCanceledException`; keep the discarded-byte count
- [x] write reader tests: frames across many `Pipe` writes; two whole frames in one write with the writer still open are
  both yielded; completion ends the enumeration; cancellation throws `OperationCanceledException`; every async test has
  a timeout so a spinning reader fails instead of hanging
- [x] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 4: Sequence tracker
**Files:**
- Create: `SpectrumVisualizer.Core/Protocol/SequenceTracker.cs`
- Create: `SpectrumVisualizer.Tests.Unit/Core/Protocol/SequenceTrackerTests.cs`

- [ ] write tests first for every `seq` rule in Acceptance Evidence item 1, including the baseline, the
  heartbeat-then-spectrum follow-up and `Reset()`
- [ ] implement `SequenceTracker`
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 5: Wavelength calibration
**Files:**
- Create: `SpectrumVisualizer.Core/Calibration/SpectrometerCalibration.cs`
- Create: `SpectrumVisualizer.Tests.Unit/Core/Calibration/SpectrometerCalibrationTests.cs`
- Create: `SpectrumVisualizer.Tests.Unit/Core/Calibration/FirmwarePixelTables.cs` (the two firmware tables from OptoskyProj
  `df5201f` `main.c:166` and `:169`, as test data)

- [ ] write tests first: ATP-2000 `PixelFor` equals `PixelOrder_2000` for all 840 entries; ATP-8000 within 1 pixel of
  `PixelOrder_8000` for all 1564; wavelengths below and above the table range clamp; `Window` is inclusive and clamped
  to `0…P-1`; pixel 0 of ATP-2000 is 185.89856 nm
- [ ] implement `SpectrometerCalibration` with the coefficients from `SpectrumCalc.cs:6-7` and the table ranges
- [ ] run `dotnet test SpectrumVisualizer.slnx` - must pass before next task

### Task 6: Board emulator tool
**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tools.BoardEmulator/SpectrumVisualizer.Tools.BoardEmulator.csproj`,
  `Program.cs`, `EmulatorOptions.cs`, `SpectrumGenerator.cs`, `FrameSchedule.cs`
- Create: `SpectrumVisualizer.Tests.Unit/Tools/SpectrumGeneratorTests.cs`, `FrameScheduleTests.cs`
- Modify: `SpectrumVisualizer.slnx`, `SpectrumVisualizer/Directory.Packages.props` (pin `RJCP.SerialPortStream` 3.0.5 and
  `System.CommandLine` 2.0.2)

- [ ] create the console project referencing Core, with the options of Solution Overview
- [ ] implement `SpectrumGenerator` (Gaussian peak at the wavelength, flat floor, pixel count per device) and
  `FrameSchedule` (which frame to send at a tick: spectrum at the rate, heartbeat after 3 s of silence, bad CRC every
  nth frame, `seq` per the rules)
- [ ] write tests for both with a fake time source; the port write is not unit-tested
- [ ] run `dotnet test SpectrumVisualizer.slnx` and `dotnet run --project SpectrumVisualizer/SpectrumVisualizer.Tools.BoardEmulator -- --help`

### Task 7: Docs
**Files:**
- Create: `docs/architecture/board-protocol.md`
- Modify: `docs/architecture/README.md`, `docs/architecture/overview.md`, `CLAUDE.md` (layout line for the tool)

- [ ] write `board-protocol.md`: the frame (from OptoskyProj#20, in English), decoder rules, `seq` rules, calibration and
  the window (stating that the board's Amplitude window stays in pixels until the firmware change for OptoskyProj#7),
  the emulator's options, and the known stall: a corrupted header whose `len` reads as a valid value makes the decoder
  wait for that many bytes before the CRC can fail; present tense, no version numbers
- [ ] update the index, the overview components table and the CLAUDE.md layout

### Task 8: Verify acceptance criteria
- [ ] run every command of Acceptance Evidence and record the results
- [ ] confirm the old solution still builds and passes 13 tests

### Task 9: [Final] Update documentation
- [ ] update `readme.md` with the emulator's run command
- [ ] update `CLAUDE.md` if new patterns appeared
- [ ] leave this plan in `docs/plans/` (archiving is delivery work)

## Post-Completion
**Manual verification:**
- the port-pair smoke check above, during the owner's final assembly check.

**Remaining slices:** serial-link, spectrum-view, spectrum-store, intensity-trend, firmware-frame-sender, release-3-0.
