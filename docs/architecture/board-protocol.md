# Board protocol

The board sends frames to the PC over a serial port, one way only; the app sends nothing. The contract is
Semiteq/OptoskyProj#20. This page restates it in English and describes the Core code that reads it
(`SpectrumVisualizer.Core/Protocol/`, `SpectrumVisualizer.Core/Calibration/`) and the board emulator
(`SpectrumVisualizer.Tools.BoardEmulator`). Core holds no serial port code.

## Channel

USB-UART adapter, 115200 baud, 8N1. Multi-byte fields are little-endian. Floats are IEEE 754 single precision.

## Frame

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 2 | sync `AA 55` |
| 2 | 2 | `len`, u16: bytes from `state` to the end of the measurement part |
| 4 | 1 | `state`, u8 |
| 5 | 1 | `seq`, u8: measurement counter |
| 6 | 16 + 2·P | measurement part, spectrum frames only |
| 6 + N | 2 | CRC-16/MODBUS over `len` through the end of the measurement part, low byte first |

| Kind | `len` | Frame size | The board sends it |
| --- | --- | --- | --- |
| ATP-2000 spectrum (P = 2048) | 4114 | 4120 bytes | after each ATP-2000 measurement |
| ATP-8000 spectrum (P = 512) | 1042 | 1048 bytes | after each ATP-8000 measurement |
| Heartbeat | 2 | 8 bytes | when nothing was sent for 3 s |

No other `len` is valid. The frame kind is decided by `len` only.

`state` bits:

| Bit | Meaning |
| --- | --- |
| 0 | ATP-2000 connected |
| 1 | ATP-8000 connected |
| 2 | last ATP-2000 measurement failed |
| 3 | last ATP-8000 measurement failed |
| 4-7 | reserved, sent as zero; the decoder masks them off, so `BoardState` carries bits 0-3 only |

The board clears a spectrometer's failure bit on its next good measurement, before it sends that spectrum. A spectrum
frame therefore always has its own spectrometer's failure bit at 0; the app sees the bit in a heartbeat or in the other
spectrometer's frames.

Measurement part:

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 1 | `device`: 1 = ATP-2000, 2 = ATP-8000; sets P |
| 1 | 2 | `wavelength`, u16, nm, 189-2460: the wavelength the spectrum was taken for |
| 3 | 1 | `delta`, u8, 0-100: half-width of the window around `wavelength` |
| 4 | 4 | `amplitude`, f32: mean over the window, the value the PLC receives |
| 8 | 4 | `snr`, f32: signal-to-noise as a ratio, not dB |
| 12 | 4 | `q`, f32: quality factor, the value the PLC receives |
| 16 | 2·P | `pixels`: u16 per pixel from pixel 0, dark spectrum subtracted, never below zero |

The board also sends its dark measurement as an ordinary spectrum frame: every pixel, `amplitude` and `snr` are zero.

Example heartbeat, both spectrometers connected, no failures, last measurement 42:

```
AA 55  02 00  03  2A  80 B3
sync   len    st  seq crc16
```

CRC-16/MODBUS: reflected polynomial 0xA001, initial value 0xFFFF, no final XOR (`Crc16Modbus`). ASCII `123456789`
gives `0x4B37`.

## Model

`BoardFrame` holds `State`, `Seq` and a `SpectrumMeasurement` that is null for a heartbeat. `SpectrumMeasurement`'s
constructor rejects a pixel array whose length does not match its `Spectrometer`, so every measurement in the app has
a valid size. `FrameEncoder.Encode` builds the bytes of a frame; the emulator and the tests use it.

## Decoder

`FrameDecoder.TryReadFrame(ref ReadOnlySequence<byte>, out BoardFrame?, out long discarded)` applies the receive
rules of OptoskyProj#20:

1. Find `AA 55`.
2. Read `len`. If it is not 2, 4114 or 1042, drop one byte and go to step 1.
3. Wait for the whole frame and check the CRC. On a mismatch, drop one byte and go to step 1.
4. In a spectrum frame, check that `device` matches `len`. On a mismatch, drop one byte and go to step 1.
5. Return the frame and slice it off the buffer.

A failed candidate always costs one byte, never a whole frame, so a false `AA 55` inside pixel data cannot hide the
real next frame. `TryReadFrame` keeps scanning inside one call; `false` means only that a whole frame is not buffered
yet. Every dropped byte is sliced off the buffer and added to `discarded`; a lone trailing `AA` stays in the buffer,
because the next read may complete the sync.

`FrameReader.ReadAllAsync(PipeReader, CancellationToken)` runs the decoder over a pipe. It yields every whole frame in
the buffer before it reads again, marks the rest of the buffer examined so the next read waits for new bytes, ends when
the writer completes, and surfaces cancellation as `OperationCanceledException`. Malformed input never throws; it adds
to `DiscardedBytes`, a running count that any thread may read. The bytes of a partial frame left when the writer
completes are added to `DiscardedBytes` too.

A false candidate whose `len` reads as a valid value stalls the decoder: it waits for the whole frame before the CRC
can fail. Three inputs produce one: a corrupted header that keeps a valid `len`, a false `AA 55` followed by a valid
`len` inside pixel data, and a stream opened in the middle of a frame. The wait is a byte count, not a time: up to
4120 bytes (`len` 4114) or 1048 bytes (`len` 1042). How long it takes depends on the traffic behind the candidate:

| Traffic | Wait |
| --- | --- |
| spectra of the same kind flowing | about one frame period |
| ATP-8000 spectra at 1-1.3 per second, behind a 4114 candidate | about 3-4 s |
| heartbeats only, one 8-byte frame every 3 s | about 26 min (`len` 4114), about 6.5 min (`len` 1042) |

No frame is lost, because the bytes stay in the buffer: after the CRC fails, the decoder drops one byte and finds the
real frames behind it, and they arrive late in one burst. A wait over 10 s trips the "no valid frame" status of
OptoskyProj#20. Recovery belongs to the serial-link slice, which owns the port: when that status fires, it drops the
decoder's buffer.

## Sequence counter

The board increments `seq` before each spectrum frame, so the first spectrum after start carries 1. A heartbeat repeats
the last spectrum's `seq`, 0 before any spectrum. After 255 comes 0.

`SequenceTracker.Observe(BoardFrame)` returns the number of spectra lost:

| Frame | Step `(seq - last) mod 256` | Lost |
| --- | --- | --- |
| first frame seen, any kind | none | 0, the frame becomes the baseline |
| spectrum | 1 | 0 |
| spectrum | k > 1 | k - 1 |
| spectrum | 0 | 256 |
| heartbeat | 0 | 0 |
| heartbeat | k > 0 | k |

The first frame is the baseline, so an app started mid-run reports no false loss, even when that frame carries 57. A
heartbeat reports k, not k - 1: it repeats the last spectrum the board sent, so every spectrum up to and including
that `seq` was lost. Every frame, heartbeats included, becomes the new baseline, so the spectrum after a heartbeat
that reported a loss does not report it again. `Reset()` starts a new baseline, for a reconnect.

A board restart looks like an ordinary gap. A loss of 256 or more spectra in a row miscounts; at 1.3 spectra per second
that is over 3 minutes of silence. OptoskyProj#20 has the app report a silent board after 10 s without a valid frame,
long before that.

## Calibration

`SpectrometerCalibration.For(Spectrometer)` holds each device's pixel-to-wavelength polynomial,
λ(p) = c0 + c1·p + c2·p² + c3·p³:

| Device | c0 | c1 | c2 | c3 | Table range |
| --- | --- | --- | --- | --- | --- |
| ATP-2000 | 185.89856 | 0.390762 | 1.126E-5 | -8.969E-10 | 189-1028 nm |
| ATP-8000 | 895.373169 | 3.00962138 | 9.568E-5 | 4.519E-8 | 898-2461 nm |

The board's firmware uses the same coefficients for its pixel tables. `WavelengthOf(pixel)` reads a wavelength computed
once per pixel.

`PixelFor(nm)` follows the firmware's `PixelForWave()`: it clamps the wavelength to the table range, then returns the
nearest pixel; a tie goes to the lower pixel. The ATP-2000 result equals the firmware table in every entry; the
ATP-8000 result is within one pixel of it. ATP-8000 pixel 0 lies at 895.37 nm, below the 898 nm table start, so no
wavelength maps to it.

`Window(nm, delta)` returns the inclusive `PixelWindow` from `PixelFor(max(nm - delta, 0))` to `PixelFor(nm + delta)`,
with `delta` in nm (OptoskyProj#7). Clamping in `PixelFor` keeps both ends inside 0…P-1. The board builds its noise
window the same way, but its Amplitude window is ±`delta` pixels around `PixelForWave(wavelength)`, so the app's and
the board's amplitudes differ on ATP-2000. OptoskyProj#7 tracks moving the board's Amplitude window to nm.

## Board emulator

`SpectrumVisualizer.Tools.BoardEmulator` writes protocol frames to a COM port, for manual runs over crossed USB-UART
adapters or a com0com pair:

```powershell
dotnet run --project SpectrumVisualizer/SpectrumVisualizer.Tools.BoardEmulator -- --port COM5
dotnet run --project SpectrumVisualizer/SpectrumVisualizer.Tools.BoardEmulator -- --help
```

`--help` lists the options and their defaults. The port runs at 115200 baud, fixed. An ATP-2000 frame takes
about 0.36 s on the wire, so the port caps `--rate` near 2.8 for ATP-2000. A failed write, for example an unplugged
adapter, prints one line and exits with code 1.

`SpectrumGenerator` builds the spectrum once: a Gaussian peak at the wavelength (σ = max(`delta`, 1)/2 nm, height
30000) over a flat floor of 1000. The frame carries `amplitude` 30000, `snr` 30 and `q` 0. `state` has only the
emulated spectrometer's connected bit set.

`FrameSchedule` decides, on a 10 ms tick, which frame is due: a spectrum at the rate, or a heartbeat after 3 s with
nothing sent. `seq` follows the board's rules. A late tick sends one spectrum and drops the backlog instead of sending
a burst. A frame with a corrupted CRC still consumes its `seq`, as a frame corrupted on the wire does. Ctrl+C stops
the tool.
