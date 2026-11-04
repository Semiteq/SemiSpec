using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;

namespace SpectrumVisualizer.Core.Trend;

/// <summary>Turns a folded intensity series into the envelope columns the trend draws, gaps included.</summary>
public static class TrendColumns
{
	public const int MinimumColumns = 256;
	public const int MaximumColumns = 2048;

	private const double VerticalPadding = 0.05;

	private static readonly TimeSpan _failureJoin = TimeSpan.FromSeconds(5);

	// The fold's range is half-open, so a window ending exactly at the newest point would leave it out.
	private static readonly TimeSpan _newestPointInclusion = TimeSpan.FromMilliseconds(1);

	/// <summary>One column per pixel of the data area, 256 to 2048 columns, each a whole number of ms, at least 1.</summary>
	public static TimeSpan ColumnWidth(TimeSpan span, double dataAreaPixels)
	{
		var columns = Math.Clamp(Math.Round(dataAreaPixels), MinimumColumns, MaximumColumns);
		var milliseconds = (long)Math.Ceiling(span.Ticks / (columns * TimeSpan.TicksPerMillisecond));

		return TimeSpan.FromTicks(Math.Max(1, milliseconds) * TimeSpan.TicksPerMillisecond);
	}

	/// <summary>The fold query at the key over [from, through], the end included.</summary>
	public static IntensityQuery QueryThrough(TrendKey key, DateTimeOffset from, DateTimeOffset through, TimeSpan columnWidth)
	{
		return IntensityQuery.Create(key.WavelengthNm, key.Delta, from, through + _newestPointInclusion, columnWidth).Value;
	}

	/// <summary>
	/// Maps each column per the column table of the trend plan; toAxis converts a time to the chart's X value.
	/// </summary>
	public static IReadOnlyList<EnvelopeColumn> Map(IntensitySeries series, Func<DateTimeOffset, double> toAxis)
	{
		var halfWidth = series.ColumnWidth / 2;
		var columns = new List<EnvelopeColumn>(series.Columns.Length);
		foreach (var column in series.Columns)
		{
			if (OpensGap(column))
			{
				columns.Add(new EnvelopeColumn(toAxis(column.Start), double.NaN, double.NaN, double.NaN));
			}

			if (column.Count > 0)
			{
				columns.Add(new EnvelopeColumn(toAxis(column.Start + halfWidth), column.Min, column.Max, column.Last));
			}
		}

		return columns;
	}

	/// <summary>The gap rule: a column without a break, a value or an uncovered spectrum is left out, so the line connects across it.</summary>
	public static bool HasContent(IntensityColumn column)
	{
		return column.Count > 0 || column.BreakCount > 0 || column.UncoveredCount > 0;
	}

	/// <summary>A break, or an uncovered spectrum with no value, cuts the line at the column's start.</summary>
	public static bool OpensGap(IntensityColumn column)
	{
		return column.BreakCount > 0 || (column.Count == 0 && column.UncoveredCount > 0);
	}

	/// <summary>The drawn values padded by 5 % of their span, plus or minus 1 when flat; null when none is drawn.</summary>
	public static (double Bottom, double Top)? VerticalRange(IReadOnlyList<EnvelopeColumn> columns)
	{
		var bottom = double.PositiveInfinity;
		var top = double.NegativeInfinity;
		foreach (var column in columns)
		{
			if (!column.IsGap)
			{
				bottom = Math.Min(bottom, column.Min);
				top = Math.Max(top, column.Max);
			}
		}

		if (double.IsInfinity(bottom))
		{
			return null;
		}

		if (bottom.Equals(top))
		{
			return (bottom - 1.0, top + 1.0);
		}

		var margin = (top - bottom) * VerticalPadding;
		return (bottom - margin, top + margin);
	}

	/// <summary>The failed columns as time ranges; failed columns less than 5 s apart join into one range.</summary>
	public static IReadOnlyList<TimeRange> FailureRanges(IntensitySeries series)
	{
		var ranges = new List<TimeRange>();
		foreach (var column in series.Columns)
		{
			if (column.Failed == BoardState.None)
			{
				continue;
			}

			var end = column.Start + series.ColumnWidth;
			if (ranges.Count > 0 && column.Start - ranges[^1].To < _failureJoin)
			{
				ranges[^1] = ranges[^1] with { To = end };
			}
			else
			{
				ranges.Add(new TimeRange(column.Start, end));
			}
		}

		return ranges;
	}
}
