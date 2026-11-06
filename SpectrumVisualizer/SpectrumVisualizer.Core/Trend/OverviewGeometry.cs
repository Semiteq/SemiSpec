using SpectrumVisualizer.Core.Store;

namespace SpectrumVisualizer.Core.Trend;

/// <summary>Places the trend window, the pointer and the intensity band along the overview strip.</summary>
public static class OverviewGeometry
{
	private const double FlatBandPadding = 1.0;
	private const double LoneColumnSize = 3.0;

	/// <summary>The window as fractions of the extent; an empty extent yields the full strip.</summary>
	public static (double Start, double Width) WindowFraction(
		DateTimeOffset extentFirst,
		DateTimeOffset extentLast,
		DateTimeOffset windowFrom,
		DateTimeOffset windowTo)
	{
		var span = (extentLast - extentFirst).TotalSeconds;
		if (span <= 0.0)
		{
			return (0.0, 1.0);
		}

		var start = Math.Clamp((windowFrom - extentFirst).TotalSeconds / span, 0.0, 1.0);
		var end = Math.Clamp((windowTo - extentFirst).TotalSeconds / span, 0.0, 1.0);

		return (start, end - start);
	}

	/// <summary>The window highlight in pixels: at least the minimum width, never outside the strip.</summary>
	public static (double Left, double Width) HighlightSpan(
		double startFraction,
		double widthFraction,
		double stripWidth,
		double minimumWidth)
	{
		if (stripWidth <= 0.0)
		{
			return (0.0, 0.0);
		}

		var width = Math.Min(stripWidth, Math.Max(minimumWidth, widthFraction * stripWidth));

		return (SpanLeftWithin(startFraction * stripWidth, width, stripWidth), width);
	}

	/// <summary>Moves a span's left edge so it lies inside [0, limit]; a wider span starts at 0.</summary>
	public static double SpanLeftWithin(double left, double width, double limit)
	{
		return Math.Max(0.0, Math.Min(left, limit - width));
	}

	public static DateTimeOffset TimeAtFraction(DateTimeOffset extentFirst, DateTimeOffset extentLast, double fraction)
	{
		var span = extentLast - extentFirst;

		return extentFirst + (span * Math.Clamp(fraction, 0.0, 1.0));
	}

	/// <summary>
	/// The band columns under the trend's gap rule: an empty column without a break or an uncovered spectrum is left
	/// out, so the band connects across it; a break puts an empty column before the column's values.
	/// </summary>
	public static IntensitySeries Connected(IntensitySeries band)
	{
		var count = 0;
		foreach (var column in band.Columns)
		{
			count += (PutsGapBeforeValues(column) ? 1 : 0) + (TrendColumns.HasContent(column) ? 1 : 0);
		}

		var columns = new IntensityColumn[count];
		var next = 0;
		foreach (var column in band.Columns)
		{
			if (PutsGapBeforeValues(column))
			{
				columns[next++] = column with { Min = double.NaN, Max = double.NaN, Last = double.NaN, Count = 0 };
			}

			if (TrendColumns.HasContent(column))
			{
				columns[next++] = column;
			}
		}

		return band with { Columns = columns };
	}

	/// <summary>The unbroken runs of drawable columns between first and last, scaled to the band's pixels.</summary>
	public static IReadOnlyList<BandFigure> BandFigures(
		IntensitySeries band,
		DateTimeOffset first,
		DateTimeOffset last,
		double width,
		double height)
	{
		var spanSeconds = (last - first).TotalSeconds;
		if (spanSeconds <= 0.0 || width <= 0.0 || height <= 0.0
			|| !TryReadBandRange(band.Columns, first, last, out var bottom, out var top))
		{
			return [];
		}

		var scale = new BandScale(first, spanSeconds, width, bottom, top, height);
		var figures = new List<BandFigure>();
		var runStart = -1;

		for (var index = 0; index <= band.Columns.Length; index++)
		{
			if (index < band.Columns.Length && IsDrawn(band.Columns[index], first, last))
			{
				runStart = runStart < 0 ? index : runStart;
				continue;
			}

			if (runStart >= 0)
			{
				figures.Add(Figure(band.Columns, runStart, index, scale));
				runStart = -1;
			}
		}

		return figures;
	}

	private static bool TryReadBandRange(
		IntensityColumn[] columns,
		DateTimeOffset first,
		DateTimeOffset last,
		out double bottom,
		out double top)
	{
		bottom = double.MaxValue;
		top = double.MinValue;
		var hasValue = false;

		foreach (var column in columns)
		{
			if (IsDrawn(column, first, last))
			{
				bottom = Math.Min(bottom, column.Min);
				top = Math.Max(top, column.Max);
				hasValue = true;
			}
		}

		if (hasValue && bottom == top)
		{
			bottom -= FlatBandPadding;
			top += FlatBandPadding;
		}

		return hasValue;
	}

	private static bool IsDrawn(IntensityColumn column, DateTimeOffset first, DateTimeOffset last)
	{
		return column.Start >= first && column.Start <= last
			&& double.IsFinite(column.Min)
			&& double.IsFinite(column.Max)
			&& double.IsFinite(column.Last);
	}

	private static BandFigure Figure(IntensityColumn[] columns, int start, int end, BandScale scale)
	{
		if (end - start == 1)
		{
			return LoneColumn(columns[start], scale);
		}

		var outline = new List<BandPoint>(2 * (end - start));
		var centerLine = new List<BandPoint>(end - start);

		for (var index = start; index < end; index++)
		{
			var x = scale.X(columns[index].Start);
			outline.Add(new BandPoint(x, scale.Y(columns[index].Max)));
			centerLine.Add(new BandPoint(x, scale.Y(columns[index].Last)));
		}

		for (var index = end - 1; index >= start; index--)
		{
			outline.Add(new BandPoint(scale.X(columns[index].Start), scale.Y(columns[index].Min)));
		}

		return new BandFigure(outline, centerLine);
	}

	private static bool PutsGapBeforeValues(IntensityColumn column)
	{
		return TrendColumns.OpensGap(column) && column.Count > 0;
	}

	/// <summary>At least a 3 px square, so a column of one value shows as a mark rather than a line of no height.</summary>
	private static BandFigure LoneColumn(IntensityColumn column, BandScale scale)
	{
		var center = scale.X(column.Start);
		var left = SpanLeftWithin(center - (LoneColumnSize / 2.0), LoneColumnSize, scale.Width);
		var right = Math.Min(scale.Width, left + LoneColumnSize);
		var middle = scale.Y(column.Last);
		var top = scale.Y(column.Max);
		var bottom = scale.Y(column.Min);
		if (bottom - top < LoneColumnSize)
		{
			top = SpanLeftWithin(((top + bottom) / 2.0) - (LoneColumnSize / 2.0), LoneColumnSize, scale.Height);
			bottom = Math.Min(scale.Height, top + LoneColumnSize);
		}

		return new BandFigure(
			[
				new BandPoint(left, top),
				new BandPoint(right, top),
				new BandPoint(right, bottom),
				new BandPoint(left, bottom)
			],
			[new BandPoint(left, middle), new BandPoint(right, middle)]);
	}

	private readonly record struct BandScale(
		DateTimeOffset First,
		double SpanSeconds,
		double Width,
		double Bottom,
		double Top,
		double Height)
	{
		public double X(DateTimeOffset timestamp)
		{
			return (timestamp - First).TotalSeconds / SpanSeconds * Width;
		}

		public double Y(double value)
		{
			return (Top - value) / (Top - Bottom) * Height;
		}
	}
}

/// <summary>
/// One unbroken run of the overview band in control coordinates: max forward, min back, and the centre.
/// </summary>
public sealed record BandFigure(IReadOnlyList<BandPoint> Outline, IReadOnlyList<BandPoint> CenterLine);

public readonly record struct BandPoint(double X, double Y);
