# Architecture

English, present tense, the design as it is. Build and test commands live in the root `CLAUDE.md`.

## Documents

- [overview.md](./overview.md): purpose, stack, components, startup and the error route.
- [testing-strategy.md](./testing-strategy.md): the test project, what each kind of test owns, the headless setup.
- [ui-theme.md](./ui-theme.md): the colours, the palette keys, the style files and the inset scale.
- [ui-text.md](./ui-text.md): the English and Russian text sets and how the UI culture picks one.

## Decision records

None.

## Locked decisions

| Area | Decision |
| --- | --- |
| Platform | .NET 10 (`net10.0`), C# 14, Windows only |
| Desktop shell | Avalonia 12 (Win32, Skia, HarfBuzz), ReactiveUI MVVM, Semi.Avalonia theme with the palette shared with SemiPlot |
| Conventions | SemiPlot is the reference app: same package pins, build gates, folder shape and error route |
| Layout | New projects under `SpectrumVisualizer/` with their own `Directory.Build.props`, `Directory.Packages.props` and `.editorconfig`; `SpectrumVisualizer.slnx` at the root lists them. The old WinForms app stays at the root under its own settings |
| Startup | No launch keys and no config files: every site runs the same setup |
| Logging | Serilog to `%LOCALAPPDATA%\SpectrumVisualizer\Logs`; the operator raises the level to Debug from the menu at run time; the level is not persisted |
| Errors | FluentResults for failures; one route to the message panel; ReactiveUI's unhandled exceptions reach the same panel |
| Text | resx: English neutral set, Russian satellite; the UI culture follows Windows |
| Instances | One window per process; a second running copy is allowed |
| Tests | One `Tests.Unit` project: xUnit 3.2.2, AwesomeAssertions, Avalonia.Headless. No integration project until a test needs Docker or a real port |
| Chart renderer | ScottPlot 5 |
| Serial library | `RJCP.SerialPortStream` 3.0.5 with one cancellable read loop. `System.IO.Ports` is rejected: its `ReadAsync` ignores the cancellation token on Windows and reopening after an unplug fails |
| Board protocol | The board-to-PC frame specified in Semiteq/OptoskyProj#20: sync, length, CRC-16/MODBUS, sequence counter, heartbeat. The app is read-only toward the board and reads no other frame format |
| Spectrum storage | SQLite (`Microsoft.Data.Sqlite`), a file per running instance under the local app data folder, wiped at start, 24 h retention |
| Installer | Inno Setup over a self-contained win-x64 publish folder; no single-file publish |
