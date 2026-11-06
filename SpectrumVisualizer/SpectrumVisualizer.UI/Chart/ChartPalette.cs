using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

using ScottPlot;

using Color = ScottPlot.Color;

namespace SpectrumVisualizer.UI.Chart;

public readonly record struct ChartColors(Color Line, Color BandFill, Color CentreLine);

/// <summary>
/// Paints the five ScottPlot surfaces the library draws from its own defaults, and resolves the plottable colours.
/// </summary>
public static class ChartPalette
{
	private const string FigureBackgroundKey = "AppPanelBackgroundBrush";
	private const string DataBackgroundKey = "AppContentBackgroundBrush";
	private const string GridLineKey = "AppSubtleLineBrush";
	private const string MinorGridLineKey = "AppMinorGridLineBrush";
	private const string AxisKey = "AppSecondaryForegroundBrush";
	private const string LineKey = "AppAccentBrush";
	private const string BandFillKey = "AppAccentFillBrush";
	private const string CentreLineKey = "AppMarkerLineBrush";

	public static void Apply(Plot plot, IResourceHost resources, ThemeVariant variant)
	{
		var figureBackground = Resolve(resources, variant, FigureBackgroundKey);
		var dataBackground = Resolve(resources, variant, DataBackgroundKey);
		var gridLine = Resolve(resources, variant, GridLineKey);
		var minorGridLine = Resolve(resources, variant, MinorGridLineKey);
		var axisColor = Resolve(resources, variant, AxisKey);

		plot.FigureBackground.Color = figureBackground;
		plot.DataBackground.Color = dataBackground;
		plot.Grid.MajorLineColor = gridLine;
		plot.Grid.MinorLineColor = minorGridLine;

		foreach (var axis in plot.Axes.GetAxes())
		{
			axis.Label.ForeColor = axisColor;
			axis.TickLabelStyle.ForeColor = axisColor;
			axis.MajorTickStyle.Color = axisColor;
			axis.MinorTickStyle.Color = axisColor;
			axis.FrameLineStyle.Color = axisColor;
		}
	}

	public static ChartColors Colors(IResourceHost resources, ThemeVariant variant)
	{
		return new ChartColors(
			Resolve(resources, variant, LineKey),
			Resolve(resources, variant, BandFillKey),
			Resolve(resources, variant, CentreLineKey));
	}

	private static Color Resolve(IResourceHost resources, ThemeVariant variant, string key)
	{
		if (!resources.TryFindResource(key, variant, out var value) || value is not ISolidColorBrush brush)
		{
			throw new InvalidOperationException($"Palette key '{key}' does not resolve to a brush under {variant}.");
		}

		var alpha = (byte)Math.Round(brush.Color.A * Math.Clamp(brush.Opacity, 0, 1));
		return new Color(brush.Color.R, brush.Color.G, brush.Color.B, alpha);
	}
}
