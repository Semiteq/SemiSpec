namespace SpectrumVisualizer.UI.Spectrum;

internal readonly record struct AxisRange(double Min, double Max);

internal readonly record struct SpectrumLimits(AxisRange X, AxisRange Y);

/// <summary>The axis limits of the shown spectrum; Y is in counts on the linear axis and in decades on the log axis.</summary>
internal static class SpectrumScale
{
	private const double PaddingShare = 0.05;
	private const double LinearFlatHalfRange = 1.0;
	private const double LogFlatHalfRange = 0.5;
	private const double StuckHeadroom = 1.2;
	private static readonly AxisRange _stuckEmptyRange = new(0.0, 1.0);

	/// <summary>A dark frame keeps lastNonDarkY when there is one, so the axis does not jump on every dark frame.</summary>
	public static SpectrumLimits Following(
		ReadOnlySpan<double> xs,
		ReadOnlySpan<double> ys,
		bool isDark,
		bool logarithmic,
		AxisRange? lastNonDarkY)
	{
		var x = DeviceRange(xs);
		if (isDark && lastNonDarkY is { } kept)
		{
			return new SpectrumLimits(x, kept);
		}

		var min = double.PositiveInfinity;
		var max = double.NegativeInfinity;
		foreach (var y in ys)
		{
			min = Math.Min(min, y);
			max = Math.Max(max, y);
		}

		var padding = min == max
			? logarithmic ? LogFlatHalfRange : LinearFlatHalfRange
			: (max - min) * PaddingShare;

		return new SpectrumLimits(x, new AxisRange(min - padding, max + padding));
	}

	/// <summary>Y from zero to 1.2 times rawMax, the largest displayed value before the log step.</summary>
	public static SpectrumLimits StuckToZero(ReadOnlySpan<double> xs, double rawMax, bool logarithmic)
	{
		var x = DeviceRange(xs);
		if (logarithmic)
		{
			return new SpectrumLimits(x, rawMax <= 1.0 ? _stuckEmptyRange : new AxisRange(0.0, Math.Log10(rawMax * StuckHeadroom)));
		}

		return new SpectrumLimits(x, rawMax == 0.0 ? _stuckEmptyRange : new AxisRange(0.0, rawMax * StuckHeadroom));
	}

	private static AxisRange DeviceRange(ReadOnlySpan<double> xs)
	{
		return new AxisRange(xs[0], xs[^1]);
	}
}
