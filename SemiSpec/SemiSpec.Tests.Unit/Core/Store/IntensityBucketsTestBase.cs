using SemiSpec.Core.Store;

namespace SemiSpec.Tests.Unit.Core.Store;

public abstract class IntensityBucketsTestBase
{
	private protected static readonly DateTimeOffset _from = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

	private protected static IntensityQuery Query(DateTimeOffset from, DateTimeOffset to, TimeSpan width)
	{
		return IntensityQuery.Create(650, 10, from, to, width).Value;
	}

	/// <summary>
	/// Folds one row every 37 ms of the 10 s from _from, valued by its offset, into the query's columns.
	/// </summary>
	private protected static IntensitySeries Fold(IntensityQuery query)
	{
		var buckets = new IntensityBuckets(query);
		for (var offsetMs = 0; offsetMs < 10_000; offsetMs += 37)
		{
			buckets.Add(_from.AddMilliseconds(offsetMs), offsetMs);
		}

		return buckets.ToSeries();
	}

	private protected static IEnumerable<IntensityColumn> FullColumns(IntensitySeries series)
	{
		return series.Columns.Where(c => c.Start >= series.From && c.Start + series.ColumnWidth <= series.To);
	}
}
