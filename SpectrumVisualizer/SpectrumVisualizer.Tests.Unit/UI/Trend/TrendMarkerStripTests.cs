using AwesomeAssertions;

using ScottPlot;

using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.UI.Trend;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Trend;

[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendMarkerStripTests
{
	private const int PlotWidth = 400;
	private const int PlotHeight = 300;
	private const double AxisMax = 100.0;
	// Rows above the bottom edge of the data area, clear of the frame line and of the strip's antialiased top edge.
	private const int InsideStripRow = 2;
	private const int AboveStripRow = 8;

	[Fact]
	public void TheStripNeverSteersTheLimits()
	{
		var strip = new TrendMarkerStrip();
		strip.ReplaceSpans([Span(10.0, 20.0)]);

		strip.GetAxisLimits().Should().Be(AxisLimits.NoLimits);
	}

	[Fact]
	public void ASpanFillsTheBottomOfTheDataAreaOverItsColumnsOnly()
	{
		var (rendered, pixels, dataRect) = Render([Span(40.0, 60.0)]);
		using var plot = rendered;
		var bottom = (int)Math.Floor(dataRect.Bottom);
		var inside = PixelColumnAt(plot, 50.0);
		var outside = PixelColumnAt(plot, 20.0);

		StrokeProbe.IsRed(pixels, bottom - InsideStripRow, inside).Should().BeTrue("the span is marked");
		StrokeProbe.IsRed(pixels, bottom - AboveStripRow, inside).Should().BeFalse("the strip is 4 px tall");
		StrokeProbe.IsRed(pixels, bottom - InsideStripRow, outside).Should().BeFalse("only the span is marked");
	}

	[Fact]
	public void ASpanNarrowerThanAPixelStillMarksOne()
	{
		var (rendered, pixels, dataRect) = Render([Span(50.0, 50.0001)]);
		using var plot = rendered;
		var bottom = (int)Math.Floor(dataRect.Bottom);
		var column = PixelColumnAt(plot, 50.0);

		var marked = Enumerable.Range(column - 1, 3)
			.Any(candidate => StrokeProbe.IsRed(pixels, bottom - InsideStripRow, candidate));

		marked.Should().BeTrue("one failed spectrum in a 24 h window still shows");
	}

	private static (Plot Plot, byte[,,] Pixels, PixelRect DataRect) Render(IReadOnlyList<TimeRange> spans)
	{
		var strip = new TrendMarkerStrip();
		strip.Recolor(Colors.Red);
		strip.ReplaceSpans(spans);
		var plot = new Plot();
		plot.Add.Plottable(strip);
		plot.Axes.SetLimits(0, AxisMax, 0, 1);

		using var image = plot.GetImage(PlotWidth, PlotHeight);

		return (plot, image.GetArrayRGB(), plot.RenderManager.LastRender.Layout.DataRect);
	}

	private static TimeRange Span(double fromX, double toX)
	{
		return new TimeRange(LocalTimeAxis.FromAxis(fromX), LocalTimeAxis.FromAxis(toX));
	}

	private static int PixelColumnAt(Plot plot, double x)
	{
		return (int)Math.Round(plot.GetPixel(new Coordinates(x, 0)).X);
	}
}
