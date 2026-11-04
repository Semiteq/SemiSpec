using AwesomeAssertions;

using SpectrumVisualizer.Core.Trend;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Trend;

[Trait("Component", "Core")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class OverviewGeometryBandTests : OverviewGeometryTestBase
{
	private const double BandWidth = 400.0;
	private const double BandHeight = 30.0;

	[Fact]
	public void ABandWithABreakDrawsTwoFigures()
	{
		var band = Band(
			(0.0, 1.0, 3.0, 2.0),
			(1.0, 2.0, 4.0, 3.0),
			(2.0, double.NaN, double.NaN, double.NaN),
			(3.0, 0.0, 2.0, 1.0),
			(4.0, 1.0, 5.0, 3.0));

		var figures = OverviewGeometry.BandFigures(band, First, First.AddDays(4), BandWidth, BandHeight);

		// The drawn range is [0, 5] over 30 px, so a value v sits at 6 * (5 - v).
		figures.Should().HaveCount(2);
		figures[0].Outline.Should().Equal(
			new BandPoint(0.0, 12.0),
			new BandPoint(100.0, 6.0),
			new BandPoint(100.0, 18.0),
			new BandPoint(0.0, 24.0));
		figures[0].CenterLine.Should().Equal(new BandPoint(0.0, 18.0), new BandPoint(100.0, 12.0));
		figures[1].Outline.Should().Equal(
			new BandPoint(300.0, 18.0),
			new BandPoint(400.0, 0.0),
			new BandPoint(400.0, 24.0),
			new BandPoint(300.0, BandHeight));
		figures[1].CenterLine.Should().Equal(new BandPoint(300.0, 24.0), new BandPoint(400.0, 12.0));
	}

	[Fact]
	public void ALoneColumnBetweenBreaksDrawsAFigureTwoPixelsWide()
	{
		var band = Band(
			(0.0, 1.0, 3.0, 2.0),
			(1.0, 2.0, 4.0, 3.0),
			(2.0, double.NaN, double.NaN, double.NaN),
			(3.0, 2.0, 4.0, 3.0),
			(4.0, double.NaN, double.NaN, double.NaN));

		var figures = OverviewGeometry.BandFigures(band, First, First.AddDays(4), BandWidth, BandHeight);

		figures.Should().HaveCount(2);
		figures[1].Outline.Select(point => point.X).Should().Equal(299.0, 301.0, 301.0, 299.0);
		figures[1].CenterLine.Select(point => point.X).Should().Equal(299.0, 301.0);
	}

	[Fact]
	public void AnInfiniteColumnIsABreakAndEveryPointStaysFinite()
	{
		var band = Band(
			(0.0, 0.0, 2.0, 1.0),
			(1.0, 1.0, 5.0, 3.0),
			(2.0, double.NegativeInfinity, double.PositiveInfinity, 0.0),
			(3.0, 1.0, 3.0, 2.0),
			(4.0, 0.0, 5.0, 2.0));

		var figures = OverviewGeometry.BandFigures(band, First, First.AddDays(4), BandWidth, BandHeight);

		figures.Should().HaveCount(2);
		figures.SelectMany(figure => figure.Outline.Concat(figure.CenterLine))
			.Should().OnlyContain(point => double.IsFinite(point.Y));
	}

	[Fact]
	public void AColumnWithNoCentreIsLeftOutOfTheRange()
	{
		var band = Band(
			(0.0, 0.0, 5.0, 2.0),
			(1.0, 1.0, 4.0, 2.0),
			(2.0, -100.0, 100.0, double.NaN),
			(3.0, 1.0, 4.0, 2.0),
			(4.0, 0.0, 5.0, 3.0));

		var figures = OverviewGeometry.BandFigures(band, First, First.AddDays(4), BandWidth, BandHeight);

		var outlineYs = figures.SelectMany(figure => figure.Outline).Select(point => point.Y).ToArray();
		outlineYs.Min().Should().Be(0.0, "the drawn maximum 5 maps to the top");
		outlineYs.Max().Should().Be(BandHeight, "the drawn minimum 0 maps to the bottom");
	}

	[Fact]
	public void AFlatBandDrawsAMidLine()
	{
		var band = Band((0.0, 5.0, 5.0, 5.0), (1.0, 5.0, 5.0, 5.0));

		var figures = OverviewGeometry.BandFigures(band, First, First.AddDays(1), BandWidth, BandHeight);

		figures.Should().ContainSingle();
		figures[0].Outline.Should().OnlyContain(point => point.Y == BandHeight / 2.0);
		figures[0].CenterLine.Should().OnlyContain(point => point.Y == BandHeight / 2.0);
	}

	[Fact]
	public void AnAllNaNBandDrawsNothing()
	{
		var band = Band(
			(0.0, double.NaN, double.NaN, double.NaN),
			(1.0, double.NaN, double.NaN, double.NaN));

		var figures = OverviewGeometry.BandFigures(band, First, First.AddDays(1), BandWidth, BandHeight);

		figures.Should().BeEmpty();
	}

	[Fact]
	public void BandFiguresDropColumnsOutsideTheBoundsAndMapTheMaxToTheTop()
	{
		var band = Band(
			(-1.0, -100.0, 100.0, 0.0),
			(0.0, 0.0, 10.0, 5.0),
			(1.0, 2.0, 8.0, 5.0),
			(2.0, -100.0, 100.0, 0.0));

		var figures = OverviewGeometry.BandFigures(band, First, First.AddDays(1), BandWidth, BandHeight);

		figures.Should().ContainSingle();
		figures[0].Outline.Should().Equal(
			new BandPoint(0.0, 0.0),
			new BandPoint(BandWidth, 6.0),
			new BandPoint(BandWidth, 24.0),
			new BandPoint(0.0, BandHeight));
		figures[0].CenterLine.Should().Equal(
			new BandPoint(0.0, BandHeight / 2.0),
			new BandPoint(BandWidth, BandHeight / 2.0));
	}

	[Fact]
	public void ConnectedLeavesOutAnEmptyColumnWithoutABreakOrAnUncoveredSpectrum()
	{
		var band = Band((0.0, 1.0, 3.0, 2.0), (1.0, double.NaN, double.NaN, double.NaN), (2.0, 2.0, 4.0, 3.0));

		var connected = OverviewGeometry.Connected(band);

		connected.Columns.Select(column => column.Start).Should().Equal(First, First.AddDays(2));
		OverviewGeometry.BandFigures(connected, First, First.AddDays(2), BandWidth, BandHeight)
			.Should().ContainSingle();
	}

	[Fact]
	public void ConnectedKeepsAnEmptyColumnWithAnUncoveredSpectrumAsABreak()
	{
		var band = Band((0.0, 1.0, 3.0, 2.0), (1.0, double.NaN, double.NaN, double.NaN), (2.0, 2.0, 4.0, 3.0));
		band.Columns[1] = band.Columns[1] with { UncoveredCount = 1 };

		var connected = OverviewGeometry.Connected(band);

		connected.Columns.Should().HaveCount(3);
		OverviewGeometry.BandFigures(connected, First, First.AddDays(2), BandWidth, BandHeight).Should().HaveCount(2);
	}

	[Fact]
	public void ConnectedPutsAGapBeforeAColumnWithABreakAndValues()
	{
		var band = Band((0.0, 1.0, 3.0, 2.0), (1.0, 2.0, 4.0, 3.0), (2.0, 2.0, 4.0, 3.0));
		band.Columns[1] = band.Columns[1] with { BreakCount = 1, LostCount = 2 };

		var connected = OverviewGeometry.Connected(band);

		connected.Columns.Should().HaveCount(4);
		connected.Columns[1].Start.Should().Be(First.AddDays(1));
		double.IsNaN(connected.Columns[1].Min).Should().BeTrue();
		connected.Columns[2].Should().Be(band.Columns[1]);
		OverviewGeometry.BandFigures(connected, First, First.AddDays(2), BandWidth, BandHeight).Should().HaveCount(2);
	}
}
