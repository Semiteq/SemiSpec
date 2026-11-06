namespace SemiSpec.UI.Trend;

/// <summary>One vertex of the polyline the envelope columns are drawn as.</summary>
public readonly record struct EnvelopePoint(double X, double Y, bool StartsSegment);

/// <summary>
/// The geometry EnvelopeLine strokes, kept free of Skia so the culling window and the gap break are testable.
/// </summary>
public static class EnvelopePath
{
	// One column beyond each edge, so a segment entering the viewport is drawn rather than starting at it.
	public static (int First, int LastExclusive) VisibleRange(
		IReadOnlyList<EnvelopeColumn> columns,
		double xMin,
		double xMax)
	{
		if (columns.Count == 0)
		{
			return (0, 0);
		}

		var first = Math.Max(0, FirstAtOrAfter(columns, xMin) - 1);
		var lastExclusive = Math.Min(columns.Count, FirstAfter(columns, xMax) + 1);

		return (first, Math.Max(first, lastExclusive));
	}

	public static void Build(
		IReadOnlyList<EnvelopeColumn> columns, int first, int lastExclusive, List<EnvelopePoint> destination)
	{
		destination.Clear();

		// NaN doubles as "no Y yet", which a gap column restores; a drawn Y is never NaN.
		var previousY = first > 0 ? columns[first - 1].Center : double.NaN;
		var segmentIsOpen = false;

		for (var index = first; index < lastExclusive; index++)
		{
			var column = columns[index];
			if (column.IsGap)
			{
				previousY = double.NaN;
				segmentIsOpen = false;
				continue;
			}

			if (column.Min.Equals(column.Max))
			{
				AddVertex(column.X, column.Min);
				previousY = column.Min;
				continue;
			}

			// Entering at the edge nearer the previous Y keeps the crossing between two columns short.
			var entersAtMin = double.IsNaN(previousY)
				|| Math.Abs(column.Min - previousY) <= Math.Abs(column.Max - previousY);
			var entry = entersAtMin ? column.Min : column.Max;
			var farExtreme = entersAtMin ? column.Max : column.Min;
			var heldY = column.Center;

			AddVertex(column.X, entry);
			AddVertex(column.X, farExtreme);
			if (!heldY.Equals(farExtreme))
			{
				AddVertex(column.X, heldY);
			}

			previousY = heldY;
		}

		void AddVertex(double x, double y)
		{
			destination.Add(new EnvelopePoint(x, y, !segmentIsOpen));
			segmentIsOpen = true;
		}
	}

	/// <summary>True for a vertex that opens a segment no next vertex continues.</summary>
	public static bool IsLone(IReadOnlyList<EnvelopePoint> points, int index)
	{
		return points[index].StartsSegment && (index + 1 == points.Count || points[index + 1].StartsSegment);
	}

	private static int FirstAtOrAfter(IReadOnlyList<EnvelopeColumn> columns, double x)
	{
		var low = 0;
		var high = columns.Count;

		while (low < high)
		{
			var middle = low + ((high - low) / 2);
			if (columns[middle].X < x)
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

	private static int FirstAfter(IReadOnlyList<EnvelopeColumn> columns, double x)
	{
		var low = 0;
		var high = columns.Count;

		while (low < high)
		{
			var middle = low + ((high - low) / 2);
			if (columns[middle].X <= x)
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
}
