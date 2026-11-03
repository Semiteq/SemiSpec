# Architecture

English, present tense, the design as it is. Build and test commands live in the root `CLAUDE.md`.

## Documents

- [overview.md](./overview.md): purpose, stack, components, startup and the error route.
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
| Errors | FluentResults for failures; one route to the message panel; ReactiveUI's unhandled exceptions reach the same panel; an exception that escapes a dispatcher job ends the app and is logged as Fatal |
| Text | resx: English neutral set, Russian satellite; the UI culture follows Windows |
| Instances | One window per process; a second running copy is allowed |
| Tests | One `Tests.Unit` project: xUnit 3.2.2, AwesomeAssertions, Avalonia.Headless. No integration project until a test needs Docker or a real port |
| Chart renderer | ScottPlot 5 |
| Serial library | The serial link will use `RJCP.SerialPortStream` 3.0.5 with one cancellable read loop; no code reads a port yet. `System.IO.Ports` is rejected: its `ReadAsync` ignores the cancellation token on Windows and reopening after an unplug fails |
| Board protocol | The app will read only the board-to-PC frame specified in Semiteq/OptoskyProj#20 (sync, length, CRC-16/MODBUS, sequence counter, heartbeat) and stay read-only toward the board; no frame decoder exists yet |
| Spectrum storage | SQLite (`Microsoft.Data.Sqlite`), a file per running instance under the local app data folder, wiped at start, 24 h retention |
| Installer | Inno Setup over a self-contained win-x64 publish folder; no single-file publish |
