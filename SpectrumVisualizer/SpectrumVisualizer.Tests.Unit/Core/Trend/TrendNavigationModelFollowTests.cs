using AwesomeAssertions;

using SpectrumVisualizer.Core.Trend;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Trend;

[Trait("Component", "Core")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendNavigationModelFollowTests : TrendNavigationModelTestBase
{
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
		var model = new TrendNavigationModel(_origin.AddMinutes(-10), _origin, firstSample);
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
}
