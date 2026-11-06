using SemiSpec.Core.Protocol;
using SemiSpec.Core.Store;

namespace SemiSpec.Core.Trend;

/// <summary>
/// The trend's column rules on a folded intensity series: the column width, the gap rule, the failure ranges.
/// </summary>
public static class TrendColumns
{
	public const int MaximumColumns = 2048;

	private const int MinimumColumns = 256;

	private static readonly TimeSpan _failureJoin = TimeSpan.FromSeconds(5);

	// The fold's range is half-open, so a window ending exactly at the newest point would leave it out.
	private static readonly TimeSpan _newestPointInclusion = TimeSpan.FromMilliseconds(1);

	/// <summary>
	/// One column per pixel of the data area, 256 to 2048 columns, each a whole number of ms, at least 1.
	/// </summary>
	public static TimeSpan ColumnWidth(TimeSpan span, double dataAreaPixels)
	{
		var columns = Math.Clamp(Math.Round(dataAreaPixels), MinimumColumns, MaximumColumns);
		var milliseconds = (long)Math.Ceiling(span.Ticks / (columns * TimeSpan.TicksPerMillisecond));

		return TimeSpan.FromTicks(Math.Max(1, milliseconds) * TimeSpan.TicksPerMillisecond);
	}

	/// <summary>The fold query at the key over [from, through], the end included.</summary>
	public static IntensityQuery QueryThrough(
		TrendKey key, DateTimeOffset from, DateTimeOffset through, TimeSpan columnWidth)
	{
		return IntensityQuery.Create(
			key.WavelengthNm, key.Delta, from, through + _newestPointInclusion, columnWidth).Value;
	}

	/// <summary>
	/// The gap rule: a column without a break, a value or an uncovered spectrum is left out, so the line connects
	/// across it.
	/// </summary>
	public static bool HasContent(IntensityColumn column)
	{
		return column.Count > 0 || column.BreakCount > 0 || column.UncoveredCount > 0;
	}

	/// <summary>A break, or an uncovered spectrum with no value, cuts the line at the column's start.</summary>
	public static bool OpensGap(IntensityColumn column)
	{
		return column.BreakCount > 0 || (column.Count == 0 && column.UncoveredCount > 0);
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
