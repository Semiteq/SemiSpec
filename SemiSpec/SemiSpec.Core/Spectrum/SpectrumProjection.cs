namespace SemiSpec.Core.Spectrum;

/// <summary>What one projection learned about the frame: the largest value before the log step, and darkness.</summary>
public readonly record struct ProjectedSpectrum(double RawMax, bool IsDark);

public static class SpectrumProjection
{
	/// <summary>
	/// Writes each pixel's displayed value into ys: inverted over the frame's maximum if display asks, then log10
	/// floored at 1 on the log axis.
	/// </summary>
	public static ProjectedSpectrum Fill(ReadOnlySpan<ushort> pixels, SpectrumDisplay display, Span<double> ys)
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
			var value = display.IsInverted ? max - pixels[i] : pixels[i];
			rawMax = Math.Max(rawMax, value);
			ys[i] = display.IsLogarithmic ? Math.Log10(Math.Max(value, 1)) : value;
		}

		return new ProjectedSpectrum(rawMax, IsDark: max == 0);
	}
}
