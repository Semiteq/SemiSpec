using Avalonia;
using Avalonia.Controls;

using ScottPlot;

namespace SemiSpec.UI.Chart;

/// <summary>Places a chart's one-line state text in the top margin, above the data area of the last render.</summary>
internal static class ChartStateLine
{
	/// <summary>The top axis margin both charts reserve for the line, in ScottPlot pixels.</summary>
	public const float TopMargin = 24f;

	private const double GapAboveData = 2.0;

	/// <summary>
	/// Sizes the band to the margin over the data area; the line sits at the band's bottom, so a hidden line needs
	/// no measure.
	/// </summary>
	public static void Place(Control band, Plot plot)
	{
		var dataRect = plot.LastRender.DataRect;
		var scale = plot.ScaleFactor;
		if (dataRect.Width <= 0f || scale <= 0.0)
		{
			return;
		}

		band.Width = dataRect.Width * scale;
		band.Height = Math.Max(0.0, (dataRect.Top * scale) - GapAboveData);
		band.Margin = new Thickness(dataRect.Left * scale, 0.0, 0.0, 0.0);
	}
}
