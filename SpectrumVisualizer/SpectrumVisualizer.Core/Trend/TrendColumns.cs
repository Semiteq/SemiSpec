using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;

namespace SpectrumVisualizer.Core.Trend;

/// <summary>Turns a folded intensity series into the envelope columns the trend draws, gaps included.</summary>
public static class TrendColumns
{
	public const int MinimumColumns = 256;
	public const int MaximumColumns = 2048;

	private static readonly TimeSpan _failureJoin = TimeSpan.FromSeconds(5);

	/// <summary>One column per pixel of the data area, 256 to 2048 columns, each a whole number of ms, at least 1.</summary>
	public static TimeSpan ColumnWidth(TimeSpan span, double dataAreaPixels)
	{
		var columns = Math.Clamp(Math.Round(dataAreaPixels), MinimumColumns, MaximumColumns);
		var milliseconds = (long)Math.Ceiling(span.Ticks / (columns * TimeSpan.TicksPerMillisecond));

		return TimeSpan.FromTicks(Math.Max(1, milliseconds) * TimeSpan.TicksPerMillisecond);
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
			if (column.BreakCount > 0 || (column.Count == 0 && column.UncoveredCount > 0))
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
