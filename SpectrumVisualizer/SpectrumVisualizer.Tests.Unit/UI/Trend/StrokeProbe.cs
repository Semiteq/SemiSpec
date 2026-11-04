using ScottPlot;

namespace SpectrumVisualizer.Tests.Unit.UI.Trend;

/// <summary>The pixel probe of a rendered plot: where the red channel dominates the other two.</summary>
internal static class StrokeProbe
{
	// A channel this far above the other two is drawn data, not an antialiased edge of a grey grid line, label or frame.
	private const int DominanceThreshold = 24;

	public static bool IsRed(byte[,,] pixels, int row, int column)
	{
		var red = pixels[row, column, 0];
		var others = Math.Max(pixels[row, column, 1], pixels[row, column, 2]);

		return red - others >= DominanceThreshold;
	}

	public static bool ColumnCarriesRed(byte[,,] pixels, PixelRect dataRect, int column)
	{
		for (var row = (int)Math.Ceiling(dataRect.Top); row <= (int)Math.Floor(dataRect.Bottom); row++)
		{
			if (IsRed(pixels, row, column))
			{
				return true;
			}
		}

		return false;
	}
}
