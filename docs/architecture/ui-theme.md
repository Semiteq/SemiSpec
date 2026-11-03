# UI theme

Every colour the app's own AXAML paints resolves to a key in
`SpectrumVisualizer/SpectrumVisualizer.UI/Styles/Palette.axaml`. The palette is the one SemiPlot uses: the JetBrains
values, in a `Light` and a `Dark` variant with the same key set. `App.axaml` declares `RequestedThemeVariant="Light"`
and nothing in the app changes it, so the app runs on `Light`. What the palette does not cover keeps Semi's own colour.

`SpectrumVisualizer/SpectrumVisualizer.UI/App.axaml` is an include manifest and holds no style body:

1. `Application.Resources`: `ResourceInclude` of `Styles/Palette.axaml`, then of `Styles/Icons.axaml`.
2. `Application.Styles`: `<semi:SemiTheme />`, then `StyleInclude` of `Styles/Forms.axaml`, then of
   `Styles/Buttons.axaml`.

A style sits at the narrowest scope that works: the message panel's severity dots are styled inside
`Messages/MessagePanelView.axaml`.

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

`Palette.axaml` sets seven corner-radius keys to 4 px: `ButtonCornerRadius`, `TextBoxDefaultCornerRadius`,
`CheckBoxBoxCornerRadius`, `ComboBoxSelectorCornerRadius`, `ColorPickerCornerRadius`, `DataGridRowCornerRadius` and
`DataGridColumnHeaderCornerRadius`. They sit at the top level of the dictionary, not in a variant, because a radius does
not change with the variant. The same `StaticResource` alias is why the component keys are overridden and
`SemiBorderRadiusSmall` is not. The app has no `ColorPicker` or `DataGrid`, so two of the seven keys have no reader.

## The application's own surfaces

Semi owns the controls; the `App*` keys are ours, and each exists in both variants. The keys a control in this app reads:

| Key | Consumers | Light | Dark |
| --- | --- | --- | --- |
| `AppPanelBackgroundBrush` | Message panel row, status bar row | `#F7F8FA` | `#2B2D30` |
| `AppContentBackgroundBrush` | Content area | `#FFFFFF` | `#1E1F22` |
| `AppBorderBrush` | The top border of the message panel row and the status bar row | `#EBECF0` | `#393B40` |
| `AppSecondaryForegroundBrush` | Content placeholder; the message panel's empty text, and an entry's remedy, repeat count and time | `#818594` | `#6F737A` |
| `AppSeverityErrorBrush` | The message panel's dot on an `Error` entry; the `invalid` border, the warning glyph and the error message line of `Styles/Forms.axaml` | `#DB3B4B` | `#E55765` |
| `AppSeverityWarningBrush` | The same dot on a `Warning` entry | `#E3AE4D` | `#F2C55C` |
| `AppSeverityInfoBrush` | The same dot on an `Info` entry | `#3574F0` | `#3574F0` |
| `AppDestructiveBackgroundBrush` | `Button.destructive` at rest | `#C7303F` | `#C7303F` |
| `AppDestructivePointeroverBackgroundBrush` | The same button under the pointer | `#B02A38` | `#B02A38` |
| `AppDestructivePressedBackgroundBrush` | The same button pressed | `#992431` | `#992431` |
| `AppDestructiveForegroundBrush` | The same button's caption in all three states | `#FFFFFF` | `#FFFFFF` |

The palette also carries keys no control in this app reads: `AppSubtleLineBrush`, `AppMinorGridLineBrush`,
`AppMarkerLineBrush`, `AppMarkerLabelBrush`, `AppRecipeMarkerLineBrush`, `AppAccentBrush`, `AppAccentFillBrush`,
`AppConnectionOkBrush` and `AppConnectionFaultBrush`. They stay so the file matches SemiPlot's palette key for key.

`Styles/Buttons.axaml` styles `Button.destructive` from the four `AppDestructive*` keys; Semi's `Danger` class is not
used. Semi paints every button state on the template's `ContentPresenter#PART_ContentPresenter`, so each setter targets
that presenter. The rest arm carries `:not(:disabled)`, so a disabled destructive button keeps Semi's disabled paint. No
control in the app carries the class.

`Styles/Icons.axaml` holds the `StreamGeometry` glyphs shared with SemiPlot; `Forms.axaml` reads `WarningGeometry`.

`SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/AppResourcesTests.cs` resolves every key `Forms.axaml` and
`Buttons.axaml` read under both variants.

## A form never resizes on validation

`Styles/Forms.axaml` carries the form rule shared with SemiPlot. The app has no form, so no control reads these
styles. Validation changes colour and text, never size:

- A dialog has a fixed `Width` and `SizeToContent="Height"`; no row appears or disappears with validation.
- An invalid field binds `Classes.invalid` to its validity flag and shows a border in `AppSeverityErrorBrush`
  (`TextBox`, `NumericUpDown`, `CalendarDatePicker`).
- A panel with the class `invalid-glyph` also shows a 16 px `WarningGeometry` glyph inside each invalid `TextBox`,
  8 px from its right edge.
- One `TextBlock.form-message` line reserves two 20 px lines (`Height` 40), wraps and trims past the second; the
  `error` class paints it in `AppSeverityErrorBrush`. It is empty while every field is valid.

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
