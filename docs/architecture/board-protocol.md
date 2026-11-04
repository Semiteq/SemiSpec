# Board protocol

The board sends frames to the PC over a serial port, one way only; the app sends nothing. The contract is
Semiteq/OptoskyProj#20. This page restates it in English and describes the Core code that reads it
(`SpectrumVisualizer.Core/Protocol/`, `SpectrumVisualizer.Core/Calibration/`), the serial link that carries it into
the app (`SpectrumVisualizer.UI/Link/`) and the board emulator (`SpectrumVisualizer.Tools.BoardEmulator`). Core holds
no serial port code.

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
`Spectrometer.FrameSize` gives the bytes of a device's spectrum frame on the wire: 4120 for ATP-2000, 1048 for
ATP-8000.

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
to `DiscardedBytes`, a running count that any thread may read. When the writer completes, no candidate can grow any
more: the reader drops one byte at each pending candidate and decodes again, so a false candidate cannot hide the whole
frames behind it. Only the bytes that belong to no frame are added to `DiscardedBytes`.

`FrameReader.DecodeBuffered(PipeReader, ICollection<BoardFrame>)` is the same decode step without a wait, for a
caller that owns its own read loop. It calls `TryRead`, adds every whole frame to the collection, runs the drop-one-byte
pass when the writer has completed and calls `AdvanceTo(consumed, end)`. When `TryRead` finds nothing it leaves the
pipe as it is.

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
real frames behind it, and they arrive late in one burst. A wait over 10 s would trip the "no valid frame" status of
OptoskyProj#20, so the serial link drains the decoder 10 s after the last frame when bytes arrived since it: it
completes the pipe's writer, which makes the stalled candidate invalid and releases the frames behind it, and starts a
fresh pipe (see the drain under "Serial link").

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

## Serial link

`SerialLink` (`SpectrumVisualizer.UI/Link/`) carries the board's bytes from a COM port to decoded frames. It holds the
public surface: `Connect(port)`, `DisconnectAsync()`, `Dispose()`, `Status`, `StatusChanged`, `StatusUpdates(scheduler)`
and `Frames`. `StatusUpdates` is the current status followed by every change, observed on the given scheduler; the
status bar and the connection row both follow the link through it. Each Connect creates one `LinkSession` and runs it as
one task. That task opens the port, reads, writes the pipe, decodes, feeds `SequenceTracker`, writes the channel, judges
the health, drains, reopens and publishes every status. No second task runs and no lock exists: each piece of session
state has one writer, and `Status` is published with `Volatile.Write`.

### Port

`ISerialPortProvider` (`PortNames()` returning `Result<IReadOnlyList<string>>`, `Open(port)` returning
`Result<Stream>`) is the seam the tests replace. Neither member throws.
`RjcpSerialPortProvider` is the production implementation over `RJCP.SerialPortStream`:

- `PortNames()` asks an unopened `SerialPortStream` for the names and sorts them by the number after `COM`
  (`COM2` before `COM10`), then any other name by ordinal. A listing that throws is logged at Warning and returned as
  a failed result. The connection row then shows an empty list and reports a `PortListError` to the message panel.
- `Open` checks the name against `PortNames()` first, so a name missing from a listing that worked returns
  `PortNotFoundError` without touching the driver. A failed listing is no proof that the port is gone, the same rule
  the read loop applies, so `Open` goes ahead and tries the port. It opens at 115200
  baud, 8N1, with `ReadTimeout` 1000 ms and `ThrowOnReadError` on. The `SerialPortStream` constructor can throw as
  well as `Open()`, so both sit inside one `try`.
- `Open` never throws: every exception except `OperationCanceledException` becomes a link error, so the session keeps
  retrying. `UnauthorizedAccessException` and an `IOException` with `HResult` 32 (sharing violation) map to
  `PortBusyError`; any other exception maps to `PortNotFoundError` and rides along as its cause (an
  `ExceptionalError` reason).

### Read loop

Each pass takes `GetMemory(4096)` from the pipe's writer, awaits `ReadAsync(memory, token)`, then `Advance`,
`FlushAsync` and `DecodeBuffered` on the pipe's reader. A read timeout (`TimeoutException`) is a 1 s tick with no
bytes. After each read or tick the loop judges the health, drains when due and publishes the status, so a status comes
at least once a second with no timer. The loop reads the session's stopwatch once per pass and stamps the bytes and the
frames of that pass with the elapsed time. The stopwatch is `scheduler.StartStopwatch()`, started when the session is
created: `DefaultScheduler` gives a monotonic `Stopwatch`, and a `TestScheduler` gives virtual time. A wall clock step
therefore neither fakes a silence nor hides one, and the health rules compare elapsed times only.

Each decoded frame is observed by the tracker, written to the channel and sets the last-frame time; the count of bytes
since the last frame resets to 0, so bytes that trail a frame in the same read are not counted.

The pipe uses `pauseWriterThreshold` 65536, `resumeWriterThreshold` 32768 and no synchronization context. The loop
decodes after every flush and the decoder accepts only the three valid lengths, so the pipe holds at most one stalled
candidate of 4120 bytes plus the bytes behind it, well under the pause threshold. Every teardown (drain, port loss,
disconnect) completes both the writer and the reader, so the pipe returns its pooled segments.

### Health

`LinkHealth.Evaluate(now, openedAt, lastByteAt, lastFrameAt, bytesSinceFrame)` is a pure function of the loop's
timestamps. The reference time is the last valid frame, or the port open before the first frame:

- Under 10 s since the reference: Waiting before the first frame, Receiving after it.
- From 10 s, inclusive: OtherDevice when a byte arrived in the last 10 s and more than 4120 bytes
  (`Spectrometer.Atp2000.FrameSize`, the largest frame) arrived since the last frame; NotResponding otherwise.

The byte rule keeps a board that stops mid-frame at NotResponding: its tail is shorter than one frame. The clock starts
at port open, so a dead port shows NotResponding 10 s after Connect.

The byte rule has a side effect on a device that sends no valid frame at under about 412 B/s (4120 bytes in 10 s): the
link first reads NotResponding, then OtherDevice once more than 4120 bytes have arrived since the last frame, and each
entry adds its own Warning to the panel. The rule stays: telling a slow foreign sender from a stopped board would need
a second threshold with no field data behind it.

### Drain

`LinkHealth.IsDrainDue` is true when 10 s have passed since the later of the reference and the previous drain, and a
byte arrived after the reference. A tail that arrives in the same read as a frame shares its stamp, so silence after
it does not drain that tail; a tail from a later read is drained 10 s after the frame, when the link already reads
NotResponding. A drain completes the writer, calls `DecodeBuffered` once more, which releases the
frames behind a stalled candidate, completes the reader and starts a fresh pipe. The loop drains before it judges the
health, so a stall that hid real frames never shows NotResponding or OtherDevice. A drain that releases nothing keeps
the byte count, so OtherDevice stays reachable after it.

A drain has three side effects:

- The partial frame in flight is discarded.
- The fresh pipe starts mid-frame, so the decoder discards the rest of that frame as garbage.
- The frames a drain releases are stamped with the drain time, not their arrival time.

### Port loss and reopen

Four signals mean the open port is lost:

- A read returns 0 bytes: RJCP wakes a pending read this way when its monitor thread dies.
- A read throws `InvalidOperationException`: RJCP's closed-port check under `ThrowOnReadError`.
- A read throws `IOException`.
- A read timeout comes while `PortNames()` no longer lists the port. Some drivers abort the pending read in a way RJCP
  treats as benign, and only read timeouts keep coming. A failed `PortNames()` result counts as "still listed": a port
  list that cannot be read is no proof of a loss.

On a loss the session arms the 1 s reopen timer, publishes PortUnavailable, then tears the pipe down and disposes the
stream, so a driver that blocks in dispose cannot hold the status bar at its last state. A failed open arms the timer
and publishes the same way. After 1 s on the link's `IScheduler` the session opens again, until Disconnect. The wait is
a `TaskCompletionSource` with `RunContinuationsAsynchronously`, completed by `scheduler.Schedule(1 s, ...)` and
cancelled through the session token; it checks the token before it publishes, so a Disconnect that races a loss
publishes no PortUnavailable. The pipe teardown and the dispose are nested `try`/`finally`, so a pipe failure cannot
leak the port. Every open logs at Debug with the port name, resets `SequenceTracker` and starts a fresh pipe and
`FrameReader`, so the gap of an unplug does not count as lost spectra.

### Status

`LinkStatus(State, PortName, Board, LostSpectra, Fault)` is what the link publishes. `Board` is the `state` byte of the
latest frame; `LostSpectra` is the total since the operator's Connect, kept across reopens. Each loss logs at
Information with the `seq` value. The session publishes Connecting first and Disconnected last. `StatusChanged` is
raised on a thread-pool thread of the session task, never inside `Connect` or `DisconnectAsync`, and only when the
status differs from the last one with `Fault` left out of the comparison: a fault rides a state change.

| State | Enters when | Fault on entry |
| --- | --- | --- |
| Disconnected | start; Disconnect; an unexpected exception | `ExceptionalError` for an exception |
| Connecting | the session task starts | none |
| PortUnavailable | an open failed or the port was lost; retried every 1 s | the first of a run: `PortBusyError`, `PortNotFoundError` or `PortLostError` |
| Waiting | the port opened, no frame yet, under 10 s | none |
| Receiving | a valid frame under 10 s ago | none |
| NotResponding | 10 s with no frame; no byte in the last 10 s, or at most 4120 bytes since the last frame | `BoardNotRespondingError` |
| OtherDevice | 10 s with no frame, a byte in the last 10 s and over 4120 bytes since the last frame | `UnrecognisedDataError` |

A run lasts from one Receiving to the next: only its first failed open or port loss carries a fault, so retries that
fail the same way stay quiet and the panel gets one entry per incident. An exception anywhere else in the session
tears the pipe down, closes the port and publishes Disconnected with an `ExceptionalError`.

The five link errors share the abstract `LinkError` (`LinkErrors.cs`), which puts the port name in the message, and so
in the log line. The message panel's view of a link error leaves the port
out, so a repeat on another port coalesces into one row.

### Frames out

`Frames` is a `ChannelReader<BoardFrame>` over a bounded channel of 64 frames with `DropOldest`, one writer and one
reader. 64 frames are about 50 s of ATP-8000 spectra at 1.3 per second. `Wait` would let a slow reader block the read
loop and trip the 10 s status on a healthy board. A reader that falls behind drops many frames in a row, so only the
first drop of a session logs at Debug. No code reads the channel, so it fills and drops the oldest frame.

### Stop

`DisconnectAsync()` awaits `CancelAsync()` on the session token, so the token's callbacks and the session's tail run on
the thread pool rather than on the caller's thread, then awaits the session task, which completes after the session
closed the port and published Disconnected. With no session running it completes at once. `ReadAsync` honours the
token, and the 1 s read timeout bounds the wait even if a driver did not. `Dispose()` cancels without waiting, for the
container's synchronous dispose. `Connect` does nothing while a session runs, including the moment after Disconnected
is published and before the session task completes; it logs that ignored Connect at Debug.

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
30000) over a flat floor of 1000. The frame carries `amplitude` 30000, `snr` 30 and `q` 0.
`SpectrumGenerator.Dark` builds the dark spectrum: every pixel, `amplitude` and `snr` zero.

Three options shape `state` and the dark frames:

| Option | Effect |
| --- | --- |
| `--both-connected` | Sets both connected bits; without it only the emulated spectrometer's bit is set. Spectra still come from `--device` only |
| `--failed 2000\|8000` | Sets that spectrometer's measurement-failed bit. The emulated spectrometer's own bit is cleared in its spectrum frames, as the board does, so it shows in heartbeats only. Naming the other spectrometer requires `--both-connected`, or the command fails with a line naming both options |
| `--dark-every <n>` | Sends every nth spectrum as the dark spectrum; 0, the default, never. A separate spectrum counter decides it, because `seq` wraps at 255; the dark frame consumes its `seq` like any spectrum |

The cross-option rule is a validator on the root command. It checks the raw tokens before it reads the values, because
`System.CommandLine` runs command validators before the option values convert.

`FrameSchedule` decides, on a 10 ms tick, which frame is due: a spectrum at the rate, or a heartbeat after 3 s with
nothing sent. `seq` follows the board's rules. A late tick sends one spectrum and drops the backlog instead of sending
a burst. A frame with a corrupted CRC still consumes its `seq`, as a frame corrupted on the wire does. Ctrl+C stops
the tool.
