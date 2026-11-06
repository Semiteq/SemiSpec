# UI theme

Every colour the app's own AXAML paints resolves to a key in
`SpectrumVisualizer/SpectrumVisualizer.UI/Styles/Palette.axaml`. The palette is a subset of the one SemiPlot uses: the
JetBrains values, in a `Light` and a `Dark` variant with the same key set, cut to the `App*` keys the views read plus
Semi's component overrides. `App.axaml` declares `RequestedThemeVariant="Light"` and nothing in the app changes it, so
the app runs on `Light`. What the palette does not cover keeps Semi's own colour.

`SpectrumVisualizer/SpectrumVisualizer.UI/App.axaml` is an include manifest and holds no style body:

1. `Application.Resources`: `ResourceInclude` of `Styles/Palette.axaml`.
2. `Application.Styles`: `<semi:SemiTheme />`.

A style sits at the narrowest scope that works.

## How the palette reaches a control

Semi's semantic tokens (`SemiColorPrimary`, `SemiColorText0` and the rest) cannot be retinted from the app. Semi's
component keys alias them through `StaticResource`, which resolves inside Semi's own dictionary at load time, so an
override in `Application.Resources` is never read. A control template reads the component key through
`DynamicResource`, so `Palette.axaml` overrides component keys (`TextBlockDefaultForeground`, `MenuItemForeground`,
`ButtonSolidPrimaryBackground` and the rest) inside `ResourceDictionary.ThemeDictionaries`.

Consumers read palette keys with `DynamicResource`. A `StaticResource` read resolves once and stops following the
variant.

## Semi's own control strings

Semi keys its built-in control strings by specific culture and falls back to
`zh-CN` for any other. `App.Initialize` calls `SemiTheme.OverrideLocaleResources` with `App.SemiLocaleFor`, which maps a
Russian UI culture to `ru-RU` and every other one to `en-US`.

## Corner radius sits outside the theme dictionaries

`Palette.axaml` sets four corner-radius keys to 4 px: `ButtonCornerRadius`, `TextBoxDefaultCornerRadius`,
`CheckBoxBoxCornerRadius` and `ComboBoxSelectorCornerRadius`. They sit at the top level of the dictionary, not in a
variant, because a radius does not change with the variant. The same `StaticResource` alias is why the component keys
are overridden and `SemiBorderRadiusSmall` is not.

## The application's own surfaces

Semi owns the controls; the `App*` keys are ours, and each exists in both variants. Every `App*` key has a reader:

| Key | Consumers | Light | Dark |
| --- | --- | --- | --- |
| `AppPanelBackgroundBrush` | Connection row, message panel row, status bar row, spectrum pane, trend pane, overview pane, signal pane; the trend readout's box; each plot's figure background | `#F7F8FA` | `#2B2D30` |
| `AppContentBackgroundBrush` | Each plot's data area; the overview strip | `#FFFFFF` | `#1E1F22` |
| `AppBorderBrush` | The bottom border of the connection row; the top border of the trend pane, the overview pane, the message panel row and the status bar row; the left border of the signal pane; the border of the trend readout's box | `#EBECF0` | `#393B40` |
| `AppSecondaryForegroundBrush` | The empty-state line of each chart; the historical line over the spectrum chart; the trend's hover line and the overview's hover line; each plot's axis labels, tick labels, ticks and frame; an entry's remedy, repeat count and time; the status bar dot in a neutral link state | `#818594` | `#6F737A` |
| `AppSubtleLineBrush` | The 1 px separators between status bar segments; each plot's major gridlines | `#EBECF0` | `#393B40` |
| `AppMinorGridLineBrush` | The spectrum plot's minor gridlines, drawn on the log axis only | `#F4F5F7` | `#2B2D30` |
| `AppAccentBrush` | The spectrum line; the trend's envelope line; the overview band at opacity 0.35; the border of the overview's window highlight | `#3574F0` | `#3574F0` |
| `AppAccentFillBrush` | The ±`delta` window band and the overview's window highlight, `Opacity="0.25"` | `#3574F0` | `#3574F0` |
| `AppMarkerLineBrush` | The centre line at the frame's wavelength; the line at the shown moment on the trend | `#6F737A` | `#A8ADBD` |
| `AppSeverityErrorBrush` | The message panel's dot on an Error entry; the trend's failure strip | `#DB3B4B` | `#E55765` |
| `AppSeverityWarningBrush` | The message panel's dot on a Warning entry | `#E3AE4D` | `#F2C55C` |
| `AppSeverityInfoBrush` | The message panel's dot on an Info entry | `#3574F0` | `#3574F0` |
| `AppConnectionOkBrush` | The status bar dot in Receiving | `#208A3C` | `#5FAD65` |
| `AppConnectionFaultBrush` | The status bar dot in PortUnavailable, NotResponding and OtherDevice | `#DB3B4B` | `#E55765` |

`SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/AppResourcesTests.cs` resolves every key in this table under both
variants.

## The plot

Two ScottPlot plots exist: the spectrum plot and the trend plot. ScottPlot draws with its own colour values, not with
Avalonia brushes, so the plots are the consumers that do not read keys through `DynamicResource`. `ChartPalette.Apply`
resolves each key with `TryFindResource` under the application's actual variant and paints five surfaces of a plot:
the figure background, the data area, the major and minor gridlines, and each axis's label, tick labels, ticks and
frame. `ChartPalette.Colors` resolves the series colours: the spectrum line, the band and the centre line, which
`SpectrumChartViewModel.ApplyPalette` paints, and the envelope line, the shown-moment line and the failure strip, which
`TrendChartViewModel.ApplyPalette` paints. Each view runs both inside one hold of its plot's `lock (Plot.Sync)`, so a
render never sees half a repaint. A key that does not resolve to a solid brush throws with the key's name. The trend's
readout and hover line and the overview are Avalonia controls over or beside the plot and read their keys through
`DynamicResource`.

ScottPlot has no brush opacity, so the resolved colour's alpha is the brush colour's alpha times its `Opacity`:
`AppAccentFillBrush` reaches the plot at alpha 64, and the band stays translucent over the line.

`SpectrumChartView` and `TrendChartView` each repaint their plot when they load and on every
`ActualThemeVariantChanged` of the application, and remove that handler when they unload; both do it through their
`ChartFrames` (`UI/Chart/`).

## An off-scale inset in AXAML is a defect

Every `Margin`, `Padding`, `Spacing`, `ColumnSpacing` and `RowSpacing` value in the UI's AXAML is one to four numbers from
the scale 0, 4, 8, 12, 16, 24, 32. A text button carries no padding of its own: Semi's `Button` theme gives it `12,0` and a
32 px `MinHeight`. This command lists every off-scale inset and prints nothing on the current tree:

```powershell
git grep -nP '(?:Margin|Padding|Spacing)(?:\x22\s+Value)?=\x22(?!(?:0|4|8|12|16|24|32)(?:[, ]+(?:0|4|8|12|16|24|32)){0,3}\x22)' -- 'SpectrumVisualizer/SpectrumVisualizer.UI/*.axaml'
```

`\x22` is the double quote, so the command runs unchanged in PowerShell and bash; it needs Git built with PCRE. Sizes
that are not insets (`Width`, `Height`, `MaxHeight`, `LineHeight`, `BorderThickness`, `CornerRadius`) are outside the
rule. No CI job runs the command.

## A colour literal in AXAML is a defect

The palette is the only place a colour is written. A literal on a control cannot follow the variant. This command lists
every literal outside `Styles/` and prints nothing on the current tree:

```powershell
git grep -nE '#[0-9A-Fa-f]{6,8}\b|"(Gray|Black|White)"' -- 'SpectrumVisualizer/SpectrumVisualizer.UI/*.axaml' ':!SpectrumVisualizer/SpectrumVisualizer.UI/Styles/*'
```

No CI job runs it.
