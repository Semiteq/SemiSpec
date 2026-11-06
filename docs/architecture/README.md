# Architecture

English, present tense, the design as it is. Build and test commands live in the root `CLAUDE.md`.

## Documents

- [overview.md](./overview.md): purpose, stack, components, startup, the main window, the spectrum screen, the
  trend, the spectrum store, the error route and the release.
- [board-protocol.md](./board-protocol.md): the board-to-PC frame, the decoder, the `seq` rules, the serial link,
  the wavelength calibration and the board emulator.
- [testing-strategy.md](./testing-strategy.md): the test project, what each kind of test owns, the headless setup.
- [ui-theme.md](./ui-theme.md): the colours, the palette keys, the include manifest, the inset scale and the plot's
  colours.
- [ui-text.md](./ui-text.md): the English and Russian text sets and how the UI culture picks one.

## Decision records

None.

## Locked decisions

| Area | Decision |
| --- | --- |
| Platform | .NET 10 (`net10.0`), C# 14, Windows only |
| Desktop shell | Avalonia 12 (Win32, Skia, HarfBuzz), ReactiveUI MVVM, Semi.Avalonia theme with the palette shared with SemiPlot |
| Conventions | SemiPlot (`Semiteq/SemiPlot`) is the reference app: same package pins, build gates, folder shape and error route |
| Layout | The projects under `SpectrumVisualizer/` with their own `Directory.Build.props`, `Directory.Packages.props` and `.editorconfig`; `SpectrumVisualizer.slnx` at the root lists them; the Inno Setup script sits in `Installer/` |
| Startup | No launch keys and no config files: every site runs the same setup |
| Logging | Serilog to `%LOCALAPPDATA%\SpectrumVisualizer\Logs`; the operator raises the level to Debug from the menu at run time; the level is not persisted |
| Errors | FluentResults for failures; one route to the message panel, with an Info, Warning or Error severity per entry; ReactiveUI's unhandled exceptions and exceptions that escape a dispatcher job reach the same panel and the app keeps running; before the panel exists an escaped exception ends the app and is logged as Fatal |
| Text | resx: English neutral set, Russian satellite; the UI culture follows Windows |
| Instances | One window per process; a second running copy is allowed |
| Tests | One `Tests.Unit` project: xUnit 3.2.2, AwesomeAssertions, Avalonia.Headless. No integration project until a test needs Docker or a real port |
| Chart renderer | ScottPlot.Avalonia 5.1.59; the plot sits alone in a decorator with a `BitmapCache` and redraws at most once per display frame, as in SemiPlot |
| Frame distribution | `FrameDistributor` is the one reader of the link's channel; it keeps the latest spectrum frame and forwards every frame to its subscribers (the live spectrum source, the spectrum store and the trend feed), which own their scheduling (`overview.md`) |
| Serial library | The chosen library is `RJCP.SerialPortStream` 3.0.5 with one cancellable read loop; the app reads the board's port with it at 115200 baud, 8N1, a 1 s read timeout and `ThrowOnReadError`, and the board emulator writes a port with it. `System.IO.Ports` is rejected: its `ReadAsync` ignores the cancellation token on Windows and reopening after an unplug fails |
| Board protocol | The chosen frame is the board-to-PC frame specified in Semiteq/OptoskyProj#20 (sync, length, CRC-16/MODBUS, sequence counter, heartbeat), the only format the app accepts; the app is read-only toward the board. Core decodes it from a `PipeReader` (`board-protocol.md`) |
| Serial link | One session task per Connect reads, decodes and judges the link; decoded frames leave with their receive time through a bounded channel of 64 with `DropOldest`; a lost port or a failed open is retried every 1 s until the operator disconnects; the 10 s health rules of OptoskyProj#20 give NotResponding and OtherDevice, and a stalled decoder is drained instead of reported (`board-protocol.md`) |
| Tools | Console tools live under `SpectrumVisualizer/` as `SpectrumVisualizer.Tools.*` on `System.CommandLine` 2.0.2: the board emulator writes protocol frames to a port, the store benchmark measures the store file |
| Spectrum storage | SQLite (`Microsoft.Data.Sqlite`), one file `spectra-<process id>.db` per running instance under `%LOCALAPPDATA%\SpectrumVisualizer\Spectra`; the own file is wiped at start, and the sweep deletes every other file no live instance holds open; WAL with `synchronous=NORMAL` and a 64 MB journal limit; one background writer drains a bounded queue in batched transactions and deletes rows older than 24 h; the intensity series reads each row's window from its full pixels with `substr` in one statement (`overview.md`, Spectrum store) |
| Trend | The intensity at the key (wavelength and `delta`) of the latest spectrum frame, one point per spectrum, in an in-memory cache of the last 24 h fed by the distributor; the chart and the overview fold the cache on the store's epoch grid; the store is re-read only on a key change, the visible window first, then the rest of the day in hour chunks, cancelled by a newer key change; more than 10 s without any frame resets the `seq` baseline, and more than 10 s without a spectrum breaks the line; breaks and failure marks belong to the run and survive a key change; a click shows that moment's spectrum until Back to live (`overview.md`, Trend) |
| Installer | Inno Setup over a self-contained win-x64 publish folder; no single-file publish. The script keeps the `AppId` of the 2.x installer (`overview.md`, Release) |
