using AwesomeAssertions;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Trend;

[Trait("Component", "Core")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendNavigationModelZoomTests : TrendNavigationModelTestBase
{
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
}
