# Firmware frame sender

## Overview
- The board firmware in Semiteq/OptoskyProj sends the PC the frame of OptoskyProj#20 instead of the `01 FF…1E` word
  ladder: `AA 55 | len u16 LE | state u8 | seq u8 | [device u8, wavelength u16, delta u8, amplitude f32, snr f32,
  q f32, pixels u16 x P] | crc16 LE`, CRC-16/MODBUS over `len` through the end of the measurement part.
- A spectrum frame follows every finished measurement, a dark one included. A heartbeat follows 3 s with nothing sent.
  `seq` counts spectrum frames from 1 and wraps 255 to 0; a heartbeat repeats the last value.
- The frame bytes come from a byte generator in a new pure C module, `pc_link.c`: no AVR headers, no buffer for the
  frame, a running CRC, pixel bytes read straight from external SRAM through a hook. `main.c` keeps only the USART
  glue.
- The module compiles on the PC. A host program checks the send rules and writes golden frame bytes. The app's
  `FrameDecoder` and `FrameEncoder` in SpectrumVisualizer are the oracle for those bytes, so the C byte order is checked
  by code that was not written from the C.
- Scope guard: no PLC, spectrometer, encoder or calculation changes. The idle spectrometer's fault bit stays as it is.
  The board's Amplitude window stays in pixels until OptoskyProj#7. The PC receive path (`OnLegacyByte` channel 1,
  `Begin()`) stays.

## Context (from discovery)
- Roadmap: docs/plans/roadmaps/20261104-avalonia-rewrite-roadmap.md — slice firmware-frame-sender (Semiteq/OptoskyProj#20)
- The slice brief is the roadmap paragraph at `roadmap:251-272`. The thesis (`roadmap:14-15`) makes the frame of #20 the
  only contract between app and board; the guard strategy (`roadmap:94-95`) puts the real board after this slice.
- Two repositories, two local branches, local commits only, no push:
  - OptoskyProj at `C:/Users/admin/projects/OptoskyProj`, branch `firmware-frame-sender` created from `origin/master`
    at `df5201f`. Its tasks edit that checkout in place; exec's worktree isolation covers SpectrumVisualizer only.
  - SpectrumVisualizer, branch `firmware-frame-sender` stacked on `intensity-trend` at `2a4fc14`. This plan lives here,
    so the OptoskyProj PR carries no planning files.
- OptoskyProj facts (all `OptoskyEncoder/OptoskyEncoder/main.c` at `df5201f` unless named):
  - The board that talks to the PC is the `OptoskyEncoder` project; the other three folders hold Gerber files
    (`README.md:87-89`). The PC port is `USART_PC USARTC0` (`main.c:90`), set up by `UartInit` at 115200 8N1
    (`main.c:4`, `:1560-1569`, `:1604`).
  - The current send path is three functions driven from the main loop, no DMA, no interrupt:
    `StartSend2Aplication(d, now)` snapshots `Amplitude`, `Q_fact`, `NoizSNR` and arms `PcActive` (`:888-900`);
    `PcWord(n)` returns 16-bit words high byte first, with the `01FF FFFF FFFF FF1E` header, pixels, Amp, Q, SNR and the
    footer (`:901-923`); `Send2Aplication()` writes up to 32 bytes per pass while `USART_DREIF_bm` is set, waits for
    `USART_TXCIF_bm` at the end, abandons the frame after 1000 ms without progress, and resets
    `SEQ == SendToAplication_50` to `Beginning` (`:924-964`). The state for it is `:234-237`.
  - `Q_factor()` clears `SpectrumFault[ProcDevice - 1]`, marks the device connected, sets `SEQ = SendToAplication_50`
    and calls `StartSend2Aplication` (`:1057-1069`). The main loop runs `Q_factor()` then `Send2Aplication()` then
    `RequstAtp()` in each pass (`:1697-1701`).
  - `RequstAtp()` serves due service requests first, then returns while `PcActive` is set, so no new spectrum (dark or
    light) starts during a PC send (`:1484-1495`). External SRAM pixels therefore stay fixed while a frame goes out.
  - Pixels after dark subtraction sit in external SRAM at `EXT_DATA_2000` / `EXT_DATA_8000` (`:159-163`,
    `DataBase()` `:332-335`), written low byte first (`WriteWord` `:342-347`), read through `__far_mem_read`
    (`ReadWord` `:336-341`; macro in `SRC/ebi_driver.h:78`, `:125`).
  - Dark frames: `ReformForDarkSpectr()` stores the raw spectrum as the dark one when `ProcDark` is set and writes
    `raw - dark` clamped at 0 (`:1003-1022`), so a dark measurement leaves all-zero pixels and passes through
    `Signal_To_Noiz()` and `Q_factor()` like any spectrum (`:1031-1069`). The dark queue is `PendingDark`, set from a
    rising edge of PLC byte 3 bit 0 (`:1207-1215`) or a legacy `0x77` command (`:1352-1357`) and served before
    `PendingShot` (`:1496-1509`).
  - Measurement values: `Amplitude`, `Q_fact`, `NoizSNR` are `volatile uint16_t` (`:183`); `ProcDevice`, `ProcDelta`
    (`:222`) and `ProcWave` (`:225`) are the snapshot of the frame being processed, set in `RecievAtp_DMA`
    (`:777-782`). The PLC wavelength reaches `CurrentWavelength` through `SetWavelength()`, which clamps to 189-2460 nm
    and picks ATP-2000 below 904 nm (`:394-402`, `:1192-1195`); `delta` is capped at 100 (`:1195`).
  - State sources: `Ready_2000`, `Ready_8000` (`:184`, written in `SetConnection` `:456-462`); `SpectrumFault[2]`
    (`:218`), set by `CommFail` for a spectrum job (`:501-502`) and by a bad spectrometer checksum (`:994-998`), cleared
    only in `Q_factor()` of the same device (`:1064`). A device drops to not connected after 3 failed service requests
    (`:496-499`). The PLC reply builds its connected bits from the same `Ready_*` (`:1155`).
  - `crc16()` is CRC-16/MODBUS with a `uint8_t size` (`:370-380`), so it cannot cover a 4118-byte frame.
  - `ReadPlcFloat` swaps words (`:386-393`) and shares the global `float_converter` (`:189-193`) with
    `StanbyResponceMake` (`:1162-1164`); the PC frame uses neither.
  - Timing helpers: `Due()` compares with a signed difference (`:308-311`); the send path uses unsigned elapsed time
    (`:937`, `:958`). `TicksMs` counts milliseconds from `TCC0` (`:1645-1658`).
  - Build: the Atmel Studio project lists every compiled file in `<Compile Include>` items (`OptoskyEncoder.cproj:139-190`);
    `-funsigned-char` (`cproj:69`), `-fpack-struct` (`cproj:82`), `-Os` (`cproj:81`), all warnings (`cproj:84`). No
    `-std` flag is set. No CI and no `.gitattributes` exist in OptoskyProj; `main.c` is UTF-8 with BOM and CRLF line ends;
    `core.autocrlf` is false. `main.c` already has English comments (`:494`, `:506-509`, `:757-758`).
  - `.gitignore` ignores `*.o`, `*.bin`, `Debug/`, `Release/` but not `*.obj` or `*.exe` (`.gitignore`).
  - Docs: the README is Russian. It names USARTC0 "отладка через ПК" (`README.md:29`) and says debug messages go out on
    it (`README.md:226-230`); both are wrong once the frame protocol runs.
  - Commit style: Russian description, English type and scope, body with the problem and then why the change solves
    it, `Co-Authored-By` trailer (`a0e96bb`).
  - OptoskyProj#17 proposes `pc_link.c` as the home of the PC frame code and host-built tests for logic without
    registers. This slice extracts that one module because the host check needs it; the rest of #17 stays open.
- SpectrumVisualizer facts:
  - `FrameDecoder.TryReadFrame(ref ReadOnlySequence<byte>, out BoardFrame?, out long discarded)` returns false only
    when a whole frame is not buffered yet (`FrameDecoder.cs:22-63`).
  - `FrameEncoder.Encode(BoardFrame)` writes every multi-byte field with `BinaryPrimitives` little-endian and the CRC
    over `len`..end (`FrameEncoder.cs:8-29`, `:31-46`). Offsets live in `FrameLayout` (`FrameLayout.cs:8-21`).
  - `BoardFrame(State, Seq, Measurement?)` (`BoardFrame.cs:6`), `BoardState` bits 0-3 (`:8-16`);
    `SpectrumMeasurement` rejects a pixel count that does not match its device (`:29-34`).
  - Encoder and decoder share `FrameLayout`, so their round-trip tests cannot catch a layout both sides share wrongly.
    `ProtocolSamples.HeartbeatExample` (`ProtocolSamples.cs:13`) and `FrameEncoderTests` (`FrameEncoderTests.cs:19-26`)
    pin the one external sample, the heartbeat of #20.
  - Test traits `Component`, `Area`, `Category` (`FrameEncoderTests.cs:13-15`); a data file is copied to the output
    with `<None Update=… CopyToOutputDirectory="PreserveNewest"/>` (`SpectrumVisualizer.Tests.Unit.csproj:27`).
  - The emulator's heartbeat timer runs from the last frame sent (`FrameSchedule.cs:26`, `:48`, `:57`).
  - `docs/architecture/board-protocol.md` restates #20: frame `:13-68`, dark frames `:58`, heartbeat example `:60-65`.
- Host compiler, verified 2026-11-05: Visual Studio Community 2026 with MSVC 14.51.36231 and Windows SDK 10.0.26100.0
  is installed; `cl.exe -nologo -W4 -WX -std:c11 -Foout/ -Feout/t.exe t.c` with `INCLUDE` and `LIB` set explicitly
  compiled and ran a C11 file from Git Bash. No gcc or clang is on `PATH`. No AVR compiler is on this machine.

## Development Approach
- **testing approach**: Regular.
- Complete each task fully before moving to the next; every task ends with its checks passing.
- OptoskyProj tasks commit in OptoskyProj with a Russian description; SpectrumVisualizer tasks commit there in English.
  Headers follow `review:writing-style`; the trailing reference is the issue, never a PR.
- OptoskyProj commits never go through the stage script, which commits in the current directory. They use
  `git -C C:/Users/admin/projects/OptoskyProj add <files>` and `git -C C:/Users/admin/projects/OptoskyProj commit`.
- Review phases that diff only SpectrumVisualizer get the OptoskyProj files by absolute path
  (`C:/Users/admin/projects/OptoskyProj/OptoskyEncoder/OptoskyEncoder/main.c`, `pc_link.c`, `pc_link.h`,
  `C:/Users/admin/projects/OptoskyProj/OptoskyEncoder/HostTest/pc_link_test.c`), so the C code gets its review.
- The AVR glue is not compiled in this run: the team builds the firmware by hand, and no AVR toolchain is installed.
  Every rule that can live without registers lives in `pc_link.c` and is checked on the host; `main.c` changes stay
  thin glue that the owner's build and board check cover.

## Testing Strategy
- **host C checks** (OptoskyProj): `OptoskyEncoder/HostTest/pc_link_test.c`, plain C11 with no test framework. Each
  check prints its name on failure and the program exits 1; on success it prints `all checks passed`, writes the golden
  frames file named by `argv[1]` and exits 0.
- **unit tests** (SpectrumVisualizer): `FirmwareFrameTests` in `SpectrumVisualizer.Tests.Unit/Core/Protocol`, traits
  `Component=Core`, `Area=Protocol`, `Category=Unit`. One fact and two theories over `MemberData(nameof(FrameNames))`,
  the names of the expected frames: 9 tests. `FirmwareRamp` builds the ramp in C#, and the heartbeat is
  `ProtocolSamples.Heartbeat(42)`.
- **board**: the owner's manual checklist in Acceptance Evidence. It covers what the host check leaves unverified,
  listed by name at the start of that checklist.

## Acceptance Evidence
Today the board sends the `01 FF…1E` ladder high byte first (`main.c:901-923`, `:948` at df5201f); the app's `FrameDecoder` finds
no `AA 55` frame in it, and nothing outside the app's own encoder pins the byte order of a spectrum frame.

Automatable, on this machine:

1. Host C checks and golden frames, from Git Bash:

   ```bash
   V="C:/Program Files/Microsoft Visual Studio/18/Community/VC/Tools/MSVC/14.51.36231"
   K="C:/Program Files (x86)/Windows Kits/10"; S=10.0.26100.0
   cd C:/Users/admin/projects/OptoskyProj/OptoskyEncoder/HostTest && mkdir -p out
   INCLUDE="$V/include;$K/Include/$S/ucrt;$K/Include/$S/shared;$K/Include/$S/um" \
   LIB="$V/lib/x64;$K/Lib/$S/ucrt/x64;$K/Lib/$S/um/x64" \
   "$V/bin/Hostx64/x64/cl.exe" -nologo -W4 -WX -std:c11 -I../OptoskyEncoder -Foout/ -Feout/pc_link_test.exe \
     pc_link_test.c ../OptoskyEncoder/pc_link.c
   ./out/pc_link_test.exe out/firmware-frames.txt
   ```

   Compiles with 0 warnings, prints `all checks passed`, exits 0. From Git Bash, keep the `-Fo` and `-Fe` paths
   relative: MSYS rewrites an absolute `C:/` path glued to a flag letter. The same build from a Developer PowerShell
   (`Common7/Tools/Launch-VsDevShell.ps1 -Arch amd64`) is `cl /nologo /W4 /WX /std:c11 /I..\OptoskyEncoder /Foout\
   /Feout\pc_link_test.exe pc_link_test.c ..\OptoskyEncoder\pc_link.c`; with gcc it is `gcc -std=c11 -Wall -Wextra
   -Werror -I../OptoskyEncoder -o out/pc_link_test pc_link_test.c ../OptoskyEncoder/pc_link.c`.
2. The checked-in golden file matches the C output byte for byte:
   `git diff --no-index --exit-code --ignore-cr-at-eol C:/Users/admin/projects/OptoskyProj/OptoskyEncoder/HostTest/out/firmware-frames.txt
   C:/Users/admin/projects/SpectrumVisualizer/SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Core/Protocol/FirmwareFrames.txt`
   exits 0.
3. `dotnet test SpectrumVisualizer.slnx --filter "FullyQualifiedName~FirmwareFrameTests"` passes: every golden frame
   decodes through `FrameDecoder` with 0 discarded bytes to the expected `BoardFrame`, and equals
   `FrameEncoder.Encode` of that frame byte for byte.
4. The check bites: with the pixel bytes of `PcFrameNextByte` swapped (high byte first), the program's output copied
   over `FirmwareFrames.txt` makes `FirmwareFrameTests` fail. Then `git checkout -- OptoskyEncoder/OptoskyEncoder/pc_link.c`
   in OptoskyProj and `git checkout -- SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Core/Protocol/FirmwareFrames.txt`
   in SpectrumVisualizer restore both; nothing of it is committed.
5. `dotnet test SpectrumVisualizer.slnx` passes in full; `dotnet build SpectrumVisualizer.slnx -c Release` gives 0
   warnings; `dotnet format SpectrumVisualizer.slnx --verify-no-changes` and `dotnet terse` on the new test file exit 0.
6. `grep -n "PcWord\|PcLowByte\|PcAmp\|PcActive" OptoskyEncoder/OptoskyEncoder/main.c` in OptoskyProj prints nothing,
   and `grep -n "pc_link" OptoskyEncoder/OptoskyEncoder/OptoskyEncoder.cproj` prints the two new `Compile Include`
   lines.

Manual board checklist (owner, after building `OptoskyEncoder.atsln` in Atmel Studio and flashing per `README.md:115-127`,
with the app from this branch on the USARTC0 adapter at 115200).

The host check leaves three things unverified; the steps below cover them:
- the DREIF/TXCIF/1000 ms state machine in the new `Send2Aplication()`: steps 1-3 (frames complete, heartbeats and
  spectra alternate, no frame is cut);
- the bit pattern of the target's `(float)uint16_t`: step 2 (Amplitude, SNR and Q in the app equal the PLC values);
- the main-loop pass rate that keeps the 2-byte USARTC0 TX buffer fed: step 4. Throughput is passes x 2 bytes per
  pass, as today; the 32-byte budget at `main.c:920` never delivers 32 bytes in one pass, because the loop stops when
  `DREIF` clears. A slow pass rate never shows as the 1000 ms abandon or a `seq` gap, because every byte written
  restarts the 1000 ms limit. It shows only as a longer frame time, so step 4 measures it.

The app's screen shows the link state, the two connected bits and the lost-spectra total in the status bar. `seq`, the
state byte and the frame size are visible only in the spectrum store, and each loss also writes a log line:
- Store: the newest `%LOCALAPPDATA%\SpectrumVisualizer\Spectra\spectra-<pid>.db`, `<pid>` being the app's process id.
  The file is in WAL mode, so the `sqlite3` shell reads it while the app runs. The last 20 frames, newest first:

  ```
  sqlite3 "%LOCALAPPDATA%\SpectrumVisualizer\Spectra\spectra-<pid>.db" "SELECT 'spectrum' AS kind, received_at, seq, state, device, wavelength_nm, amplitude, snr, 24 + length(pixels) AS frame_bytes FROM spectrum UNION ALL SELECT 'heartbeat', received_at, seq, state, NULL, NULL, NULL, NULL, 8 FROM heartbeat ORDER BY received_at DESC LIMIT 20;"
  ```

  `received_at` is Unix time in milliseconds. The store keeps 2 bytes per pixel, so `frame_bytes` is 8 for a heartbeat,
  1048 for ATP-8000 and 4120 for ATP-2000.
- Log: the newest `%LOCALAPPDATA%\SpectrumVisualizer\Logs\spectrum-visualizer*.log`. Serilog rolls the file at 5 MB
  (`spectrum-visualizer_001.log` and on), so read the file with the latest change time. Each loss writes the
  Information line `<n> spectra lost before seq <s> on port <port>`; the lost total in the status bar shows the same
  count.

The `state` column is a bit field (`PcStateByte`, `main.c:891-895`): bit 0 ATP-2000 connected (`Ready_2000`), bit 1
ATP-8000 connected (`Ready_8000`), bit 2 ATP-2000 fault, bit 3 ATP-8000 fault. Decode it as `state & 1`, `state & 2`,
`state & 4`, `state & 8`.

1. Hold PLC byte 3 bit 0 low at power-up. The edge detector starts with `previous_state` 0 (`main.c:201`), so a bit
   already high at boot reads as a rising edge and queues a dark spectrum (`main.c:290-296`, `main.c:1175-1183`); a
   dark `seq` 1 row is then correct behaviour, and step 2's first spectrum is that dark one. Power the board with both
   spectrometers and no PLC trigger. About 3 s after power-up the status bar shows the link up: the first heartbeat
   comes 3000 ms after `PcSenderInit`. Heartbeat rows then arrive every 3 s with `seq` 0. State bits 0 and 1 (ATP-2000
   and ATP-8000 in the status bar) set once the spectrometers answer; the fault bits 2 and 3 stay 0.
2. Trigger a measurement on ATP-8000 (PLC wavelength 1550 nm, pulse mode). Pulse mode measures continuously, so this
   starts a stream of spectra. The first spectrum row has `seq` 1 and `frame_bytes` 1048; the chart shows the spectrum
   with wavelength 1550 and `delta` equal to min(PLC delta, 100) (`main.c:1163`). Stop pulse mode, then compare
   Amplitude, SNR and Q on screen with the values the PLC reads: they must be equal. During a live stream they differ,
   because the PLC reply carries the globals at poll time and the next measurement overwrites them about one frame
   before the app shows that frame.
3. Keep measuring. `seq` rises by 1 per spectrum row; the lost total in the status bar stays 0, the log has no
   "spectra lost" line, and the link never shows "other device". Then stop pulse mode for at least 3 s after some
   spectra: heartbeat rows arrive every 3 s and repeat the last spectrum's non-zero `seq`. The first spectrum after a
   restart of pulse mode carries that `seq` + 1.
4. Set the PLC wavelength to 650 nm (below 904 nm, `main.c:393-401`). The next spectrum rows are ATP-2000 rows with
   `frame_bytes` 4120 and wavelength 650; the spectrum and trend follow. Measure the frame time with a logic analyser
   or an oscilloscope on USARTC0 TX: one ATP-2000 frame from the first `AA` to the last CRC byte takes about 358 ms
   (4120 bytes x 10 bits at 115200 baud), one ATP-8000 frame about 91 ms. A clearly longer frame means the main loop
   does not keep the TX buffer fed. Without an analyser or an oscilloscope, record this check as not checkable: the
   old app shows no rate and the new app cannot read the old frames, so no baseline exists. The risk of a starved TX
   buffer then stays open.
5. Request a dark measurement (PLC byte 3 bit 0 rising edge, `main.c:1175-1183`). One spectrum row arrives with the
   next `seq` and `amplitude` and `snr` 0, and the chart shows all pixels at 0; the following light spectra show the
   dark-subtracted signal.
6. Change the PLC wavelength while measuring continuously. The frame's wavelength follows the PLC within one
   measurement, and the trend recomputes at the new wavelength.
7. Unplug the measuring spectrometer while pulse mode runs. Its fault bit sets first: one failed spectrum job calls
   `CommFail(JOB_SPECTRUM)` (`main.c:500-501`). Its connected bit clears later, after 3 failed service requests 2 s
   apart (`main.c:492-499`). In the store's `state` column (decoded as above) and in the status bar, the unplugged
   device, not the other one, shows failed and then not connected; the other device's bits do not change.
8. Leave pulse mode running for at least 255 spectra. The store shows `seq` 255 followed by 0, and the lost total
   stays 0.
9. Reset the board while the app runs, best in the middle of a spectrum frame. Frames resume within 10 s: a frame cut
   by the reset stalls the decoder for about one frame period while spectra flow, and up to the 10 s drain otherwise
   (`board-protocol.md`, Drain). The gap shows as a "spectra lost
   before seq" log line and a higher lost total, because the restart's first frame does not continue `seq` (per #20 a
   restart reads as loss); the trend shows a NoSpectra break when more than 10 s pass between spectra. The app
   recovers without a reconnect.

## Progress Tracking
- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix

## Solution Overview
- `OptoskyEncoder/OptoskyEncoder/pc_link.h` and `pc_link.c`, pure C: `<stdint.h>`, `<stdbool.h>`, `<string.h>`, no
  `<avr/...>`, no `stdio`. Two parts:
  - The frame generator `PcFrame`: begun as a heartbeat or a spectrum, then pulled one byte at a time. It keeps the
    common part (`len`, `state`, `seq` and, for a spectrum, `device`..`q`, at most 20 bytes) in a small array, asks a
    pixel hook for the pixel bytes, and folds every byte from offset 2 on into a running CRC that it emits low byte
    first at the end. No frame buffer.
  - The sender `PcSender`: the send rules of #20. It holds the queued spectrum's values, the active frame, `seq` and
    the time the last frame finished. `PcSenderStartNext(now, state)` starts the queued spectrum first (incrementing
    `seq`), else a heartbeat when 3000 ms passed since the last frame finished (or since init), else nothing.
    `PcSenderFinish(now)` ends the active frame and says whether it was a spectrum. `PcSenderActive()` is true while a
    frame is active; `PcSenderBusy()` is true while a frame is active or a spectrum is queued.
- `main.c` glue: `StartSend2Aplication(d)` queues the spectrum with `ProcWave`, `ProcDelta` and the three PLC values
  as floats; `Send2Aplication()` starts the next frame through `StartPcFrame` when idle, returns while
  `PcSenderActive()` is false, pumps bytes into `USARTC0.DATA` with the same 32-byte `DREIF` budget, finishes on `TXCIF` or after 1000 ms without progress, and resets `SEQ` only when a spectrum frame
  finishes. The pixel hook reads `__far_mem_read(DataBase(device) + index)`. `RequstAtp()` waits on
  `PcSenderBusy()` instead of `PcActive`. `PcWord()` and its state are deleted.
- Host check: `OptoskyEncoder/HostTest/pc_link_test.c` (outside the cproj) runs the rule checks and writes golden
  frames. SpectrumVisualizer checks those bytes with its decoder and encoder.

Decisions:
- The host check is a pure C module compiled on the PC plus golden bytes checked into SpectrumVisualizer and decoded
  by the app. MSVC is already installed, so nothing new is installed; the golden file is committed, so the app's tests
  and CI need no C compiler. The decoder and encoder were written from #20, not from the C, so a byte order error in
  the C fails a test. Byte equality with `FrameEncoder` also catches a field the decoder ignores (reserved state bits).
- The heartbeat bytes of #20 (`AA 55 02 00 03 2A 80 B3`) are also a self-check inside the C program, so the C side
  meets one external sample before any golden file exists.
- The module is `pc_link.c`, the name OptoskyProj#17 gives the PC frame module. Only this module moves out of
  `main.c`.
- The C program uses no test framework (#17 mentions Unity or Ceedling): a dozen checks do not earn a new dependency.
- Floats go to bytes through `memcpy` into a `uint32_t` and shifts, so the order is explicit on any host. The global
  `float_converter` stays untouched. ASSUMPTION: avr-gcc 8 `float` is IEEE 754 single precision, 4 bytes, the
  same format `(float)` conversion gives on the PC; a `_Static_assert(sizeof(float) == 4, …)` guards the size, and the
  board check guards the format.
- The new C files use `uint8_t`, never plain `char` (`-funsigned-char` on AVR only, `cproj:69`), and never write a
  struct to the wire (`-fpack-struct` on AVR only, `cproj:82`).
- The new C files are ASCII only, English comments, CRLF line ends, UTF-8 with BOM like `main.c`. Non-ASCII text would
  trip MSVC C4819 under a Russian code page, and `-WX` makes it fatal.
- The CRC step lives once in `pc_link.c` as `PcCrc16Update`. The PLC `crc16()` stays as it is: changing it touches the
  PLC path, which the scope guard forbids.
- `seq` is a new counter inside `PcSender`, not the global `SEQ` state variable. It increments when a spectrum frame
  starts. A frame abandoned after 1000 ms without progress keeps its `seq`, so the app sees the loss.
- The state byte is built when a frame starts: bit 0 `Ready_2000`, bit 1 `Ready_8000`, bit 2 `SpectrumFault[0]`, bit 3
  `SpectrumFault[1]`, bits 4-7 zero. `Q_factor()` clears the measuring device's fault before it queues the frame
  (`main.c:1064`, `:1068` at df5201f), so a spectrum frame never carries its own device's fault bit.
- The values come from the moment `Q_factor()` queues the spectrum. Pixels are not copied: no new spectrum starts
  while `PcSenderBusy()` is true, and `Send2Aplication()` starts a queued spectrum in the same pass that finishes a
  heartbeat, before `RequstAtp()` runs (`main.c:1698-1701` at df5201f).
- A queued spectrum has priority over a due heartbeat. The 3000 ms heartbeat timer runs from the end of the last
  frame (the `TXCIF` moment or the abandon), and from `PcSenderInit` at boot, so the first heartbeat comes 3 s after
  power-up, as in the emulator.
- Elapsed time is unsigned (`(uint32_t)(now - last) >= 3000`), the form the send path already uses, so a `TicksMs`
  wrap is harmless.
- The 32-byte budget per pass and the abandon after 1000 ms stay. ASSUMPTION: the CRC step (8 shift-and-xor rounds)
  costs about 100 cycles per byte at 32 MHz, against about 2800 cycles per byte at 115200 baud; the frame time stays
  set by the baud rate (4120 bytes about 358 ms).
- `StartSend2Aplication` and `Send2Aplication` keep their names; #20 and the roadmap name them.
- Host build output goes to `OptoskyEncoder/HostTest/out/`, which `.gitignore` ignores, so no `.obj` or `.exe` lands in
  the OptoskyProj PR.
- The golden file is text: one frame per line, `<name> <uppercase hex without spaces>`, so a diff shows which frame
  and byte moved. The C program writes LF, and SpectrumVisualizer stores the file with LF
  (`SpectrumVisualizer/** text eol=lf`). The drift check ignores CR at end of line for a copy an editor saved with CRLF,
  and the C# test reads lines with `File.ReadAllLines`.

## Technical Details
`pc_link.h` surface:

```c
#define PC_LINK_HEARTBEAT_MS 3000UL
#define PC_DEVICE_2000 1
#define PC_DEVICE_8000 2
#define PC_MEASUREMENT_SIZE 16
#define PC_FRAME_COMMON_MAX (4 + PC_MEASUREMENT_SIZE)

typedef uint8_t (*PcPixelReader)(uint8_t device, uint16_t byteIndex); /* byte k of the pixel area, k < 2 * P */

typedef struct
{
    uint8_t device;      /* PC_DEVICE_2000 or PC_DEVICE_8000 */
    uint16_t wavelength; /* nm */
    uint8_t delta;
    float amplitude, snr, q;
} PcMeasurement;

uint16_t PcCrc16Update(uint16_t crc, uint8_t value);

void PcFrameBeginHeartbeat(PcFrame *frame, uint8_t state, uint8_t seq);
void PcFrameBeginSpectrum(PcFrame *frame, uint8_t state, uint8_t seq, const PcMeasurement *measurement);
bool PcFrameDone(const PcFrame *frame);
uint8_t PcFrameNextByte(PcFrame *frame, PcPixelReader readPixel);

void PcSenderInit(PcSender *sender, uint32_t now);
void PcSenderQueueSpectrum(PcSender *sender, const PcMeasurement *measurement);
bool PcSenderStartNext(PcSender *sender, uint32_t now, uint8_t state); /* true when a frame started */
PcFrame *PcSenderFrame(PcSender *sender);
bool PcSenderFinish(PcSender *sender, uint32_t now);                   /* true when the finished frame was a spectrum */
bool PcSenderActive(const PcSender *sender);
bool PcSenderBusy(const PcSender *sender);
```

`PcFrame` holds the common-part array, its size, the pixel byte count (`2 * P`, 0 for a heartbeat), the device, the
next byte position and the CRC. `PcFrameDone` derives the frame size from the common-part size and the pixel byte
count, so the size is not stored twice. `PcSender` holds a `PcFrame`, the queued `PcMeasurement`, flags for
active, active-is-spectrum and spectrum-queued, `seq` and the last-finished time.

Frame bytes, positions from 0:

| Position | Bytes | Source |
| --- | --- | --- |
| 0-1 | `AA 55` | constant, not in the CRC |
| 2-3 | `len` LE | 2, 4114 (ATP-2000) or 1042 (ATP-8000) |
| 4 | `state` | argument |
| 5 | `seq` | argument |
| 6 | `device` | measurement |
| 7-8 | `wavelength` LE | measurement |
| 9 | `delta` | measurement |
| 10-13, 14-17, 18-21 | `amplitude`, `snr`, `q`, each f32 LE | `memcpy` to `uint32_t`, then low byte first |
| 22 … 21 + 2P | pixels | `readPixel(device, k)`, k = 0 … 2P-1 |
| last 2 | CRC LE | running CRC over positions 2 … end of pixels |

Frame sizes: heartbeat 8, ATP-2000 4120, ATP-8000 1048.

`main.c` glue, in place of `main.c:234-237` and `:888-964` at df5201f:

```c
static PcSender PcLink;
static uint32_t PcLastProgressMs;

static uint8_t ReadPcPixelByte(uint8_t device, uint16_t byteIndex)
{
    return __far_mem_read(DataBase(device) + byteIndex);
}
static uint8_t PcStateByte(void)
{
    return (uint8_t)((Ready_2000 ? 1 : 0) | (Ready_8000 ? 2 : 0) | (SpectrumFault[0] ? 4 : 0) |
                     (SpectrumFault[1] ? 8 : 0));
}
```

- `StartSend2Aplication(uint8_t d)` builds a `PcMeasurement` with a designated initializer (`.device = d`,
  `.wavelength = ProcWave`, `.delta = ProcDelta`, `.amplitude = (float)Amplitude`, `.snr = (float)NoizSNR`,
  `.q = (float)Q_fact`) and calls `PcSenderQueueSpectrum`. `Q_factor()` calls it without `now`.
- `StartPcFrame(now)` calls `PcSenderStartNext(&PcLink, now, PcStateByte())`; on a start it clears `TXCIF` and sets
  `PcLastProgressMs = now`.
- `Send2Aplication()`, per pass: `StartPcFrame(now)`, then return while `PcSenderActive(&PcLink)` is false. While the
  frame is not done and `DREIF` is set, up to 32 bytes:
  `USARTC0.DATA = PcFrameNextByte(PcSenderFrame(&PcLink), ReadPcPixelByte)`; every byte written sets
  `PcLastProgressMs = now`, as `main.c:952` does at df5201f. When the frame is done and `TXCIF` is set,
  or 1000 ms pass without progress, `PcSenderFinish`; if it returns true and `SEQ == SendToAplication_50`, set
  `SEQ = Beginning`. The finish check runs in the same pass as the last byte write. After a finish, `StartPcFrame`
  runs once more in the same pass, so a queued spectrum goes out before `RequstAtp()` runs.
- `main()` calls `PcSenderInit(&PcLink, GetMilliseconds())` after `sei()` (`main.c:1679` at df5201f).
- `RequstAtp()`: `if (PcActive) return;` (`main.c:1494-1495` at df5201f) becomes `if (PcSenderBusy(&PcLink)) return;`.

Golden frames (the C program writes them; the C# test builds the same frames independently):

| Name | state | seq | device | wavelength | delta | amplitude | snr | q | pixels |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `heartbeat-example` | `0x03` | 42 | none | | | | | | none; bytes `AA 55 02 00 03 2A 80 B3` |
| `atp2000` | `0x09` | 1 | 1 | 650 | 10 | 4660 | 258 | 7 | ramp |
| `atp8000` | `0x06` | 255 | 2 | 1550 | 100 | 65535 | 1 | 0 | ramp |
| `atp8000-dark` | `0x03` | 0 | 2 | 1679 | 10 | 0 | 0 | 3 | all zero |

Ramp: `pixel[i] = (uint16_t)(i * 257u + 0x1234u)`, so every pixel has different high and low bytes. The C program puts
the pixels into a byte array with `WriteWord`'s layout (low byte at `2i`, high at `2i + 1`, `main.c:342-347`), and its
pixel hook reads that array, as the board reads external SRAM. Float values are whole numbers, as `(float)uint16_t`
gives on the board.

Host rule checks in `pc_link_test.c`:
- `PcCrc16Update` over ASCII `123456789` gives `0x4B37`;
- the heartbeat (state `0x03`, seq 42) equals `AA 55 02 00 03 2A 80 B3`;
- frame sizes 8, 4120, 1048; `len` bytes `02 00`, `12 10`, `12 04`;
- after init, no heartbeat at 2999 ms, a heartbeat with seq 0 at 3000 ms;
- the first queued spectrum gets seq 1; the next heartbeat repeats 1; a heartbeat timer restarts at each finish;
- a spectrum queued while a heartbeat is active does not start until `PcSenderFinish`, then starts before any
  heartbeat; `PcSenderBusy` is true while it waits;
- `PcSenderFinish` returns true for a spectrum and false for a heartbeat;
- 256 spectra take seq from 1 through 255 to 0;
- the timer works across a `uint32_t` wrap (init at `0xFFFFF000`): no heartbeat 100 ms after a finish at
  `0xFFFFFF00`, whose 3000 ms deadline wraps; a heartbeat 68000 ms after a finish, so elapsed time keeps 32 bits;
- every spectrum frame's trailer equals CRC-16/MODBUS recomputed over its bytes from `len` to the last pixel; the
  `atp2000` and `atp8000` frames end with `AE 43` and `B0 2A`, the trailers `FirmwareFrameTests` accepts;
- the pixel hook gets the measurement's device, for both frame kinds and through the sender;
- a heartbeat after a spectrum finishes as a heartbeat;
- a spectrum abandoned mid-frame finishes as a spectrum; the next spectrum takes the next seq with a fresh CRC;
- the render buffer is one byte longer than the largest frame, so a frame that runs long fails its size check.

## What Goes Where
- **Implementation Steps**: the C module and host program, the `main.c` glue, the golden file and C# test, the docs in
  both repositories.
- **Post-Completion**: the build in Atmel Studio, flashing, the board run against the app, the PR notes.

## Implementation Steps

### Task 1: Frame generator and host program
Repository: OptoskyProj. Commit: `feat(firmware): добавить генератор кадра для ПК (#20)`, body in Russian.

**Files:**
- Create: `OptoskyEncoder/OptoskyEncoder/pc_link.h`
- Create: `OptoskyEncoder/OptoskyEncoder/pc_link.c`
- Create: `OptoskyEncoder/HostTest/pc_link_test.c`
- Modify: `OptoskyEncoder/OptoskyEncoder/OptoskyEncoder.cproj` (two `<Compile Include>` items, next to `main.c` at
  `cproj:139`)
- Modify: `.gitignore` (`OptoskyEncoder/HostTest/out/`)

- [x] write `PcMeasurement`, `PcFrame`, `PcCrc16Update`, `PcFrameBeginHeartbeat`, `PcFrameBeginSpectrum`,
  `PcFrameDone`, `PcFrameNextByte` per Technical Details, with `_Static_assert(sizeof(float) == 4, ...)`
- [x] add `pc_link.c` and `pc_link.h` to the cproj; the `HostTest` folder stays out of it
- [x] write the frame checks in `pc_link_test.c`: CRC check value, heartbeat example bytes, frame sizes and `len`
  bytes, field bytes at their positions for the `atp2000` golden frame (`8A 02` wavelength, `00 A0 91 45` amplitude
  4660.0f), pixel bytes low first
- [x] make `pc_link_test.exe <path>` write the four golden frames of Technical Details to `<path>` in binary mode
- [x] run Acceptance Evidence item 1: 0 warnings, `all checks passed`, exit 0

### Task 2: Send rules
Repository: OptoskyProj. Commit: `feat(firmware): добавить правила отправки кадров ПК (#20)`, body in Russian.

**Files:**
- Modify: `OptoskyEncoder/OptoskyEncoder/pc_link.h`, `pc_link.c`
- Modify: `OptoskyEncoder/HostTest/pc_link_test.c`

- [x] write `PcSender` with `PcSenderInit`, `PcSenderQueueSpectrum`, `PcSenderStartNext`, `PcSenderFrame`,
  `PcSenderFinish`, `PcSenderActive`, `PcSenderBusy` per Solution Overview
- [x] write the sender checks of Technical Details: heartbeat at 3000 ms and not 2999, seq 0 before any spectrum,
  first spectrum seq 1, heartbeat repeats the last seq, spectrum waits for an active heartbeat and then has priority,
  `PcSenderBusy` while queued, finish result per kind, wrap 255 to 0, `uint32_t` time wrap
- [x] run Acceptance Evidence item 1: 0 warnings, `all checks passed`, exit 0

### Task 3: Golden frames in the app's tests
Repository: SpectrumVisualizer. Commit: `test(protocol): decode the firmware golden frames`, English.

**Files:**
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Core/Protocol/FirmwareFrames.txt` (copied from the
  output of Task 2's program)
- Create: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Core/Protocol/FirmwareFrameTests.cs`
- Modify: `SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/SpectrumVisualizer.Tests.Unit.csproj` (copy the data file
  to the output, as `xunit.runner.json` at `:27`)

- [x] copy `out/firmware-frames.txt` to `FirmwareFrames.txt`; Acceptance Evidence item 2 exits 0
- [x] build the four expected `BoardFrame`s in C# from the table in Technical Details (ramp computed in C# by
  `FirmwareRamp`, not read from the file; heartbeat from `ProtocolSamples.Heartbeat(42)`)
- [x] write a theory over the four names (`MemberData(nameof(FrameNames))`): the line decodes through `FrameDecoder.TryReadFrame` to one frame, 0
  discarded bytes, equal to the expected frame (`BeEquivalentTo`)
- [x] write a theory over the four names: the line equals `FrameEncoder.Encode(expected)` byte for byte
- [x] write a fact that the file holds exactly the four names, so a frame missing from the C output fails
- [x] run `dotnet test SpectrumVisualizer.slnx --filter "Area=Protocol"`, `dotnet format SpectrumVisualizer.slnx
  --verify-no-changes`, `dotnet terse` on the new file - must pass before task 4

### Task 4: Send the new frame from main.c
Repository: OptoskyProj. Commit: `feat(firmware): отправлять ПК кадр нового протокола (#20)`, body in Russian.

**Files:**
- Modify: `OptoskyEncoder/OptoskyEncoder/main.c`

- [x] `#include "pc_link.h"`; replace the PC send state at `main.c:234-237` with `PcLink` and `PcLastProgressMs`
- [x] add `ReadPcPixelByte`, `PcStateByte` and `StartPcFrame`; rewrite `StartSend2Aplication(uint8_t d)` and `Send2Aplication()` per
  Technical Details; delete `PcWord()`
- [x] change the call in `Q_factor()` (`main.c:1068`) and the guard in `RequstAtp()` (`main.c:1494-1495`); call
  `PcSenderInit` in `main()` after `sei()`
- [x] leave `crc16()`, `ReadPlcFloat`, `float_converter`, `OnLegacyByte`, `Begin()`, the PLC, spectrometer, encoder
  and calculation code byte for byte unchanged: `git diff` touches only the lines above
- [x] no host test reaches `main.c` (the AVR glue is not compiled in this run); re-run Acceptance Evidence item 1 and item 6 - must pass
  before task 5

### Task 5: Verify acceptance criteria
Repository: SpectrumVisualizer, this plan file only. Commit: `docs(plans): record firmware-frame-sender checks`,
English.

- [x] run Acceptance Evidence items 1, 2, 3, 5 and 6 and record the results here
- [x] run item 4 (pixel byte swap makes `FirmwareFrameTests` fail), then revert the swap; `git status` in OptoskyProj
  shows no change after the revert
- [x] confirm every requirement of Overview maps to a check: frame bytes (items 1-3), send rules (item 1), glue
  (item 6 and the board checklist)
- [x] `git log --oneline origin/master..HEAD` in OptoskyProj lists only this slice's commits and no planning files
- [x] `dotnet test SpectrumVisualizer.slnx` passes in full
- [x] manual board checklist steps 1-9 (skipped - not automatable: no board, no AVR toolchain)

Results, 2026-11-05:
- Item 1: `cl -W4 -WX -std:c11` exit 0 with no warning lines; `pc_link_test.exe` prints `all checks passed`, exit 0.
- Item 2: the drift check `git diff --no-index --exit-code --ignore-cr-at-eol` exits 0.
- Item 3: `FirmwareFrameTests` 9 passed, 0 failed.
- Item 4: `PcFrameNextByte` reading pixel byte `k ^ 1` (high byte first). The real host program then fails its own
  checks (`first pixel 0x1234 goes low byte first`, `second pixel 0x1335 goes low byte first`), exits 1 and writes no
  file. A scratch copy of `pc_link_test.c` without the `Failures` exit wrote the swapped frames; copied over
  `FirmwareFrames.txt`, they make `FirmwareFrameTests` fail 4 of 9 (`atp2000` and `atp8000` in both theories; the
  all-zero `atp8000-dark` and the heartbeat carry no byte order to swap). After both `git checkout --` restores,
  `git status --short` in OptoskyProj prints nothing, and item 1, item 2 and `FirmwareFrameTests` (9 passed) pass again.
- Item 5: `dotnet build SpectrumVisualizer.slnx -c Release` 0 warnings, 0 errors; `dotnet test SpectrumVisualizer.slnx`
  932 passed, 0 failed; `dotnet format --verify-no-changes` exit 0; `dotnet terse FirmwareFrameTests.cs` exit 0.
- Item 6: the `PcWord|PcLowByte|PcAmp|PcActive` grep on `main.c` prints nothing (exit 1); the cproj grep prints
  `142: <Compile Include="pc_link.c">` and `145: <Compile Include="pc_link.h">`.
- Old solution: `dotnet build SpectrumVisualizer.sln` 0 errors (15 warnings, the old app's own); `dotnet test
  SpectrumVisualizer.Tests` 13 passed.
- Overview mapping: frame bytes are covered by items 1-3 and the item 4 bite; the send rules (`seq`, 3 s heartbeat,
  spectrum priority, wraps) by the sender checks of item 1; the `main.c` glue by item 6 and board checklist steps 1-9.
- OptoskyProj `origin/master..HEAD` before the docs task and the review fixes: `001254f`, `bc9e6ca`, `1440c90`;
  `--stat` touches only `.gitignore`, `HostTest/pc_link_test.c`, `OptoskyEncoder.cproj`, `pc_link.c`, `pc_link.h` and
  `main.c`; no planning files. The final list of 7 commits is in Post-Completion.

Results after the review fixes, 2026-11-05, at OptoskyProj `47788bd` and SpectrumVisualizer `06d1c4f`:
- Item 1: `cl -W4 -WX -std:c11` exit 0 with no warning lines; `all checks passed`, exit 0. The host program now also
  recomputes the CRC of every spectrum frame, pins the trailers `AE 43` and `B0 2A`, records the device the pixel hook
  gets, and checks the abandon path and the time wrap at finish + 100 ms and + 68000 ms.
- Item 2: drift check exit 0; no golden byte changed in the review fixes.
- Items 3 and 5: `FirmwareFrameTests` 9 passed; `dotnet test SpectrumVisualizer.slnx` 932 passed, 0 failed;
  `dotnet format SpectrumVisualizer.slnx --verify-no-changes` exit 0.
- Item 6: the grep on `main.c` prints nothing; `main.c` calls `PcSenderActive` (`main.c:917`), which the `PcActive`
  pattern does not match.
- `pc_link.c`, `pc_link.h` and `pc_link_test.c` are UTF-8 with BOM and CRLF on every line, ASCII after the BOM.

### Task 6: [Final] Update documentation
Repositories: OptoskyProj (commit `docs(readme): описать протокол плата → ПК (#20)`, Russian) and SpectrumVisualizer
(commit `docs(protocol): point to the firmware golden frames`, English).

**Files:**
- Modify: OptoskyProj `README.md`
- Modify: SpectrumVisualizer `docs/architecture/board-protocol.md`

- [x] OptoskyProj `README.md`, in Russian: replace "отладка через ПК" at `README.md:29` with "кадры для
  SpectrumVisualizer"; replace the USARTC0 debug section at `README.md:226-230` with a "Протокол плата → ПК"
  section: the channel, a link to #20 for the frame, the send rules (spectrum after each measurement, dark frame as an
  all-zero spectrum, heartbeat after 3 s, `seq`), and `pc_link.c` as the code
- [x] OptoskyProj `README.md`, in Russian: a "Проверка на ПК" subsection with the host build commands of Acceptance
  Evidence item 1 (Developer PowerShell and gcc forms), the rule that a changed `pc_link.c` regenerates
  `FirmwareFrames.txt` in SpectrumVisualizer, and the drift check of item 2 with `--ignore-cr-at-eol`; add `pc_link.c/h` and `HostTest/` to the structure tree at
  `README.md:65-90`
- [x] SpectrumVisualizer `board-protocol.md`: a short paragraph after the heartbeat example (`:60-65`) naming
  `FirmwareFrames.txt` as bytes produced by the firmware's `pc_link.c` and `FirmwareFrameTests` as the check of the C
  byte order; the frame contract itself does not change
- [x] run `dotnet test SpectrumVisualizer.slnx --filter "Area=Protocol"` once more after the docs commit

## Post-Completion
*Items requiring manual intervention or external systems - no checkboxes, informational only*

**Manual verification:**
- Optional compile gate before Atmel Studio: `winget install ZakKemble.avr-gcc`, then from
  `OptoskyEncoder/OptoskyEncoder/` run `avr-gcc -mmcu=atxmega128a1u -Os -funsigned-char -fpack-struct -Wall -DNDEBUG
  -I SRC -c main.c pc_link.c SRC/*.c` and compare its warnings with the same run on `df5201f` (without `pc_link.c`).
  The cproj builds against the XMEGAA DFP through `-B`, so this gate is valid only if the toolchain's bundled avr-libc
  headers cover `atxmega128a1u`.
- Before flashing, save the current `Release/*.hex`: the repository no longer carries a `.hex` (PR #2), so the saved
  file is the only way back to the old firmware.
- Switch the board and the PC app together: the old WinForms app cannot read the new frames.
- The owner builds `OptoskyEncoder.atsln` in Atmel Studio (Release, `-Os`), checks the build has no new warnings and
  that flash and SRAM use fit the ATxmega128A1U, flashes the board, and runs the manual board checklist of Acceptance
  Evidence against the app: spectra on both devices, heartbeats, `seq` with its wrap, dark frames, the PLC wavelength
  change.

**PR notes:**
- The OptoskyProj PR body carries `Closes Semiteq/OptoskyProj#20`. No PR is created in this run.
- Reword some Russian commit messages in OptoskyProj before the PR:
  - "лесенка слов" (`001254f`) and "лестница слов" (`1440c90`) name the old frame differently from #20, which calls
    it "кадр с заголовком 01 FF…1E"; use the wording of #20.
  - "хук" (`001254f`, `f629773`, `47788bd`) becomes "функция".
  - "heartbeat" (`001254f`, `bc9e6ca`, `31d254b`) becomes "пульс", the word `f629773` already uses.
  - Passive forms ("считается", "читаются", "собирается", "добавлены", "убрано", "переведены" in `001254f`,
    `1440c90`, `505ee8a`, `f629773`) get an active verb with the actor named.
- The SpectrumVisualizer branch carries only the golden frames, their test and the doc paragraph; it ships with the
  app's stacked PRs.
- Merge: no PRs are open in OptoskyProj. A revived `fix/watchdog` or `fix/dma-fault-reset` branch conflicts in two
  lines around the `PcSenderInit` call after `sei()` (`main.c:1647`, `:1648`); `fix/encoder-i2c-timeout` touches `main.c:1367`.

**Owner items:**
- At merge, tag OptoskyProj and attach the built `.hex` to that release. The SpectrumVisualizer 3.0.0 release notes
  say "flash first, then install" and name that tag.
- Known gap for the PR body: nothing enforces golden-file drift automatically. OptoskyProj has no CI, and
  `FirmwareFrameTests` pass against the committed `FirmwareFrames.txt`.
- Board session prep: `winget install SQLite.SQLite` for the `sqlite3` CLI that the store query needs.
- RAMPZ check in the built `.lss` (the cproj enables it): look in the `TCC0_OVF_vect` and `USARTC0_RXC_vect`
  prologues for `in r0, 0x3b` and `out 0x3b, r1`. If they are absent, the far-read window under interrupts that
  existed before this change is real; the fix is `_far_mem_enter_ISR` (`SRC/ebi_driver.h:113`) in a separate issue.
- Step 4 note: an ATP-2000 frame measures about 358-360 ms, because service requests (`SERVICE_PERIOD_MS` 500,
  `main.c:152`) run during a frame. A value well above that is the starvation signal.

**Follow-up question for the owner:**
- The idle spectrometer's fault bit (`SpectrumFault[d]`, set at `main.c:501` and `:964`, cleared only by that device's
  next good spectrum at `:1032`) stays set while the PLC measures on the other device, so heartbeats keep reporting an
  old failure. Should the board clear it on a device switch or after a successful service request? The app shows only
  the measuring device's bit until then.

**For OptoskyProj#17:**
- `PixelCount()` in `pc_link.c` repeats `Pixels()` at `main.c:315`. It stays: `pc_link.c` must build without AVR
  headers. When #17 splits `main.c`, one pure module should own the pixel count and both callers should use it.

**Remaining slices:** release-3-0.

**Executed by exec:**
- branch: firmware-frame-sender

Run note: SpectrumVisualizer has 11 commits on `firmware-frame-sender` (`4e1d432`..`06d1c4f`) before this record,
stacked on `intensity-trend` at `2a4fc14`; every one of them touches this plan, and 8 touch nothing else. The others
add the golden frames and `FirmwareFrameTests` (`68e758e`), the `board-protocol.md` paragraph (`e606ec1`) and the
review fixes to both (`06d1c4f`). OptoskyProj has 7 local commits on `firmware-frame-sender` from `origin/master` at
`df5201f`, none pushed: `001254f` (frame generator and host program), `bc9e6ca` (send rules), `1440c90` (`main.c`
glue), `505ee8a` (README), `f629773` (review fixes to `pc_link` and the host check), `31d254b` (README fixes) and
`47788bd` (named `pc_link` constants). Review phase 1 (5 agents) found 0 critical, 6 major and about 21 minor
findings, fixed in OptoskyProj `f629773` and `31d254b` and SpectrumVisualizer `14e2996`. Its critical re-check found
the quality side clean and 5 major findings in the board checklist: unread state bits 2 and 3, the dark edge at boot,
the live PLC compare race, step 4 without an analyser, and step 9's stall bound. All 5 were fixed in `08ee148`.
Phase 2 (smells) found 10 minor findings and phase 3b (comments) 3 minor findings, fixed in OptoskyProj `47788bd`
and SpectrumVisualizer `06d1c4f`. Phase 3 (codex) was skipped because codex is not installed. Phase 4 (critical)
found quality and implementation clean. Final validation, 2026-11-05: the MSVC host build gives 0 warnings and
`all checks passed`, the drift check exits 0, the item 6 grep prints nothing, `dotnet test SpectrumVisualizer.slnx`
passes 932 of 932, `dotnet format --verify-no-changes` exits 0 and the Release build gives 0 warnings. The board
checklist was not run: this machine has no board and no AVR toolchain.

**Decisions made autonomously:**
- `pc_link.c` names its static frame start `BeginFrame`, because `main.c` already has the `Begin()` of the scope guard.
- OptoskyProj commits carry the `Co-Authored-By` trailer, against the caller's request: `a0e96bb` ends with it, and
  Context names it as the repo style.
- `PcSenderQueueSpectrum` overwrites a queued spectrum. `RequstAtp()` waits on `PcSenderBusy()`, so a second queue
  before the start does not happen.
- `PcSenderFinish` on an idle sender returns false and keeps the timer; `PcFrameNextByte` past the end returns 0. The
  glue reaches neither guard; both stay as safety nets.
- `Send2Aplication()` asks `PcSenderActive()`, because `PcSenderBusy()` is also true for a queued spectrum.
- `StartPcFrame` holds the frame start, because `Send2Aplication()` starts a frame twice per pass.
- The finish check runs in the pass of the last byte write; the old code checked `TXCIF` one pass later. All pixel
  bytes are read before the finish, so an early `TXCIF` cannot let a new spectrum overwrite SRAM under an unsent frame.
- `StartSend2Aplication` uses a designated initializer, so amplitude, SNR and Q cannot swap places.
- `main.c` keeps its CRLF line ends, its BOM and its missing final newline.
- The host check pins the trailers as wire bytes `AE 43` and `B0 2A`, the order `FirmwareFrames.txt` shows; the CRC
  values are `0x43AE` and `0x2AB0`.
- Acceptance Evidence item 4 took its swapped output from a scratch copy of `pc_link_test.c` without the `Failures`
  exit, because the committed program fails its own low-byte checks and writes no file. Nothing of it was committed.
- `FirmwareFrameTests` reads the golden file once into a static readonly dictionary. `FrameNames` is a collection
  expression, because `new(keys)` tripped IDE0028.
- The README puts "Протокол плата → ПК" under "Протокол связи", not under "Отладка", and does not call USARTC0 one-way,
  because the legacy receive path (`OnLegacyByte` channel 1) stays.
- The `board-protocol.md` paragraph follows the CRC definition, so the heartbeat example and its CRC stay together.
- Board checklist step 4 needs a logic analyser or an oscilloscope; without one the step is recorded as not checkable.
- The review fixes split OptoskyProj work into `fix(firmware)` and `docs(readme)` commits and type plan fixes
  `docs(plans)`. The old `SpectrumVisualizer.sln` was not rebuilt after them: no file it builds changed.
- The plan cites `main.c` by its line at HEAD, or marks the line "at df5201f".

## Verify it yourself

Automated, on branch `firmware-frame-sender` in both repositories:

1. In OptoskyProj, build and run the host check from Git Bash with the MSVC command of Acceptance Evidence item 1
   (`cl.exe -nologo -W4 -WX -std:c11 -I../OptoskyEncoder -Foout/ -Feout/pc_link_test.exe pc_link_test.c
   ../OptoskyEncoder/pc_link.c` from `OptoskyEncoder/HostTest`, with `INCLUDE` and `LIB` set), then
   `./out/pc_link_test.exe out/firmware-frames.txt`. It compiles with 0 warnings, prints `all checks passed` and
   exits 0.
2. The drift check of item 2:
   `git diff --no-index --exit-code --ignore-cr-at-eol C:/Users/admin/projects/OptoskyProj/OptoskyEncoder/HostTest/out/firmware-frames.txt
   C:/Users/admin/projects/SpectrumVisualizer/SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/Core/Protocol/FirmwareFrames.txt`
   exits 0.
3. In SpectrumVisualizer, `dotnet test SpectrumVisualizer.slnx --filter "FullyQualifiedName~FirmwareFrameTests"`
   passes 9 of 9; `dotnet test SpectrumVisualizer.slnx` passes 932 of 932.
4. In OptoskyProj, `grep -n "PcWord\|PcLowByte\|PcAmp\|PcActive" OptoskyEncoder/OptoskyEncoder/main.c` prints
   nothing, and `grep -n "pc_link" OptoskyEncoder/OptoskyEncoder/OptoskyEncoder.cproj` prints lines 142 and 145.

By hand: run the manual board checklist in Acceptance Evidence after Post-Completion's build and flash. The
highest-value steps:

1. Step 7: unplug the measuring spectrometer. Only its fault bit sets, then only its connected bit clears; this is
   the one check of the state bit order.
2. Steps 3 and 8: the store query shows `seq` rising by 1 per spectrum and wrapping 255 to 0, with the lost total at 0.
3. Step 3: after pulse mode stops, heartbeats arrive every 3 s and repeat the last non-zero `seq`.
4. Step 4: a logic analyser or an oscilloscope on USARTC0 TX shows an ATP-2000 frame in about 358 ms and an ATP-8000
   frame in about 91 ms.
5. Step 5: a dark request gives one spectrum with the next `seq`, `amplitude` and `snr` 0, and all pixels at 0.
