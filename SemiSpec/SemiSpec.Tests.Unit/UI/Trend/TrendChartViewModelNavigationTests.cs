using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using ScottPlot.TickGenerators;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Trend;

[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendChartViewModelNavigationTests : TrendChartTestBase
{
	private static readonly TimeSpan _tenMinutes = TimeSpan.FromMinutes(10);

	[AvaloniaFact]
	public void TheDefaultWindowIsTenMinutesAndFollowsLive()
	{
		Chart.Width.Should().Be(_tenMinutes);
		Chart.IsFollowing.Should().BeTrue();
		Chart.HasPoints.Should().BeFalse();
		Chart.Plot.Axes.Bottom.TickGenerator.Should().BeOfType<DateTimeAutomatic>();

		AddSpectra(0, 1);
		Sample();

		Chart.HasPoints.Should().BeTrue();
		Chart.To.Should().Be(At(0));
		Chart.Width.Should().Be(_tenMinutes);
	}

	[AvaloniaFact]
	public void ANewFrameMovesTheWindowWhileFollowing()
	{
		AddSpectra(0, 1);
		Sample();

		AddSpectra(1, 1);
		Sample();

		Chart.To.Should().Be(At(1));
		Chart.From.Should().Be(At(1) - _tenMinutes);
	}

	[AvaloniaFact]
	public void WhileFollowingASilenceShowsAsAnEmptySpanWithinASecond()
	{
		AddSpectra(0, 60);
		Sample();
		Time.Advance(TimeSpan.FromSeconds(5));

		Scheduler.AdvanceBy(Second.Ticks);

		Chart.To.Should().Be(At(59) + TimeSpan.FromSeconds(5), "the live edge is now, not the newest spectrum");
		Chart.Width.Should().Be(_tenMinutes);
		Chart.IsFollowing.Should().BeTrue();
	}

	[AvaloniaFact]
	public void APausedWindowStaysPutWhileTheClockRuns()
	{
		AddSpectra(0, 1800);
		Sample();
		Chart.PanBy(TimeSpan.FromMinutes(-5));
		var (from, to) = (Chart.From, Chart.To);
		Time.Advance(TimeSpan.FromSeconds(5));

		Scheduler.AdvanceBy(Second.Ticks);

		Chart.From.Should().Be(from);
		Chart.To.Should().Be(to);
	}

	[AvaloniaFact]
	public void APanIntoThePastStopsFollowingAndANewFrameKeepsTheWindow()
	{
		AddSpectra(0, 1800);
		Sample();

		Chart.PanBy(TimeSpan.FromMinutes(-5));
		var (from, to) = (Chart.From, Chart.To);
		AddSpectra(1800, 1);
		Sample();

		Chart.IsFollowing.Should().BeFalse();
		Chart.From.Should().Be(from);
		Chart.To.Should().Be(to);
		to.Should().Be(At(1799) - TimeSpan.FromMinutes(5));
	}

	[AvaloniaFact]
	public void FollowLiveReturnsToTheNewestFrame()
	{
		AddSpectra(0, 1800);
		Sample();
		Chart.PanBy(TimeSpan.FromMinutes(-5));
		AddSpectra(1800, 1);
		Sample();

		Chart.FollowLive();

		Chart.IsFollowing.Should().BeTrue();
		Chart.To.Should().Be(At(1800));
		Chart.Width.Should().Be(_tenMinutes);
	}

	[AvaloniaFact]
	public void AWheelStepZoomsAroundTheCursorTime()
	{
		AddSpectra(0, 1800);
		Sample();
		Chart.PanBy(TimeSpan.FromMinutes(-10));
		var anchor = Chart.From + (Chart.Width / 4);
		var fraction = (anchor - Chart.From) / Chart.Width;

		Chart.ZoomAt(0.8, anchor);

		Chart.Width.Should().BeLessThan(_tenMinutes);
		((anchor - Chart.From) / Chart.Width).Should().BeApproximately(fraction, 1e-6);
		Chart.IsFollowing.Should().BeFalse();
	}

	[AvaloniaFact]
	public void AZoomInWhileFollowingThatLeavesTheNewestPointOutsideStopsFollowing()
	{
		AddSpectra(0, 1800);
		Sample();

		Chart.ZoomAt(0.8, Chart.From + (Chart.Width / 2));

		Chart.IsFollowing.Should().BeFalse();
		Chart.To.Should().BeBefore(At(1799));
	}

	[AvaloniaFact]
	public void AWheelInAtTheRightEdgeWhileFollowingKeepsFollowing()
	{
		AddSpectra(0, 1800);
		Sample();
		Time.Advance(TimeSpan.FromMilliseconds(500));

		Chart.ZoomAt(0.8, Chart.To);

		Chart.IsFollowing.Should().BeTrue("the drawn right edge is now once the window moves onto the clock");
		Chart.To.Should().Be(Time.GetUtcNow());
		Chart.Width.Should().BeLessThan(_tenMinutes);
	}

	[AvaloniaFact]
	public void AShortPanTowardNowWhileFollowingKeepsFollowing()
	{
		AddSpectra(0, 120);
		Sample();
		Chart.SetWidth(TimeSpan.FromMinutes(1));
		Time.Advance(TimeSpan.FromMilliseconds(500));

		Chart.PanBy(TimeSpan.FromMilliseconds(200));

		Chart.IsFollowing.Should().BeTrue("the window still holds now after the pan");
	}

	[AvaloniaFact]
	public void ThePresetSetsTheWidthAndKeepsFollowing()
	{
		AddSpectra(0, 60);
		Sample();

		Chart.SetWidth(TimeSpan.FromHours(1));

		Chart.Width.Should().Be(TimeSpan.FromHours(1));
		Chart.IsFollowing.Should().BeTrue();
		Chart.To.Should().Be(At(59));
	}

	[AvaloniaFact]
	public void FollowingLeavesAnEmptySpanBeforeTheFirstPoint()
	{
		AddSpectra(0, 120);
		Sample();

		Chart.From.Should().Be(At(119) - _tenMinutes);
		Chart.From.Should().BeBefore(At(0));
	}

	[AvaloniaFact]
	public void APanStopsAtTheFirstPoint()
	{
		AddSpectra(0, 1800);
		Sample();

		Chart.PanBy(TimeSpan.FromMinutes(-25));

		Chart.From.Should().Be(At(0));
		Chart.Width.Should().Be(_tenMinutes);
		Chart.IsFollowing.Should().BeFalse();
	}

	[AvaloniaFact]
	public void AZoomOutStopsAtTheFirstPoint()
	{
		AddSpectra(0, 1800);
		Sample();
		Chart.PanBy(TimeSpan.FromMinutes(-19));

		Chart.ZoomAt(1.25, Chart.To);

		Chart.From.Should().Be(At(0));
		Chart.Width.Should().BeGreaterThan(_tenMinutes);
		Chart.IsFollowing.Should().BeFalse();
	}
}
