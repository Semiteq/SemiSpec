using ScottPlot;

using SkiaSharp;

using SpectrumVisualizer.Core.Trend;

namespace SpectrumVisualizer.UI.Trend;

/// <summary>The trend drawn as one stroked polyline through each visible column's min and max; no fill, no markers.</summary>
public sealed class EnvelopeLine : IPlottable
{
	private readonly LineStyle _stroke = new() { Width = 1f };
	private readonly List<EnvelopePoint> _pathPoints = [];

	// The UI thread writes the columns and the colour; the render thread reads them in Render and GetAxisLimits.
	private readonly List<EnvelopeColumn> _columns = [];
	private readonly Lock _renderStateLock = new();
	private Color _color = Colors.Black;

	public bool IsVisible { get; set; } = true;

	public IAxes Axes { get; set; } = new Axes();

	public IEnumerable<LegendItem> LegendItems => [];

	public AxisLimits GetAxisLimits()
	{
		lock (_renderStateLock)
		{
			if (_columns.Count == 0)
			{
				return AxisLimits.NoLimits;
			}

			var bottom = double.PositiveInfinity;
			var top = double.NegativeInfinity;

			foreach (var column in _columns)
			{
				if (EnvelopePath.IsGap(column))
				{
					continue;
				}

				bottom = Math.Min(bottom, column.Min);
				top = Math.Max(top, column.Max);
			}

			var left = _columns[0].X;
			var right = _columns[^1].X;

			return double.IsInfinity(bottom)
				? AxisLimits.HorizontalOnly(left, right)
				: new AxisLimits(left, right, bottom, top);
		}
	}

	public void Render(RenderPack rp)
	{
		lock (_renderStateLock)
		{
			_stroke.Color = _color;
			var (first, lastExclusive) = EnvelopePath.VisibleRange(_columns, Axes.XAxis.Min, Axes.XAxis.Max);
			EnvelopePath.Build(_columns, first, lastExclusive, _pathPoints);
		}

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

	public void Recolor(Color color)
	{
		lock (_renderStateLock)
		{
			_color = color;
		}
	}

	/// <summary>Replaces every column with the given ascending ones.</summary>
	public void ReplaceColumns(IReadOnlyList<EnvelopeColumn> columns)
	{
		lock (_renderStateLock)
		{
			_columns.Clear();
			_columns.AddRange(columns);
		}
	}
}
