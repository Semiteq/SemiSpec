using ScottPlot;

using SemiSpec.Core.Trend;

namespace SemiSpec.UI.Trend;

/// <summary>
/// A 4 px bar along the bottom of the data area over each failure range; it never steers the axis limits.
/// </summary>
public sealed class TrendMarkerStrip : IPlottable
{
	private const float Height = 4f;
	private const float MinimumWidth = 1f;

	private readonly FillStyle _fill = new() { Color = Colors.Red };

	// The UI thread writes the spans and the colour under Plot.Sync; ScottPlot renders under the same lock.
	private readonly List<StripSpan> _spans = [];

	public bool IsVisible { get; set; } = true;

	public IAxes Axes { get; set; } = new Axes();

	public IEnumerable<LegendItem> LegendItems => [];

	public AxisLimits GetAxisLimits()
	{
		return AxisLimits.NoLimits;
	}

	public void Render(RenderPack rp)
	{
		var dataRect = rp.DataRect;
		var top = dataRect.Bottom - Height;

		foreach (var span in _spans)
		{
			var left = Math.Max(dataRect.Left, Axes.XAxis.GetPixel(span.FromX, dataRect));
			var right = Math.Min(dataRect.Right, Axes.XAxis.GetPixel(span.ToX, dataRect));
			if (right < dataRect.Left || left > dataRect.Right)
			{
				continue;
			}

			right = Math.Max(right, left + MinimumWidth);
			Drawing.FillRectangle(rp.Canvas, new PixelRect(left, right, dataRect.Bottom, top), rp.Paint, _fill);
		}
	}

	/// <summary>Called under Plot.Sync.</summary>
	public void Recolor(Color color)
	{
		_fill.Color = color;
	}

	/// <summary>Replaces every span with the given time ranges on the axis; called under Plot.Sync.</summary>
	public void ReplaceSpans(IReadOnlyList<TimeRange> ranges)
	{
		_spans.Clear();
		foreach (var range in ranges)
		{
			_spans.Add(new StripSpan(LocalTimeAxis.ToAxis(range.From), LocalTimeAxis.ToAxis(range.To)));
		}
	}

	/// <summary>One failure range, in axis units.</summary>
	private readonly record struct StripSpan(double FromX, double ToX);
}
