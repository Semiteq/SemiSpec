using Avalonia.Media;
using Avalonia.Platform;

using SkiaSharp;

namespace SpectrumVisualizer.Tests.Unit.UI;

/// <summary>
/// Text widths in real faces, which the headless text stub does not give. A width is the widest of Inter and every
/// installed family the control's font family names that holds every glyph of the text, so it holds whichever face
/// the running app resolves.
/// </summary>
internal static class RenderedText
{
	public static double Width(string text, FontFamily family, double fontSize)
	{
		var widest = 0d;
		foreach (var typeface in Candidates(family))
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

		return widest > 0 ? widest : throw new InvalidOperationException($"No candidate face holds every glyph of '{text}'.");
	}

	public static IReadOnlyList<string> InstalledFamilies(FontFamily family)
	{
		var installed = SKFontManager.Default.GetFontFamilies();

		return [.. family.FamilyNames.Where(name => installed.Contains(name, StringComparer.OrdinalIgnoreCase))];
	}

	private static IEnumerable<SKTypeface> Candidates(FontFamily family)
	{
		using (var stream = AssetLoader.Open(new Uri("avares://Avalonia.Fonts.Inter/Assets/Inter-Regular.ttf")))
		using (var data = SKData.Create(stream))
		{
			yield return SKTypeface.FromData(data);
		}

		foreach (var name in InstalledFamilies(family))
		{
			yield return SKFontManager.Default.MatchFamily(name);
		}
	}
}
