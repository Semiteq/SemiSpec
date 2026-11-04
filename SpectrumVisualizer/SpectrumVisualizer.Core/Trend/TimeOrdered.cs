namespace SpectrumVisualizer.Core.Trend;

/// <summary>Binary search, insert and trim over lists kept in ascending Unix-millisecond order.</summary>
internal static class TimeOrdered
{
	/// <summary>The index of the first item at or after the moment; Count when none is.</summary>
	public static int LowerBound<T>(List<T> items, long unixMs, Func<T, long> timeOf)
	{
		var low = 0;
		var high = items.Count;
		while (low < high)
		{
			var middle = low + ((high - low) / 2);
			if (timeOf(items[middle]) < unixMs)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}

		return low;
	}

	/// <summary>The index of the first item after the moment; Count when none is.</summary>
	public static int UpperBound<T>(List<T> items, long unixMs, Func<T, long> timeOf)
	{
		return LowerBound(items, unixMs + 1, timeOf);
	}

	/// <summary>Inserts after every item of the same or an earlier time, so equal times keep their arrival order.</summary>
	public static void Insert<T>(List<T> items, T item, Func<T, long> timeOf)
	{
		var unixMs = timeOf(item);
		if (items.Count == 0 || timeOf(items[^1]) <= unixMs)
		{
			items.Add(item);
			return;
		}

		items.Insert(UpperBound(items, unixMs, timeOf), item);
	}

	public static void DropBefore<T>(List<T> items, long unixMs, Func<T, long> timeOf)
	{
		var count = LowerBound(items, unixMs, timeOf);
		if (count > 0)
		{
			items.RemoveRange(0, count);
		}
	}
}
