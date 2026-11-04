using AwesomeAssertions;

using ScottPlot;

using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.UI.Trend;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Trend;

[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class EnvelopeLineTests
{
	private const int PlotWidth = 800;
	private const int PlotHeight = 500;
	private const int TestTimeoutMilliseconds = 120_000;
	private const int LongColumnCount = 6000;
	private const int ShortColumnCount = 2000;
	private const int GapEvery = 500;
	private const int SampleCount = 600;
	private const int GapStartIndex = 250;
	private const int ResumeIndex = 350;
	// Distance from the gap's edges to the columns that must still carry the line, in samples.
	private const int ProbeOffsetSamples = 100;
	// Half the band each column spans around its value, so every column strokes a vertical run.
	private const double ColumnSpread = 2.0;
	private const double AxisMin = 0.0;
	private const double AxisMax = 100.0;

	private static readonly TimeSpan _mutationPause = TimeSpan.FromMilliseconds(1);

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task RenderWhileTheUiThreadReplacesTheColumnsDoesNotThrow()
	{
		var line = new EnvelopeLine();
		using var plot = new Plot();
		plot.Add.Plottable(line);
		line.ReplaceColumns(Columns(LongColumnCount));
		plot.Axes.SetLimits(0, LongColumnCount, -2.0, 2.0);

		var longColumns = Columns(LongColumnCount);
		var shortColumns = Columns(ShortColumnCount);

		await RenderWhileMutating.RunAsync(
			() =>
			{
				plot.RenderInMemory(PlotWidth, PlotHeight);
				_ = line.GetAxisLimits();
			},
			() =>
			{
				line.ReplaceColumns(longColumns);
				line.ReplaceColumns(shortColumns);
				line.ReplaceColumns([]);
				Thread.Sleep(_mutationPause);
			},
			"the render thread must survive concurrent replacements");
	}

	[Fact]
	public void AnEmptyLineHasNoLimits()
	{
		new EnvelopeLine().GetAxisLimits().Should().Be(AxisLimits.NoLimits);
	}

	[Fact]
	public void TheLimitsSpanTheColumnsAndSkipTheGaps()
	{
		var line = new EnvelopeLine();
		line.ReplaceColumns(
		[
			new EnvelopeColumn(1.0, 2.0, 5.0, 3.0),
			new EnvelopeColumn(2.0, double.NaN, double.NaN, double.NaN),
			new EnvelopeColumn(3.0, -1.0, 4.0, 0.0)
		]);

		line.GetAxisLimits().Should().Be(new AxisLimits(1.0, 3.0, -1.0, 5.0));
	}

	[Fact]
	public void AnAllGapLineLimitsOnlyTheHorizontal()
	{
		var line = new EnvelopeLine();
		line.ReplaceColumns(
		[
			new EnvelopeColumn(1.0, double.NaN, double.NaN, double.NaN),
			new EnvelopeColumn(4.0, double.NaN, double.NaN, double.NaN)
		]);

		line.GetAxisLimits().Should().Be(AxisLimits.HorizontalOnly(1.0, 4.0));
	}

	// The trend's own gap shape: the gap column stands at the start of the broken stretch and its empty columns are
	// absent, so the line stops at the last value before the gap and resumes at the first value after it.
	[Fact]
	public void AGapColumnLeavesTheGapCentreBlankWhileBothSidesCarryTheLine()
	{
		var line = new EnvelopeLine();
		line.Recolor(Colors.Red);
		using var plot = new Plot();
		plot.Add.Plottable(line);
		line.ReplaceColumns(ColumnsWithGap());
		plot.Axes.SetLimits(0, SampleCount - 1, AxisMin, AxisMax);

		using var image = plot.GetImage(PlotWidth, PlotHeight);
		var pixels = image.GetArrayRGB();
		var dataRect = plot.RenderManager.LastRender.Layout.DataRect;

		StrokeProbe.ColumnCarriesRed(pixels, dataRect, PixelColumnAt(plot, (GapStartIndex + ResumeIndex) / 2))
			.Should().BeFalse("a NaN column must break the line, leaving background where it would cross");
		StrokeProbe.ColumnCarriesRed(pixels, dataRect, PixelColumnAt(plot, GapStartIndex - ProbeOffsetSamples))
			.Should().BeTrue("the segment before the gap is drawn");
		StrokeProbe.ColumnCarriesRed(pixels, dataRect, PixelColumnAt(plot, ResumeIndex + ProbeOffsetSamples))
			.Should().BeTrue("the segment after the gap is drawn");
	}

	private static List<EnvelopeColumn> Columns(int count)
	{
		var columns = new List<EnvelopeColumn>(count);
		for (var index = 0; index < count; index++)
		{
			var isGap = index % GapEvery == GapEvery - 1;
			columns.Add(isGap
				? new EnvelopeColumn(index, double.NaN, double.NaN, double.NaN)
				: new EnvelopeColumn(index, -1.0, 1.0, Math.Sin(index / 7.0)));
		}

		return columns;
	}

	private static List<EnvelopeColumn> ColumnsWithGap()
	{
		var columns = new List<EnvelopeColumn>(SampleCount);
		for (var index = 0; index < SampleCount; index++)
		{
			if (index == GapStartIndex)
			{
				columns.Add(new EnvelopeColumn(index, double.NaN, double.NaN, double.NaN));
			}
			else if (index is < GapStartIndex or >= ResumeIndex)
			{
				var value = 50.0 + (20.0 * Math.Sin(index / 7.0));
				columns.Add(new EnvelopeColumn(index, value - ColumnSpread, value + ColumnSpread, value));
			}
		}

		return columns;
	}

	private static int PixelColumnAt(Plot plot, double x)
	{
		return (int)Math.Round(plot.GetPixel(new Coordinates(x, AxisMin)).X);
	}
}
