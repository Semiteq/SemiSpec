# Architecture

English, present tense, the design as it is. Build and test commands live in the root `CLAUDE.md`.

## Documents

- [overview.md](./overview.md): purpose, stack, components, startup and the error route.
- [board-protocol.md](./board-protocol.md): the board-to-PC frame, the decoder, the `seq` rules, the serial link,
  the wavelength calibration and the board emulator.
- [testing-strategy.md](./testing-strategy.md): the test project, what each kind of test owns, the headless setup.
- [ui-theme.md](./ui-theme.md): the colours, the palette keys, the include manifest and the inset scale.
- [ui-text.md](./ui-text.md): the English and Russian text sets and how the UI culture picks one.

## Decision records

None.

## Locked decisions

| Area | Decision |
| --- | --- |
| Platform | .NET 10 (`net10.0`), C# 14, Windows only |
| Desktop shell | Avalonia 12 (Win32, Skia, HarfBuzz), ReactiveUI MVVM, Semi.Avalonia theme with the palette shared with SemiPlot |
| Conventions | SemiPlot (`Semiteq/SemiPlot`) is the reference app: same package pins, build gates, folder shape and error route |
| Layout | New projects under `SpectrumVisualizer/` with their own `Directory.Build.props`, `Directory.Packages.props` and `.editorconfig`; `SpectrumVisualizer.slnx` at the root lists them. The old WinForms app stays at the root under its own settings |
| Startup | No launch keys and no config files: every site runs the same setup |
| Logging | Serilog to `%LOCALAPPDATA%\SpectrumVisualizer\Logs`; the operator raises the level to Debug from the menu at run time; the level is not persisted |
| Errors | FluentResults for failures; one route to the message panel, with an Info, Warning or Error severity per entry; ReactiveUI's unhandled exceptions and exceptions that escape a dispatcher job reach the same panel and the app keeps running; before the panel exists an escaped exception ends the app and is logged as Fatal |
| Text | resx: English neutral set, Russian satellite; the UI culture follows Windows |
| Instances | One window per process; a second running copy is allowed |
| Tests | One `Tests.Unit` project: xUnit 3.2.2, AwesomeAssertions, Avalonia.Headless. No integration project until a test needs Docker or a real port |
| Chart renderer | ScottPlot 5 |
| Serial library | The chosen library is `RJCP.SerialPortStream` 3.0.5 with one cancellable read loop; the app reads the board's port with it at 115200 baud, 8N1, a 1 s read timeout and `ThrowOnReadError`, and the board emulator writes a port with it. `System.IO.Ports` is rejected: its `ReadAsync` ignores the cancellation token on Windows and reopening after an unplug fails |
| Board protocol | The chosen frame is the board-to-PC frame specified in Semiteq/OptoskyProj#20 (sync, length, CRC-16/MODBUS, sequence counter, heartbeat), the only format the app accepts; the app is read-only toward the board. Core decodes it from a `PipeReader` (`board-protocol.md`) |
| Serial link | One session task per Connect reads, decodes and judges the link; decoded frames leave through a bounded channel of 64 with `DropOldest`; a lost port or a failed open is retried every 1 s until the operator disconnects; the 10 s health rules of OptoskyProj#20 give NotResponding and OtherDevice, and a stalled decoder is drained instead of reported (`board-protocol.md`) |
| Tools | Console tools live under `SpectrumVisualizer/` as `SpectrumVisualizer.Tools.*` on `System.CommandLine` 2.0.2 |
| Spectrum storage | SQLite (`Microsoft.Data.Sqlite`), a file per running instance under the local app data folder, wiped at start, 24 h retention |
| Installer | Inno Setup over a self-contained win-x64 publish folder; no single-file publish |
