using AwesomeAssertions;

using SpectrumVisualizer.Core.Trend;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Trend;

[Trait("Component", "Core")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendHistoryPlanTests
{
	private static readonly DateTimeOffset _newest = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset _recentStart = _newest - TimeSpan.FromSeconds(120);
	private static readonly DateTimeOffset _dayStart = _newest - TimeSpan.FromDays(1);

	[Fact]
	public void TheVisibleRangeComesFirst()
	{
		var plan = TrendHistoryPlan.For(_newest - TimeSpan.FromMinutes(10), _newest, _newest, _recentStart);

		plan[0].Should().Be(new TimeRange(_newest - TimeSpan.FromMinutes(10), _recentStart));
	}

	[Fact]
	public void TheRestFollowsNewestFirstInHourChunks()
	{
		var visibleFrom = _newest - TimeSpan.FromHours(5.5);
		var visibleTo = visibleFrom + TimeSpan.FromMinutes(10);

		var plan = TrendHistoryPlan.For(visibleFrom, visibleTo, _newest, _recentStart);

		plan[1].Should().Be(new TimeRange(_recentStart - TimeSpan.FromHours(1), _recentStart));
		plan.Skip(1).Should().AllSatisfy(range =>
			(range.To - range.From).Should().BeLessThanOrEqualTo(TimeSpan.FromHours(1)));
		plan.Skip(1).Select(range => range.From).Should().BeInDescendingOrder();
		plan[^1].From.Should().Be(_dayStart);
	}

	[Fact]
	public void EveryRangeEndsAtTheRecentSpectraStart()
	{
		var plan = TrendHistoryPlan.For(_newest - TimeSpan.FromHours(1), _newest, _newest, _recentStart);

		plan.Should().AllSatisfy(range => range.To.Should().BeOnOrBefore(_recentStart));
		plan.Max(range => range.To).Should().Be(_recentStart);
	}

	[Fact]
	public void AVisibleRangeInsideTheRecentSpectraReadsNothingFirst()
	{
		var plan = TrendHistoryPlan.For(_newest - TimeSpan.FromSeconds(60), _newest, _newest, _recentStart);

		plan[0].Should().Be(new TimeRange(_recentStart - TimeSpan.FromHours(1), _recentStart));
	}

	[Theory]
	[InlineData(-600, 0)]
	[InlineData(-19_800, -19_200)]
	[InlineData(-86_400, -85_800)]
	[InlineData(-90_000, -89_400)]
	[InlineData(-60, 0)]
	public void TheRangesCoverTheDayWithoutOverlap(int visibleFromSeconds, int visibleToSeconds)
	{
		var plan = TrendHistoryPlan.For(
			_newest.AddSeconds(visibleFromSeconds), _newest.AddSeconds(visibleToSeconds), _newest, _recentStart);

		plan.Should().AllSatisfy(range => range.To.Should().BeAfter(range.From));
		var ordered = plan.OrderBy(range => range.From).ToList();
		ordered[0].From.Should().Be(_dayStart);
		ordered[^1].To.Should().Be(_recentStart);
		ordered.Zip(ordered.Skip(1)).Should().AllSatisfy(pair => pair.Second.From.Should().Be(pair.First.To));
	}
}
