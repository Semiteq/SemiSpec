using SemiSpec.Core.Chart;

namespace SemiSpec.Core.Spectrum;

public readonly record struct AxisRange(double Min, double Max);

public readonly record struct SpectrumLimits(AxisRange X, AxisRange Y);

/// <summary>
/// The axis limits of the shown spectrum; Y is in counts on the linear axis and in decades on the log axis.
/// </summary>
public static class SpectrumScale
{
	private const double PaddingShare = 0.05;
	private const double LinearFlatHalfRange = 1.0;
	private const double LogFlatHalfRange = 0.5;

	/// <summary>X over the device range of xs; Y over ys padded by five percent, or widened when ys is flat.</summary>
	public static SpectrumLimits Following(ReadOnlySpan<double> xs, ReadOnlySpan<double> ys, SpectrumDisplay display)
	{
		var x = DeviceRange(xs);
		var min = double.PositiveInfinity;
		var max = double.NegativeInfinity;
		foreach (var y in ys)
		{
			min = Math.Min(min, y);
			max = Math.Max(max, y);
		}

		var padding = min == max
			? display.IsLogarithmic ? LogFlatHalfRange : LinearFlatHalfRange
			: (max - min) * PaddingShare;

		return new SpectrumLimits(x, new AxisRange(min - padding, max + padding));
	}

	/// <summary>
	/// X over the device range of xs; Y from zero to the largest of ys plus 5 %, in decades on the log axis.
	/// </summary>
	public static SpectrumLimits FromZero(ReadOnlySpan<double> xs, ReadOnlySpan<double> ys)
	{
		var max = double.NegativeInfinity;
		foreach (var y in ys)
		{
			max = Math.Max(max, y);
		}

		var (bottom, top) = FromZeroRange.Over(max);
		return new SpectrumLimits(DeviceRange(xs), new AxisRange(bottom, top));
	}

	private static AxisRange DeviceRange(ReadOnlySpan<double> xs)
	{
		return new AxisRange(xs[0], xs[^1]);
	}
}
