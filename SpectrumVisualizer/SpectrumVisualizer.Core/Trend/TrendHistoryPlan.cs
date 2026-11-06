namespace SpectrumVisualizer.Core.Trend;

/// <summary>
/// The store reads that refill the trend after a key change, in the order they run: the visible window, then the rest
/// of the day in hour chunks, newest first; no range reaches past the recent spectra start.
/// </summary>
public static class TrendHistoryPlan
{
	private static readonly TimeSpan _chunk = TimeSpan.FromHours(1);
	private static readonly TimeSpan _history = TimeSpan.FromDays(1);

	public static IReadOnlyList<TimeRange> For(
		DateTimeOffset visibleFrom, DateTimeOffset visibleTo, DateTimeOffset newest, DateTimeOffset recentStart)
	{
		var dayStart = newest - _history;
		var ranges = new List<TimeRange>();
		var visible = new TimeRange(Later(visibleFrom, dayStart), Earlier(visibleTo, recentStart));
		if (visible.To <= visible.From)
		{
			AddChunksNewestFirst(ranges, dayStart, recentStart);
			return ranges;
		}

		ranges.Add(visible);
		AddChunksNewestFirst(ranges, visible.To, recentStart);
		AddChunksNewestFirst(ranges, dayStart, visible.From);
		return ranges;
	}

	private static void AddChunksNewestFirst(List<TimeRange> ranges, DateTimeOffset from, DateTimeOffset to)
	{
		for (var end = to; end > from; end -= _chunk)
		{
			ranges.Add(new TimeRange(Later(from, end - _chunk), end));
		}
	}

	private static DateTimeOffset Later(DateTimeOffset first, DateTimeOffset second)
	{
		return first > second ? first : second;
	}

	private static DateTimeOffset Earlier(DateTimeOffset first, DateTimeOffset second)
	{
		return first < second ? first : second;
	}
}

/// <summary>The half-open time range [From, To).</summary>
public readonly record struct TimeRange(DateTimeOffset From, DateTimeOffset To);
