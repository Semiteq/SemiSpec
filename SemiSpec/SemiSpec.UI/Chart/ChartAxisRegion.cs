using ScottPlot;

using Point = Avalonia.Point;

namespace SemiSpec.UI.Chart;

/// <summary>The band of one axis panel of the last render, for a press on the axis; ported from SemiPlot.</summary>
public sealed class ChartAxisRegion
{
	private readonly float _left;
	private readonly float _right;
	private readonly float _top;
	private readonly float _bottom;
	private readonly double _scale;

	private ChartAxisRegion(float left, float right, float top, float bottom, double scale)
	{
		_left = left;
		_right = right;
		_top = top;
		_bottom = bottom;
		_scale = scale;
	}

	/// <summary>The left axis band beside the data area; null before the first render.</summary>
	public static ChartAxisRegion? TryCreateLeft(Plot plot)
	{
		if (!TryReadPanel(plot, plot.Axes.Left, out var dataRect, out var size, out var offset))
		{
			return null;
		}

		var right = dataRect.Left - offset;
		return new ChartAxisRegion(right - size, right, dataRect.Top, dataRect.Bottom, plot.ScaleFactor);
	}

	/// <summary>The bottom axis band under the data area; null before the first render.</summary>
	public static ChartAxisRegion? TryCreateBottom(Plot plot)
	{
		if (!TryReadPanel(plot, plot.Axes.Bottom, out var dataRect, out var size, out var offset))
		{
			return null;
		}

		var top = dataRect.Bottom + offset;
		return new ChartAxisRegion(dataRect.Left, dataRect.Right, top, top + size, plot.ScaleFactor);
	}

	/// <summary>
	/// Whether the band holds a position in the control's device-independent pixels; the layout runs in those pixels
	/// divided by the plot's scale factor, while GetCoordinates takes them as they are.
	/// </summary>
	public bool Contains(Point position)
	{
		var x = (float)(position.X / _scale);
		var y = (float)(position.Y / _scale);
		return x >= _left && x <= _right && y >= _top && y <= _bottom;
	}

	// A hidden axis still carries a panel of size 0 at offset 0, whose band collapses onto the data rect's own edge.
	private static bool TryReadPanel(Plot plot, IAxis axis, out PixelRect dataRect, out float size, out float offset)
	{
		var layout = plot.RenderManager.LastRender.Layout;
		dataRect = layout.DataRect;
		size = 0f;
		offset = 0f;

		return axis.IsVisible
			&& dataRect.HasArea
			&& layout.PanelSizes.TryGetValue(axis, out size)
			&& layout.PanelOffsets.TryGetValue(axis, out offset);
	}
}
