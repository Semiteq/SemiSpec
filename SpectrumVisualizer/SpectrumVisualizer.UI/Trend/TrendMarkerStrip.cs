using ScottPlot;

namespace SpectrumVisualizer.UI.Trend;

/// <summary>The X range of one marked column of the trend, in axis units.</summary>
public readonly record struct StripSpan(double FromX, double ToX);

/// <summary>A 4 px bar along the bottom of the data area over each marked column; it never steers the axis limits.</summary>
public sealed class TrendMarkerStrip : IPlottable
{
	public const float Height = 4f;

	private const float MinimumWidth = 1f;

	private readonly FillStyle _fill = new();

	// The UI thread writes the spans and the colour; the render thread reads them in Render.
	private readonly List<StripSpan> _spans = [];
	private readonly Lock _renderStateLock = new();
	private Color _color = Colors.Red;

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

		lock (_renderStateLock)
		{
			_fill.Color = _color;
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
	}

	public void Recolor(Color color)
	{
		lock (_renderStateLock)
		{
			_color = color;
		}
	}

	/// <summary>Replaces every span with the given ones.</summary>
	public void ReplaceSpans(IReadOnlyList<StripSpan> spans)
	{
		lock (_renderStateLock)
		{
			_spans.Clear();
			_spans.AddRange(spans);
		}
	}
}
