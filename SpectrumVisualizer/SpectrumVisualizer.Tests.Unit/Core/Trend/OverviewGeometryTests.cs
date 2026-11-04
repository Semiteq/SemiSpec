using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Core.Trend;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Trend;

[Trait("Component", "Core")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class OverviewGeometryTests
{
	private const double StripWidth = 900.0;
	private const double MinimumHighlightWidth = 6.0;
	private const double BandWidth = 400.0;
	private const double BandHeight = 30.0;
	private static readonly DateTimeOffset _first = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset _last = new(2026, 6, 20, 0, 0, 0, TimeSpan.Zero);

	[Fact]
	public void WindowFractionMidWindowMapsToCenteredRectangle()
	{
		var from = new DateTimeOffset(2026, 6, 12, 0, 0, 0, TimeSpan.Zero);
		var to = new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero);

		var (start, width) = OverviewGeometry.WindowFraction(_first, _last, from, to);

		start.Should().BeApproximately(0.2, 1e-9);
		width.Should().BeApproximately(0.4, 1e-9);
	}

	[Fact]
	public void WindowFractionWindowEqualsExtentFillsStrip()
	{
		var (start, width) = OverviewGeometry.WindowFraction(_first, _last, _first, _last);

		start.Should().Be(0.0);
		width.Should().BeApproximately(1.0, 1e-9);
	}

	[Fact]
	public void WindowFractionWindowReachesBeyondExtentClampsToStrip()
	{
		var from = _first - TimeSpan.FromDays(5);
		var to = _last + TimeSpan.FromDays(5);

		var (start, width) = OverviewGeometry.WindowFraction(_first, _last, from, to);

		start.Should().Be(0.0);
		width.Should().Be(1.0);
	}

	[Fact]
	public void WindowFractionZeroSpanExtentReturnsFullStrip()
	{
		var (start, width) = OverviewGeometry.WindowFraction(_first, _first, _first, _first);

		start.Should().Be(0.0);
		width.Should().Be(1.0);
	}

	[Theory]
	[InlineData(1.0)]
	[InlineData(0.999)]
	public void HighlightSpanAtTheRightEndStaysInsideTheStrip(double startFraction)
	{
		var (left, width) = OverviewGeometry.HighlightSpan(startFraction, 0.0, StripWidth, MinimumHighlightWidth);

		width.Should().Be(MinimumHighlightWidth);
		(left + width).Should().BeLessThanOrEqualTo(StripWidth);
	}

	[Fact]
	public void HighlightSpanWindowWiderThanTheStripFillsItExactly()
	{
		var (left, width) = OverviewGeometry.HighlightSpan(-0.5, 2.0, StripWidth, MinimumHighlightWidth);

		left.Should().Be(0.0);
		width.Should().Be(StripWidth);
	}

	[Fact]
	public void HighlightSpanZeroStripWidthIsEmpty()
	{
		var span = OverviewGeometry.HighlightSpan(0.5, 0.1, 0.0, MinimumHighlightWidth);

		span.Should().Be((0.0, 0.0));
	}

	[Fact]
	public void HighlightSpanWindowAtTheLeftEdgeKeepsLeftAtZero()
	{
		var (left, width) = OverviewGeometry.HighlightSpan(0.0, 0.001, StripWidth, MinimumHighlightWidth);

		left.Should().Be(0.0);
		width.Should().Be(MinimumHighlightWidth);
	}

	[Theory]
	[InlineData(100.0, 100.0)]
	[InlineData(-5.0, 0.0)]
	[InlineData(899.5, StripWidth - 1.0)]
	public void SpanLeftWithinKeepsAOnePixelSpanInsideTheLimit(double left, double expected)
	{
		OverviewGeometry.SpanLeftWithin(left, 1.0, StripWidth).Should().Be(expected);
	}

	[Fact]
	public void SpanLeftWithinASpanWiderThanTheLimitStartsAtItsLeftEdge()
	{
		OverviewGeometry.SpanLeftWithin(10.0, StripWidth + 50.0, StripWidth).Should().Be(0.0);
	}

	[Fact]
	public void ABandWithABreakDrawsTwoFigures()
	{
		var band = Band(
			(0.0, 1.0, 3.0, 2.0),
			(1.0, 2.0, 4.0, 3.0),
			(2.0, double.NaN, double.NaN, double.NaN),
			(3.0, 0.0, 2.0, 1.0),
			(4.0, 1.0, 5.0, 3.0));

		var figures = OverviewGeometry.BandFigures(band, _first, _first.AddDays(4), BandWidth, BandHeight);

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

		var figures = OverviewGeometry.BandFigures(band, _first, _first.AddDays(4), BandWidth, BandHeight);

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

		var figures = OverviewGeometry.BandFigures(band, _first, _first.AddDays(4), BandWidth, BandHeight);

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

		var figures = OverviewGeometry.BandFigures(band, _first, _first.AddDays(4), BandWidth, BandHeight);

		var outlineYs = figures.SelectMany(figure => figure.Outline).Select(point => point.Y).ToArray();
		outlineYs.Min().Should().Be(0.0, "the drawn maximum 5 maps to the top");
		outlineYs.Max().Should().Be(BandHeight, "the drawn minimum 0 maps to the bottom");
	}

	[Fact]
	public void AFlatBandDrawsAMidLine()
	{
		var band = Band((0.0, 5.0, 5.0, 5.0), (1.0, 5.0, 5.0, 5.0));

		var figures = OverviewGeometry.BandFigures(band, _first, _first.AddDays(1), BandWidth, BandHeight);

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

		var figures = OverviewGeometry.BandFigures(band, _first, _first.AddDays(1), BandWidth, BandHeight);

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

		var figures = OverviewGeometry.BandFigures(band, _first, _first.AddDays(1), BandWidth, BandHeight);

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

		connected.Columns.Select(column => column.Start).Should().Equal(_first, _first.AddDays(2));
		OverviewGeometry.BandFigures(connected, _first, _first.AddDays(2), BandWidth, BandHeight)
			.Should().ContainSingle();
	}

	[Fact]
	public void ConnectedKeepsAnEmptyColumnWithAnUncoveredSpectrumAsABreak()
	{
		var band = Band((0.0, 1.0, 3.0, 2.0), (1.0, double.NaN, double.NaN, double.NaN), (2.0, 2.0, 4.0, 3.0));
		band.Columns[1] = band.Columns[1] with { UncoveredCount = 1 };

		var connected = OverviewGeometry.Connected(band);

		connected.Columns.Should().HaveCount(3);
		OverviewGeometry.BandFigures(connected, _first, _first.AddDays(2), BandWidth, BandHeight).Should().HaveCount(2);
	}

	[Fact]
	public void ConnectedPutsAGapBeforeAColumnWithABreakAndValues()
	{
		var band = Band((0.0, 1.0, 3.0, 2.0), (1.0, 2.0, 4.0, 3.0), (2.0, 2.0, 4.0, 3.0));
		band.Columns[1] = band.Columns[1] with { BreakCount = 1, LostCount = 2 };

		var connected = OverviewGeometry.Connected(band);

		connected.Columns.Should().HaveCount(4);
		connected.Columns[1].Start.Should().Be(_first.AddDays(1));
		double.IsNaN(connected.Columns[1].Min).Should().BeTrue();
		connected.Columns[2].Should().Be(band.Columns[1]);
		OverviewGeometry.BandFigures(connected, _first, _first.AddDays(2), BandWidth, BandHeight).Should().HaveCount(2);
	}

	[Fact]
	public void TimeAtFractionHalfReturnsExtentMidpoint()
	{
		var time = OverviewGeometry.TimeAtFraction(_first, _last, 0.5);

		time.Should().Be(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));
	}

	[Theory]
	[InlineData(-0.5)]
	[InlineData(1.5)]
	public void TimeAtFractionOutOfRangeClampsToExtentEdge(double fraction)
	{
		var time = OverviewGeometry.TimeAtFraction(_first, _last, fraction);

		time.Should().BeOnOrAfter(_first);
		time.Should().BeOnOrBefore(_last);
	}

	[Theory]
	[InlineData(0.0, 0.0)]
	[InlineData(2.5, 0.25)]
	[InlineData(10.0, 1.0)]
	public void FractionAtPlacesATimeInsideTheExtentAlongIt(double daysAfterFirst, double expected)
	{
		OverviewGeometry.FractionAt(_first, _last, _first.AddDays(daysAfterFirst)).Should().Be(expected);
	}

	[Theory]
	[InlineData(-0.5)]
	[InlineData(10.5)]
	public void FractionAtOutsideTheExtentIsNull(double daysAfterFirst)
	{
		OverviewGeometry.FractionAt(_first, _last, _first.AddDays(daysAfterFirst)).Should().BeNull();
	}

	[Fact]
	public void FractionAtOverAnEmptyExtentIsNull()
	{
		OverviewGeometry.FractionAt(_first, _first, _first).Should().BeNull();
	}

	private static IntensitySeries Band(params (double Days, double Min, double Max, double Last)[] columns)
	{
		IntensityColumn[] bandColumns =
		[
			.. columns.Select(column => new IntensityColumn(
				_first.AddDays(column.Days),
				column.Min,
				column.Max,
				double.IsNaN(column.Min) ? 0 : 1,
				0,
				column.Last,
				0,
				0,
				0,
				BoardState.None))
		];

		return new IntensitySeries(
			_first.AddDays(columns[0].Days),
			_first.AddDays(columns[^1].Days + 1),
			TimeSpan.FromDays(1),
			bandColumns);
	}
}
