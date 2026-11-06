using Avalonia.Media;
using Avalonia.Platform;

using SkiaSharp;

namespace SemiSpec.Tests.Unit.UI;

/// <summary>
/// Text widths in real faces, which the headless text stub does not give; the widest candidate face wins, so it bounds
/// whatever face the app resolves.
/// </summary>
internal static class RenderedText
{
	public static double Width(string text, FontFamily family, double fontSize, FontWeight weight = FontWeight.Normal)
	{
		var widest = 0d;
		foreach (var typeface in Candidates(family, weight))
		{
			using (typeface)
			using (var font = new SKFont(typeface, (float)fontSize) { LinearMetrics = true, Subpixel = true })
			{
				if (font.ContainsGlyphs(text))
				{
					widest = Math.Max(widest, font.MeasureText(text));
				}
			}
		}

		return widest > 0
			? widest
			: throw new InvalidOperationException($"No candidate face holds every glyph of '{text}'.");
	}

	/// <summary>
	/// The tallest candidate face's line spacing, the height a one-line text block takes in that face.
	/// </summary>
	public static double LineHeight(FontFamily family, double fontSize)
	{
		var tallest = 0d;
		foreach (var typeface in Candidates(family, FontWeight.Normal))
		{
			using (typeface)
			using (var font = new SKFont(typeface, (float)fontSize))
			{
				tallest = Math.Max(tallest, font.Spacing);
			}
		}

		return tallest;
	}

	public static IReadOnlyList<string> InstalledFamilies(FontFamily family)
	{
		var installed = SKFontManager.Default.GetFontFamilies();

		return [.. family.FamilyNames.Where(name => installed.Contains(name, StringComparer.OrdinalIgnoreCase))];
	}

	private static IEnumerable<SKTypeface> Candidates(FontFamily family, FontWeight weight)
	{
		var isBold = weight >= FontWeight.Bold;
		var interFace = isBold ? "Inter-Bold.ttf" : "Inter-Regular.ttf";
		using (var stream = AssetLoader.Open(new Uri($"avares://Avalonia.Fonts.Inter/Assets/{interFace}")))
		using (var data = SKData.Create(stream))
		{
			yield return SKTypeface.FromData(data);
		}

		foreach (var name in InstalledFamilies(family))
		{
			yield return SKFontManager.Default.MatchFamily(name, isBold ? SKFontStyle.Bold : SKFontStyle.Normal);
		}
	}
}
