# UI text

Every string the operator reads lives in `SemiSpec/SemiSpec.UI/Localization/Resources.resx`
(English) and `Resources.ru.resx` (Russian). C# source stays ASCII: the `terse` gate in the pre-commit hook and in CI
rejects any other character, so Cyrillic and typographic glyphs live only in the resx.

## Two sets, one selector

`SemiSpec.UI.csproj` sets `<NeutralLanguage>en</NeutralLanguage>`, so the English set is the neutral one and
lives in the main assembly. `Resources.ru.resx` carries the same keys and compiles to a `ru` satellite assembly; the SDK
takes the culture from the file name.

The app sets no culture. The generated accessor reads `ResourceManager.GetString(key, Culture)` with `Resources.Culture`
left null, which falls through to `CultureInfo.CurrentUICulture`, the Windows UI language. A Russian Windows shows the
Russian set; any other language shows English. Semi's own control strings follow the same culture (`ui-theme.md`).

Numbers and times follow `CultureInfo.CurrentCulture`: the readout pane's amplitude, SNR and Q in `G6`, the log
axis's tick labels in `"0.###"`, the axis flyouts' bounds in `"0.###"`, the trend readout's values in `G6` and its
counts, the numbers in the preset labels and the trend's width text, and the date in the trend's time axis title. These outputs are the exception. `AppLogging` writes the log file with the invariant culture, so every machine
produces the same log format. The readout pane's update time is the fixed `HH:mm:ss.fff`; the trend readout's time, the
times in the spectrum's state line and the time flyout's fields are the fixed `HH:mm:ss` in local time; the trend's
time ticks are `HH:mm:ss` or `HH:mm`; all are formatted with the invariant culture. ScottPlot's `DateTimeAutomatic`
places the trend's time ticks.

## The generated accessor

`Microsoft.CodeAnalysis.ResxSourceGenerator`, pinned in `SemiSpec/Directory.Packages.props` and referenced with
`PrivateAssets="all"`, generates the `Resources` class from one item:

```xml
<EmbeddedResource Update="Localization/Resources.resx" GenerateSource="true" EmitFormatMethods="true"/>
```

Only the neutral set carries the item; one accessor serves both sets. A key is a property (`Resources.MenuFile`), and a
key with placeholders also gets a format method (`Resources.FormatFailureThrownDetail(type, message)`). A misspelt key
is a build error, in C# and in AXAML alike.

AXAML reads a key as `{x:Static text:Resources.Key}` with
`xmlns:text="clr-namespace:SemiSpec.UI.Localization"`. A format string with a placeholder is used as a
`StringFormat`: `MessagePanelRepeatFormat` in the message panel.

## The keys

| Owner | Keys |
| --- | --- |
| Main window | `WindowTitle` |
| Second launch | `SingleInstanceNotResponding`, in a Win32 message box with the caption `SemiSpec`, since a second copy opens no Avalonia window |
| Menu | `MenuFile`, `MenuFileExit`, `MenuView`, `MenuViewMessagePanel`, `MenuViewVerboseLogging` |
| Connection row | `ConnectionPortLabel`, `ConnectionPortPlaceholder`, `ConnectionRefresh`, `ConnectionConnect`, `ConnectionDisconnect`, `ChartBackToLive`, `TrendFollowLive` |
| Status bar | `StatusNotConnected`, `StatusConnecting`, `StatusPortUnavailable`, `StatusWaiting`, `StatusReceiving`, `StatusNotResponding`, `StatusOtherDevice`, `StatusSpectrometerFormat`, `StatusSpectrometerConnected`, `StatusSpectrometerNotConnected`, `StatusSpectrometerFailed`, `StatusSpectrometerNoData`, `StatusLostSpectraFormat`, `StatusDroppedFramesFormat`, `StatusNoValue`; the restored entry: `LinkRestoredTitle`, `LinkRestoredDetail` |
| Message panel | `MessagePanelClear`, `MessagePanelRepeatFormat`, `MessagePanelMoreFormat`, `MessagePanelEmpty` |
| Readout pane | `SignalDevice`, `SignalWavelength`, `SignalWindow`, `SignalAmplitude`, `SignalSnr`, `SignalQ`, `SignalUpdated`, `SignalWavelengthFormat`, `SignalWindowFormat`; it also reads the status bar's `StatusNoValue` for a row with no value |
| Axis flyouts | `AxisFlyoutSnapshotNote`; the bounds: `AxisScaleMaximumLabel`, `AxisScaleMinimumLabel`, `AxisScaleStartNmLabel`, `AxisScaleEndNmLabel`, `AxisScaleBoundInvalid`, `AxisScaleMinimumBelowMaximum`, `AxisScaleLogMinimumPositive`; the modes: `AxisScaleTypeLabel`, `AxisScaleLogarithmic`, `ChartFromZero`, `ChartInvert`, `AxisScaleAutoscale`; the time flyout: `TimeWindowStartLabel`, `TimeWindowEndLabel`, `TimeWindowWidthLabel`, `TimeWindowPresets`, `TimeWindowDateFormat`, `TimeWindowClockPattern`, `TimeWindowStartUnreadableFormat`, `TimeWindowEndUnreadableFormat`, `TimeWindowWidthUnreadableFormat`, `TimeWindowEndNotAfterStart` |
| State lines | `ChartStateSeparator`; the spectrum's: `SpectrumHistoricalFormat`, `SpectrumStaleFormat`, `SpectrumStateLog`, `SpectrumStateInverted`, `SpectrumStateFromZero`, `SpectrumStateFrozen`; the trend's: `TrendStateNotFollowing`, `SpectrumStateFrozen`, and the width through the preset labels `TrendPresetMinutesFormat`, `TrendPresetHoursFormat` and `TrendWidthSecondsFormat` |
| Spectrum chart | `SpectrumAxisWavelength`, `SpectrumAxisIntensity`, `SpectrumEmpty` |
| Trend chart | `TrendAxisTime` (before the first refresh), `TrendAxisTimeFormat`, `TrendAxisTimeRangeFormat` (the date of a window across midnight), `TrendAxisIntensity` (before the first spectrum), `TrendAxisIntensityFormat` (the key, with a line break after the first word), `TrendEmpty` |
| Trend readout | `TrendReadoutRangeFormat`, `TrendReadoutCountFormat`, `TrendReadoutNoData`, `TrendReadoutLostFormat`, `TrendReadoutNoSpectra`, `TrendReadoutUncoveredFormat`, `TrendReadoutFailedFormat` |
| `ErrorMessageMapper` | `FailureThrownTitle`, `FailureThrownDetail`, `FailureThrownRemedy`, `FailureUnknownTitle`, `FailureUnknownRemedy`; for each link error a `Title`, `Detail` and `Remedy` key: `PortBusy*`, `PortNotFound*`, `PortLost*`, `BoardNotResponding*`, `UnrecognisedData*`; for a failed port listing `PortListTitle`, `PortListDetail`, `PortListRemedy`; for the store errors `StoreOpenTitle`, `StoreOpenDetail`, `StoreOpenRemedy`, `StoreWriteTitle`, `StoreWriteDetail`, `StoreWriteRemedy`, `StoreStoppedTitle`, `StoreStoppedDetail`, `StoreStoppedRemedy`, `StoreFileReplacedTitle`, `StoreFileReplacedDetail`, `StoreFileReplacedRemedy`; for the trend errors `TrendHistoryTitle`, `TrendHistoryDetail`, `TrendMomentTitle`, `TrendMomentDetail`, `TrendMomentRemedy` |

The device names `ATP-2000` and `ATP-8000` stay literals in code: they read the same in both sets.

A message has three parts: the title names what failed in one short line, the detail names what the app observed, and
the remedy names the action that fixes it. The remedy is empty when no action is owed; such a message has no remedy
key, because every key needs a non-empty value in both sets, and the mapper passes an empty string
(`TrendHistoryError`, the restored entry).

## What stays a literal

Log messages, exception messages and the text of a FluentResults `Error` stay English literals in code: the operator
reads them only through the log file or a message detail, and the developer reads the log. The message panel shows a
plain error's own message as its detail.

## Adding a key

1. Add the key to `Resources.resx` with the English text.
2. Add the same key to `Resources.ru.resx` with the Russian text.
3. Read it through `Resources.Key` in C# or `{x:Static text:Resources.Key}` in AXAML.

`SemiSpec/SemiSpec.Tests.Unit/UI/AppResourcesTests.cs` fails when a neutral key is missing from the
Russian set or has an empty value there.
