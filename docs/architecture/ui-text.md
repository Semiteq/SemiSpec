# UI text

Every string the operator reads lives in `SpectrumVisualizer/SpectrumVisualizer.UI/Localization/Resources.resx`
(English) and `Resources.ru.resx` (Russian). C# source stays ASCII: the `terse` gate in the pre-commit hook and in CI
rejects any other character, so Cyrillic and typographic glyphs live only in the resx.

## Two sets, one selector

`SpectrumVisualizer.UI.csproj` sets `<NeutralLanguage>en</NeutralLanguage>`, so the English set is the neutral one and
lives in the main assembly. `Resources.ru.resx` carries the same keys and compiles to a `ru` satellite assembly; the SDK
takes the culture from the file name.

The app sets no culture. The generated accessor reads `ResourceManager.GetString(key, Culture)` with `Resources.Culture`
left null, which falls through to `CultureInfo.CurrentUICulture`, the Windows UI language. A Russian Windows shows the
Russian set; any other language shows English. Semi's own control strings follow the same culture (`ui-theme.md`).

Numbers and times follow `CultureInfo.CurrentCulture`. The log file is the exception: `AppLogging` writes it with the
invariant culture, so every machine produces the same log format.

## The generated accessor

`Microsoft.CodeAnalysis.ResxSourceGenerator`, pinned in `SpectrumVisualizer/Directory.Packages.props` and referenced with
`PrivateAssets="all"`, generates the `Resources` class from one item:

```xml
<EmbeddedResource Update="Localization/Resources.resx" GenerateSource="true" EmitFormatMethods="true"/>
```

Only the neutral set carries the item; one accessor serves both sets. A key is a property (`Resources.MenuFile`), and a
key with placeholders also gets a format method (`Resources.FormatFailureThrownDetail(type, message)`). A misspelt key
is a build error, in C# and in AXAML alike.

AXAML reads a key as `{x:Static text:Resources.Key}` with
`xmlns:text="clr-namespace:SpectrumVisualizer.UI.Localization"`. A format string with a placeholder is used as a
`StringFormat`: `MessagePanelRepeatFormat` in the message panel.

## The keys

| Owner | Keys |
| --- | --- |
| Main window | `WindowTitle`, `ContentPlaceholder` |
| Menu | `MenuFile`, `MenuFileExit`, `MenuView`, `MenuViewVerboseLogging` |
| Status bar | `StatusNotConnected` |
| Message panel | `MessagePanelTitle`, `MessagePanelClear`, `MessagePanelRepeatFormat` |
| `ErrorMessageMapper` | `FailureThrownTitle`, `FailureThrownDetail`, `FailureThrownRemedy`, `FailureUnknownTitle`, `FailureUnknownRemedy` |

A message has three parts: the title names what failed in one short line, the detail names what the app observed, and
the remedy names the action that fixes it. The remedy is empty when no action is owed.

## What stays a literal

Log messages, exception messages and the text of a FluentResults `Error` stay English literals in code: the operator
reads them only through the log file or a message detail, and the developer reads the log. The message panel shows a
plain error's own message as its detail.

## Adding a key

1. Add the key to `Resources.resx` with the English text.
2. Add the same key to `Resources.ru.resx` with the Russian text.
3. Read it through `Resources.Key` in C# or `{x:Static text:Resources.Key}` in AXAML.

`SpectrumVisualizer/SpectrumVisualizer.Tests.Unit/UI/AppResourcesTests.cs` fails when a neutral key is missing from the
Russian set or has an empty value there.
