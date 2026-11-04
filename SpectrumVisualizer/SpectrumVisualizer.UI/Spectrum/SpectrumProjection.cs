namespace SpectrumVisualizer.UI.Spectrum;

/// <summary>What one projection learned about the frame: the largest value before the log step, and darkness.</summary>
internal readonly record struct ProjectedSpectrum(double RawMax, bool IsDark);

internal static class SpectrumProjection
{
	/// <summary>Writes the displayed value of each pixel into ys: inverted over the frame's maximum, then log10 floored at 1.</summary>
	public static ProjectedSpectrum Fill(ReadOnlySpan<ushort> pixels, bool inverted, bool logarithmic, Span<double> ys)
	{
		ushort max = 0;
		foreach (var pixel in pixels)
		{
			if (pixel > max)
			{
				max = pixel;
			}
		}

		var rawMax = 0;
		for (var i = 0; i < pixels.Length; i++)
		{
			var value = inverted ? max - pixels[i] : pixels[i];
			rawMax = Math.Max(rawMax, value);
			ys[i] = logarithmic ? Math.Log10(Math.Max(value, 1)) : value;
		}

		return new ProjectedSpectrum(rawMax, IsDark: max == 0);
	}
}
