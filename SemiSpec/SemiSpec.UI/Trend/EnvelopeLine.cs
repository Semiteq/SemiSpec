using ScottPlot;

using SkiaSharp;

namespace SemiSpec.UI.Trend;

/// <summary>
/// The trend drawn as one stroked polyline through each visible column's min and max; a lone vertex, a column with
/// one value between gaps, draws as a 3 px square, since a stroke needs two vertices.
/// </summary>
public sealed class EnvelopeLine : IPlottable
{
	private const float LoneVertexHalfSize = 1.5f;

	private readonly LineStyle _stroke = new() { Width = 1f, Color = Colors.Black };
	private readonly FillStyle _loneFill = new() { Color = Colors.Black };
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

		if (_pathPoints.Count == 0)
		{
			return;
		}

		using var path = new SKPath();
		for (var index = 0; index < _pathPoints.Count; index++)
		{
			var point = _pathPoints[index];
			var x = Axes.XAxis.GetPixel(point.X, rp.DataRect);
			var y = Axes.YAxis.GetPixel(point.Y, rp.DataRect);
			if (EnvelopePath.IsLone(_pathPoints, index))
			{
				var square = new PixelRect(
					x - LoneVertexHalfSize, x + LoneVertexHalfSize, y + LoneVertexHalfSize, y - LoneVertexHalfSize);
				Drawing.FillRectangle(rp.Canvas, square, rp.Paint, _loneFill);
			}
			else if (point.StartsSegment)
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
		_loneFill.Color = color;
	}

	/// <summary>Replaces every column with the given ascending ones; called under Plot.Sync.</summary>
	public void ReplaceColumns(IReadOnlyList<EnvelopeColumn> columns)
	{
		_columns.Clear();
		_columns.AddRange(columns);
	}
}
