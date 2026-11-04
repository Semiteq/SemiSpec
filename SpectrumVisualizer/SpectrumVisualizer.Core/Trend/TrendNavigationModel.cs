namespace SpectrumVisualizer.Core.Trend;

/// <summary>
/// The trend's visible window in UTC, sticky to the live edge at the start; the live edge and the first point arrive as
/// inputs, never a clock.
/// </summary>
public sealed class TrendNavigationModel(DateTimeOffset from, DateTimeOffset to, DateTimeOffset firstSample)
{
	public static readonly TimeSpan MinimumWidth = TimeSpan.FromSeconds(10);
	public static readonly TimeSpan MaximumWidth = TimeSpan.FromDays(1);

	// Zoom widths snap onto the ladder MinimumWidth * ZoomQuantizationRatio^n. The wheel uses reciprocal factors
	// (0.8 = 1 / 1.25 and 1.25), so an in-then-out cycle returns to the exact origin width instead of drifting.
	private const double ZoomQuantizationRatio = 1.25;

	public DateTimeOffset From { get; private set; } = from;

	public DateTimeOffset To { get; private set; } = from + ClampWidthSpan(to - from);

	public TimeSpan Width => To - From;

	public bool IsSticky { get; private set; } = true;

	/// <summary>The pan floor: a pan or a zoom never moves From before it; a later move applies the new floor.</summary>
	public DateTimeOffset FirstSample { get; set; } = firstSample;

	public void Pan(TimeSpan delta, DateTimeOffset now)
	{
		PlaceFrom(From + delta, Width);
		DetachUnlessHolding(now);
	}

	public void Zoom(double factor, DateTimeOffset anchor, DateTimeOffset now)
	{
		var currentWidth = Width;
		var targetWidth = ClampWidthSpan(QuantizeWidth(currentWidth * factor));

		var anchorFraction = (anchor - From) / currentWidth;
		// Clamped rather than anchor-pinned: pinning would render the empty left span as data.
		PlaceFrom(anchor - (targetWidth * anchorFraction), targetWidth);
		DetachUnlessHolding(now);
	}

	/// <summary>Sets the width exactly, off the zoom ladder, keeping the right edge and sticky.</summary>
	public void SetWidth(TimeSpan width)
	{
		var clampedWidth = ClampWidthSpan(width);
		if (IsSticky)
		{
			From = To - clampedWidth;
			return;
		}

		PlaceFrom(To - clampedWidth, clampedWidth);
	}

	public void JumpToNow(DateTimeOffset now)
	{
		var width = Width;
		To = now;
		From = now - width;
		IsSticky = true;
	}

	public void DetachSticky()
	{
		IsSticky = false;
	}

	public void OnLiveEdge(DateTimeOffset now)
	{
		if (!IsSticky)
		{
			return;
		}

		var width = Width;
		To = now;
		From = now - width;
	}

	/// <summary>A window that no longer holds now stops following.</summary>
	private void DetachUnlessHolding(DateTimeOffset now)
	{
		if (now > To || now < From)
		{
			IsSticky = false;
		}
	}

	private void PlaceFrom(DateTimeOffset from, TimeSpan width)
	{
		From = from < FirstSample ? FirstSample : from;
		To = From + width;
	}

	private static TimeSpan QuantizeWidth(TimeSpan width)
	{
		if (width <= MinimumWidth)
		{
			return MinimumWidth;
		}

		var steps = Math.Round(Math.Log(width / MinimumWidth) / Math.Log(ZoomQuantizationRatio));
		var quantizedSeconds = MinimumWidth.TotalSeconds * Math.Pow(ZoomQuantizationRatio, steps);

		return TimeSpan.FromSeconds(quantizedSeconds);
	}

	private static TimeSpan ClampWidthSpan(TimeSpan width)
	{
		if (width < MinimumWidth)
		{
			return MinimumWidth;
		}

		if (width > MaximumWidth)
		{
			return MaximumWidth;
		}

		return width;
	}
}
