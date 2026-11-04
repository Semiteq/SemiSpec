using ScottPlot;

using SkiaSharp;

namespace SpectrumVisualizer.UI.Trend;

/// <summary>
/// The trend drawn as one stroked polyline through each visible column's min and max; no fill, no markers.
/// </summary>
public sealed class EnvelopeLine : IPlottable
{
	private readonly LineStyle _stroke = new() { Width = 1f, Color = Colors.Black };
	private readonly List<EnvelopePoint> _pathPoints = [];

	// The UI thread writes the columns and the colour under Plot.Sync; ScottPlot renders under the same lock.
	private readonly List<EnvelopeColumn> _columns = [];

	public bool IsVisible { get; set; } = true;

	public IAxes Axes { get; set; } = new Axes();

	public IEnumerable<LegendItem> LegendItems => [];

	/// <summary>The chart sets both axes on every refresh, so ScottPlot never autoscales to this line.</summary>
	public AxisLimits GetAxisLimits()
	{
		return AxisLimits.NoLimits;
	}

	public void Render(RenderPack rp)
	{
		var (first, lastExclusive) = EnvelopePath.VisibleRange(_columns, Axes.XAxis.Min, Axes.XAxis.Max);
		EnvelopePath.Build(_columns, first, lastExclusive, _pathPoints);

		if (_pathPoints.Count < 2)
		{
			return;
		}

		using var path = new SKPath();
		foreach (var point in _pathPoints)
		{
			var x = Axes.XAxis.GetPixel(point.X, rp.DataRect);
			var y = Axes.YAxis.GetPixel(point.Y, rp.DataRect);
			if (point.StartsSegment)
			{
				path.MoveTo(x, y);
			}
			else
			{
				path.LineTo(x, y);
			}
		}

		Drawing.DrawLines(rp.Canvas, rp.Paint, path, _stroke);
	}

	/// <summary>Called under Plot.Sync.</summary>
	public void Recolor(Color color)
	{
		_stroke.Color = color;
	}

	/// <summary>Replaces every column with the given ascending ones; called under Plot.Sync.</summary>
	public void ReplaceColumns(IReadOnlyList<EnvelopeColumn> columns)
	{
		_columns.Clear();
		_columns.AddRange(columns);
	}
}
