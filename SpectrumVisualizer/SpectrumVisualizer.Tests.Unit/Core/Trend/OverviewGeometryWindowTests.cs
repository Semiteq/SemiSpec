using AwesomeAssertions;

using SpectrumVisualizer.Core.Trend;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Trend;

[Trait("Component", "Core")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class OverviewGeometryWindowTests : OverviewGeometryTestBase
{
	private const double StripWidth = 900.0;
	private const double MinimumHighlightWidth = 6.0;

	[Fact]
	public void WindowFractionMidWindowMapsToCenteredRectangle()
	{
		var from = new DateTimeOffset(2026, 6, 12, 0, 0, 0, TimeSpan.Zero);
		var to = new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero);

		var (start, width) = OverviewGeometry.WindowFraction(First, Last, from, to);

		start.Should().BeApproximately(0.2, 1e-9);
		width.Should().BeApproximately(0.4, 1e-9);
	}

	[Fact]
	public void WindowFractionWindowEqualsExtentFillsStrip()
	{
		var (start, width) = OverviewGeometry.WindowFraction(First, Last, First, Last);

		start.Should().Be(0.0);
		width.Should().BeApproximately(1.0, 1e-9);
	}

	[Fact]
	public void WindowFractionWindowReachesBeyondExtentClampsToStrip()
	{
		var from = First - TimeSpan.FromDays(5);
		var to = Last + TimeSpan.FromDays(5);

		var (start, width) = OverviewGeometry.WindowFraction(First, Last, from, to);

		start.Should().Be(0.0);
		width.Should().Be(1.0);
	}

	[Fact]
	public void WindowFractionZeroSpanExtentReturnsFullStrip()
	{
		var (start, width) = OverviewGeometry.WindowFraction(First, First, First, First);

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
	public void TimeAtFractionHalfReturnsExtentMidpoint()
	{
		var time = OverviewGeometry.TimeAtFraction(First, Last, 0.5);

		time.Should().Be(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));
	}

	[Theory]
	[InlineData(-0.5)]
	[InlineData(1.5)]
	public void TimeAtFractionOutOfRangeClampsToExtentEdge(double fraction)
	{
		var time = OverviewGeometry.TimeAtFraction(First, Last, fraction);

		time.Should().BeOnOrAfter(First);
		time.Should().BeOnOrBefore(Last);
	}
}
