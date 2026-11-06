using SemiSpec.Core.Store;
using SemiSpec.Core.Trend;

namespace SemiSpec.UI.Trend;

/// <summary>
/// One envelope column at chart X: its min and max, and the centre value the line passes; NaN min is a gap.
/// </summary>
public readonly record struct EnvelopeColumn(double X, double Min, double Max, double Center)
{
	private const double VerticalPadding = 0.05;
	private const double MinimumVerticalSpan = 2.0;

	public bool IsGap => double.IsNaN(Min) || double.IsNaN(Max);

	/// <summary>
	/// Maps each column per the column table in docs/architecture/overview.md; toAxis converts a time to the chart's X.
	/// </summary>
	public static IReadOnlyList<EnvelopeColumn> Map(IntensitySeries series, Func<DateTimeOffset, double> toAxis)
	{
		var halfWidth = series.ColumnWidth / 2;
		var columns = new List<EnvelopeColumn>(series.Columns.Length);
		foreach (var column in series.Columns)
		{
			if (TrendColumns.OpensGap(column))
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

	/// <summary>
	/// The drawn values padded by 5 % of their span, or a 2-wide range on their centre when the span is narrower;
	/// null when none is drawn.
	/// </summary>
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

		if (top - bottom < MinimumVerticalSpan)
		{
			var centre = (bottom + top) / 2.0;
			return (centre - (MinimumVerticalSpan / 2.0), centre + (MinimumVerticalSpan / 2.0));
		}

		var margin = (top - bottom) * VerticalPadding;
		return (bottom - margin, top + margin);
	}
}
