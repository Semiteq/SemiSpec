using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.UI.Trend;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Trend;

[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class OverviewViewModelTests : TrendChartTestBase
{
	private const double FractionTolerance = 0.01;

	private static readonly TimeSpan _refoldFloor = TimeSpan.FromSeconds(2);

	private readonly OverviewViewModel _overview;

	public OverviewViewModelTests()
	{
		_overview = NewOverview();
	}

	[AvaloniaFact]
	public void BeforeTheFirstSpectrumThereIsNoBandAndNoExtent()
	{
		_overview.Band.Should().BeNull();
		_overview.HasExtent.Should().BeFalse();
		_overview.WindowStartFraction.Should().Be(0.0);
		_overview.WindowWidthFraction.Should().Be(1.0);
	}

	[AvaloniaFact]
	public void TheBandSpansTheCacheExtent()
	{
		AddSpectra(0, 1800);
		Sample();
		Refold();

		var band = _overview.Band!;
		_overview.HasExtent.Should().BeTrue();
		_overview.ExtentLast.Should().Be(At(1799));
		_overview.ExtentFirst.Should().Be(band.Columns[0].Start);
		_overview.ExtentFirst.Should().BeOnOrBefore(At(0));
		band.Columns.Length.Should().BeLessThanOrEqualTo(OverviewViewModel.BandColumns + 2);
		band.Columns[^1].Start.Should().BeOnOrBefore(At(1799));
		band.Columns.Sum(column => column.Count).Should().Be(1800);
	}

	[AvaloniaFact]
	public void TheWindowFractionsFollowTheTrendWindow()
	{
		AddSpectra(0, 1800);
		Sample();
		Refold();

		_overview.WindowStartFraction.Should().BeApproximately(2.0 / 3.0, FractionTolerance);
		_overview.WindowWidthFraction.Should().BeApproximately(1.0 / 3.0, FractionTolerance);

		Chart.PanBy(TimeSpan.FromMinutes(-10));

		_overview.WindowStartFraction.Should().BeApproximately(1.0 / 3.0, FractionTolerance);
		_overview.WindowWidthFraction.Should().BeApproximately(1.0 / 3.0, FractionTolerance);
	}

	[AvaloniaFact]
	public void ANavigationCentresTheTrendWindowThereAndStopsFollowing()
	{
		AddSpectra(0, 1800);
		Sample();
		Refold();
		var target = OverviewGeometry.TimeAtFraction(_overview.ExtentFirst, _overview.ExtentLast, 0.5);

		_overview.NavigateToFraction(0.5);

		(Chart.From + (Chart.Width / 2)).Should().BeCloseTo(target, TimeSpan.FromMilliseconds(1));
		Chart.Width.Should().Be(TimeSpan.FromMinutes(10));
		Chart.IsFollowing.Should().BeFalse();
	}

	[AvaloniaFact]
	public void ANavigationNearTheNewestPointStillStopsFollowing()
	{
		AddSpectra(0, 1800);
		Sample();
		Refold();

		_overview.NavigateToFraction(1.0);
		var to = Chart.To;
		AddSpectra(1800, 10);
		Sample();

		Chart.IsFollowing.Should().BeFalse();
		Chart.To.Should().Be(to, "a window that does not follow keeps its place");
	}

	[AvaloniaFact]
	public void TheBandRefoldsOnTheCadenceOnly()
	{
		AddSpectra(0, 600);
		Sample();
		Refold();
		var band = _overview.Band;

		AddSpectra(600, 1);
		Sample();

		_overview.Band.Should().BeSameAs(band, "the band refolds on its cadence, not per frame");
		_overview.ExtentLast.Should().Be(At(600), "the extent follows the newest point at once");

		Refold();

		_overview.Band.Should().NotBeSameAs(band);
		_overview.Band!.Columns.Sum(column => column.Count).Should().Be(601);
	}

	[AvaloniaFact]
	public void TheBandRefreshesAfterAHistoryChunkLands()
	{
		AddSpectra(600, 60);
		Sample();
		Refold();
		var band = _overview.Band;
		var olderFrom = At(0);
		var olderTo = At(600);
		TrendPoint[] olderPoints = [.. Enumerable.Range(0, 600).Select(second =>
			new TrendPoint(At(second).ToUnixTimeMilliseconds(), 7.0))];

		Cache.LandHistory(0, new IntensityChunk(olderFrom, olderTo, olderPoints));
		Scheduler.AdvanceBy(1);

		_overview.Band.Should().NotBeSameAs(band, "a landed chunk refolds without waiting for the cadence");
		_overview.ExtentFirst.Should().BeOnOrBefore(At(0));
		_overview.Band!.Columns.Sum(column => column.Count).Should().Be(660);
	}

	[AvaloniaFact]
	public void SparsePointsOverAShortExtentDrawOneConnectedBand()
	{
		AddSpectra(0, 10);
		Sample();
		Refold();

		var figures = OverviewGeometry.BandFigures(
			_overview.Band!, _overview.ExtentFirst, _overview.ExtentLast, 900.0, 36.0);

		figures.Should().ContainSingle("columns narrower than the frame period hold nothing and do not break the band");
	}

	protected override void Dispose(bool disposing)
	{
		_overview.Dispose();
		base.Dispose(disposing);
	}

	private void Refold()
	{
		Scheduler.AdvanceBy(_refoldFloor.Ticks);
	}
}
