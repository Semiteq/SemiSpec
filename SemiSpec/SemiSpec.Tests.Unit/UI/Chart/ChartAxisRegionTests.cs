using Avalonia;
using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using ScottPlot;

using SemiSpec.UI.Chart;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartAxisRegionTests
{
	private const int ControlWidth = 800;
	private const int ControlHeight = 600;

	[AvaloniaTheory]
	[InlineData(1f)]
	[InlineData(2f)]
	public void TheBandsAndTheCoordinatesAgreeOnDeviceIndependentPixelsAtAnyScale(float scale)
	{
		var plot = new Plot { ScaleFactor = scale };
		plot.Axes.SetLimits(0.0, 100.0, 0.0, 100.0);
		// AvaPlot renders at the control's size in device-independent pixels.
		plot.RenderInMemory(ControlWidth, ControlHeight);
		var data = plot.RenderManager.LastRender.Layout.DataRect;
		var middleY = data.VerticalCenter * scale;
		var middleX = data.HorizontalCenter * scale;

		var left = ChartAxisRegion.TryCreateLeft(plot)!;
		var bottom = ChartAxisRegion.TryCreateBottom(plot)!;

		left.Contains(new Point((data.Left - 5) * scale, middleY)).Should().BeTrue();
		left.Contains(new Point((data.Left + 5) * scale, middleY)).Should().BeFalse("that point is in the data area");
		bottom.Contains(new Point(middleX, (data.Bottom + 5) * scale)).Should().BeTrue();
		bottom.Contains(new Point(middleX, (data.Bottom - 5) * scale)).Should().BeFalse();
		plot.GetCoordinates(new Pixel(data.Right * scale, data.Top * scale)).X.Should().BeApproximately(100.0, 1e-6,
			"GetCoordinates, which TrendChartView.TimeAt calls, takes the same device-independent pixels");
	}
}
