using AwesomeAssertions;

using SemiSpec.Core.Trend;

using Xunit;

namespace SemiSpec.Tests.Unit.Core.Trend;

[Trait("Component", "Core")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendNavigationModelWidthTests : TrendNavigationModelTestBase
{
	[Fact]
	public void ConstructorClampsInitialWidthAboveOneDay()
	{
		var from = _firstSample;
		var to = from.AddDays(2);

		var model = new TrendNavigationModel(from, to, _firstSample);

		model.Width.Should().Be(TimeSpan.FromDays(1));
		model.From.Should().Be(from);
	}

	[Fact]
	public void SetWidthFiveMinutesKeepsTheRightEdgeAndSetsExactlyFiveMinutes()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromHours(1));
		var to = model.To;

		model.SetWidth(TimeSpan.FromMinutes(5));

		model.To.Should().Be(to);
		model.Width.Should().Be(TimeSpan.FromMinutes(5));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void SetWidthLeavesStickyAsItWas(bool isSticky)
	{
		var model = Model(isSticky, windowWidth: TimeSpan.FromHours(1));

		model.SetWidth(TimeSpan.FromMinutes(3));

		model.IsSticky.Should().Be(isSticky);
	}

	[Theory]
	[InlineData(2.0, 10.0)]
	[InlineData(2.0 * 86_400.0, 86_400.0)]
	public void SetWidthOutsideTheLimitsClampsTheWidth(double requestedSeconds, double expectedSeconds)
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromHours(1));

		model.SetWidth(TimeSpan.FromSeconds(requestedSeconds));

		model.Width.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
	}

	[Fact]
	public void SetWidthReachingBeforeFirstSampleStartsAtFirstSampleAndKeepsTheWidth()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromHours(1));
		var width = TimeSpan.FromHours(20);

		model.SetWidth(width);

		model.From.Should().Be(_firstSample);
		model.Width.Should().Be(width);
	}

	[Fact]
	public void SetWidthWhileStickyKeepsTheRightEdgePastTheFirstSample()
	{
		var model = Model(isSticky: true, windowWidth: TimeSpan.FromHours(1));
		var to = model.To;

		model.SetWidth(TimeSpan.FromHours(20));

		model.To.Should().Be(to);
		model.From.Should().Be(to - TimeSpan.FromHours(20));
	}

	// 5 min x 0.8 is 240 s; the nearest rung is 10 s x 1.25^14, about 227.4 s.
	[Fact]
	public void SetWidthThenOneWheelNotchLandsOnTheLadder()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromHours(1));
		model.SetWidth(TimeSpan.FromMinutes(5));

		model.Zoom(factor: 0.8, anchor: model.To, now: _origin);

		model.Width.Should().BeCloseTo(
			TimeSpan.FromSeconds(10 * Math.Pow(1.25, 14)), TimeSpan.FromMilliseconds(1));
	}
}
