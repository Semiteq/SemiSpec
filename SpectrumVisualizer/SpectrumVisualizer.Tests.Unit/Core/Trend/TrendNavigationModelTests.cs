using AwesomeAssertions;

using SpectrumVisualizer.Core.Trend;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Trend;

[Trait("Component", "Core")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendNavigationModelTests
{
	private static readonly DateTimeOffset _origin = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset _firstSample = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);

	[Fact]
	public void OnLiveEdgeWhenStickyAdvancesWindowAndKeepsWidth()
	{
		var model = Model(isSticky: true, windowWidth: TimeSpan.FromMinutes(10));
		var width = model.Width;
		var now = _origin.AddMinutes(5);

		model.OnLiveEdge(now);

		model.To.Should().Be(now);
		model.Width.Should().Be(width);
		model.From.Should().Be(now - width);
		model.IsSticky.Should().BeTrue();
	}

	[Fact]
	public void OnLiveEdgeWhenNotStickyLeavesWindowUnchanged()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromMinutes(10));
		var from = model.From;
		var to = model.To;

		model.OnLiveEdge(_origin.AddMinutes(5));

		model.From.Should().Be(from);
		model.To.Should().Be(to);
	}

	[Fact]
	public void OnLiveEdgeLeavesAnEmptySpanBeforeTheFirstSample()
	{
		var firstSample = _origin.AddMinutes(-2);
		var model = new TrendNavigationModel(_origin.AddMinutes(-10), _origin, firstSample, isSticky: true);
		var now = _origin.AddMinutes(1);

		model.OnLiveEdge(now);

		model.From.Should().Be(now.AddMinutes(-10));
		model.From.Should().BeBefore(firstSample);
	}

	[Fact]
	public void JumpToNowReattachesStickyAndPlacesNowAtRightEdge()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromMinutes(10));
		var width = model.Width;
		var now = _origin.AddHours(3);

		model.JumpToNow(now);

		model.IsSticky.Should().BeTrue();
		model.To.Should().Be(now);
		model.From.Should().Be(now - width);
		model.Width.Should().Be(width);
	}

	[Fact]
	public void DetachStickyStopsFollowingAndKeepsTheWindow()
	{
		var model = Model(isSticky: true, windowWidth: TimeSpan.FromMinutes(10));
		var from = model.From;

		model.DetachSticky();

		model.IsSticky.Should().BeFalse();
		model.From.Should().Be(from);
	}

	[Fact]
	public void PanPastLiveEdgeIntoThePastAutoDetachesSticky()
	{
		var model = Model(isSticky: true, windowWidth: TimeSpan.FromMinutes(10));
		var now = model.To;

		model.Pan(TimeSpan.FromMinutes(-15), now);

		model.IsSticky.Should().BeFalse();
		(now > model.To).Should().BeTrue();
	}

	[Fact]
	public void PanForwardPastLiveEdgeWithTheLiveEdgeBeforeTheWindowStartAutoDetachesSticky()
	{
		var model = Model(isSticky: true, windowWidth: TimeSpan.FromMinutes(10));
		var now = model.To;

		model.Pan(TimeSpan.FromMinutes(30), now);

		model.IsSticky.Should().BeFalse();
		(now < model.From).Should().BeTrue();
	}

	[Fact]
	public void PanWhileLiveEdgeStaysInsideWindowKeepsSticky()
	{
		var model = Model(isSticky: true, windowWidth: TimeSpan.FromMinutes(10));
		var now = model.To - TimeSpan.FromMinutes(6);

		model.Pan(TimeSpan.FromMinutes(-3), now);

		model.IsSticky.Should().BeTrue();
		(now <= model.To && now >= model.From).Should().BeTrue();
	}

	[Fact]
	public void PanBackBeforeFirstSampleClampsFromToFirstSample()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromMinutes(10));
		var width = model.Width;

		model.Pan(TimeSpan.FromDays(-2), now: _origin);

		model.From.Should().Be(_firstSample);
		model.Width.Should().Be(width);
		model.To.Should().Be(_firstSample + width);
	}

	[Fact]
	public void ZoomOutBeyondOneDayClampsWidthToOneDay()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromHours(10));

		model.Zoom(factor: 10, anchor: model.To, now: _origin);

		model.Width.Should().Be(TimeSpan.FromDays(1));
	}

	[Fact]
	public void ZoomInBelowTenSecondsClampsWidthToTenSeconds()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromSeconds(20));

		model.Zoom(factor: 0.1, anchor: model.From, now: _origin);

		model.Width.Should().Be(TimeSpan.FromSeconds(10));
	}

	[Fact]
	public void ZoomHoldsAnchorPositionWhileScalingWidth()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromMinutes(10));
		var anchor = model.From + TimeSpan.FromMinutes(2);
		var anchorFractionBefore = (anchor - model.From) / model.Width;

		model.Zoom(factor: 0.5, anchor: anchor, now: _origin);

		// The width snaps onto the zoom ladder, near the requested half-width; the anchor keeps its relative position.
		var anchorFractionAfter = (anchor - model.From) / model.Width;
		anchorFractionAfter.Should().BeApproximately(anchorFractionBefore, 1e-9);
		model.Width.Should().BeCloseTo(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1));
	}

	[Fact]
	public void ZoomInThenOutReturnsToOriginWindowWithinTolerance()
	{
		var model = Model(isSticky: false, windowWidth: TimeSpan.FromHours(1));
		var anchor = model.From + (model.Width / 2);

		// The origin is taken after one zoom snaps onto the ladder; 1 h itself is not a ladder point.
		model.Zoom(factor: 0.8, anchor: anchor, now: _origin);
		model.Zoom(factor: 1.25, anchor: anchor, now: _origin);
		var fromOrigin = model.From;
		var toOrigin = model.To;
		var widthOrigin = model.Width;

		for (var notch = 0; notch < 8; notch++)
		{
			model.Zoom(factor: 0.8, anchor: anchor, now: _origin);
		}

		for (var notch = 0; notch < 8; notch++)
		{
			model.Zoom(factor: 1.25, anchor: anchor, now: _origin);
		}

		model.Width.Should().BeCloseTo(widthOrigin, TimeSpan.FromMilliseconds(1));
		model.From.Should().BeCloseTo(fromOrigin, TimeSpan.FromMilliseconds(1));
		model.To.Should().BeCloseTo(toOrigin, TimeSpan.FromMilliseconds(1));
	}

	[Fact]
	public void ZoomOutFarPastClampsFromToFirstSample()
	{
		var model = Model(isSticky: true, windowWidth: TimeSpan.FromHours(1));
		var anchor = model.From + TimeSpan.FromMinutes(30);

		for (var notch = 0; notch < 40; notch++)
		{
			model.Zoom(factor: 1.25, anchor: anchor, now: _origin);
		}

		model.From.Should().BeOnOrAfter(_firstSample);
	}

	[Fact]
	public void ConstructorClampsInitialWidthAboveOneDay()
	{
		var from = _firstSample;
		var to = from.AddDays(2);

		var model = new TrendNavigationModel(from, to, _firstSample, isSticky: false);

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

	[Fact]
	public void AZoomThatLeavesNowOutsideStopsFollowing()
	{
		var model = Model(isSticky: true, windowWidth: TimeSpan.FromMinutes(10));

		model.Zoom(factor: 0.8, anchor: model.From, now: _origin);

		model.IsSticky.Should().BeFalse();
	}

	[Fact]
	public void AZoomThatKeepsNowInsideKeepsFollowing()
	{
		var model = Model(isSticky: true, windowWidth: TimeSpan.FromMinutes(10));

		model.Zoom(factor: 0.8, anchor: model.To, now: _origin);

		model.IsSticky.Should().BeTrue();
		model.To.Should().Be(_origin);
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

	private static TrendNavigationModel Model(bool isSticky, TimeSpan windowWidth)
	{
		return new TrendNavigationModel(_origin - windowWidth, _origin, _firstSample, isSticky);
	}
}
