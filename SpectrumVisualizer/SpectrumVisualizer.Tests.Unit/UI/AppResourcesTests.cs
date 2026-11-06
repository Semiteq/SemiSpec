using System.Collections;
using System.Globalization;

using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;

using AwesomeAssertions;

using SpectrumVisualizer.UI;
using SpectrumVisualizer.UI.Localization;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI;

[Trait("Component", "UI")]
[Trait("Area", "Resources")]
[Trait("Category", "Unit")]
public sealed class AppResourcesTests
{
	// Every App* key the application's own views read.
	private static readonly string[] _viewKeys =
	[
		"AppPanelBackgroundBrush",
		"AppContentBackgroundBrush",
		"AppBorderBrush",
		"AppSecondaryForegroundBrush",
		"AppSubtleLineBrush",
		"AppSeverityErrorBrush",
		"AppSeverityWarningBrush",
		"AppSeverityInfoBrush",
		"AppConnectionOkBrush",
		"AppConnectionFaultBrush",
		"AppAccentBrush",
		"AppAccentFillBrush",
		"AppMarkerLineBrush",
		"AppMinorGridLineBrush",
	];

	[Fact]
	public void TheRussianSetCarriesEveryKeyTheNeutralSetCarries()
	{
		var neutral = ReadSet(CultureInfo.InvariantCulture);
		var russian = ReadSet(RussianCulture);

		neutral.Should().NotBeEmpty();
		russian.Keys.Should().BeEquivalentTo(neutral.Keys);
	}

	[Fact]
	public void BothSetsResolveEveryKeyToANonEmptyValue()
	{
		foreach (var culture in new[] { CultureInfo.InvariantCulture, RussianCulture })
		{
			foreach (var entry in ReadSet(culture))
			{
				entry.Value.Should().NotBeNullOrWhiteSpace(
					"'{0}' is read by the operator in '{1}'", entry.Key, culture.Name);
			}
		}
	}

	[AvaloniaTheory]
	[InlineData("Light")]
	[InlineData("Dark")]
	public void EveryKeyTheViewsReadResolvesUnderBothVariants(string variantName)
	{
		var variant = variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

		foreach (var key in _viewKeys)
		{
			Application.Current!.TryGetResource(key, variant, out var value)
				.Should().BeTrue("'{0}' has to resolve under {1}", key, variantName);
			value.Should().NotBeNull();
		}
	}

	[Theory]
	[InlineData("ru-RU", "ru-RU")]
	[InlineData("ru", "ru-RU")]
	[InlineData("en-US", "en-US")]
	[InlineData("en-GB", "en-US")]
	[InlineData("de-DE", "en-US")]
	public void SemisOwnStringsFollowTheUiLanguage(string uiCulture, string expected)
	{
		App.SemiLocaleFor(CultureInfo.GetCultureInfo(uiCulture)).Name.Should().Be(expected);
	}

	private static CultureInfo RussianCulture => CultureInfo.GetCultureInfo("ru");

	private static Dictionary<string, string> ReadSet(CultureInfo culture)
	{
		var set = Resources.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
		set.Should().NotBeNull("the '{0}' resource set has to load", culture.Name);

		return set!
			.Cast<DictionaryEntry>()
			.ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
	}
}
