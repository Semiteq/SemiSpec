using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.Tests.Unit.Core.Store;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Store.StoreSamples;

namespace SpectrumVisualizer.Tests.Unit.Core.Trend;

[Trait("Component", "Core")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class IntensityCachePointTests : IntensityCacheTestBase
{
	[Fact]
	public void ASpectrumAddsOnePointAtTheFrameMean()
	{
		Cache.Add(StoreSamples.Spectrum(Start));

		var column = SecondColumns(0, 1).Single();

		column.Count.Should().Be(1);
		column.Min.Should().Be(RampMean(Spectrometer.Atp8000, SampleNm, SampleDelta));
		column.Max.Should().Be(column.Min);
	}

	[Fact]
	public void ADarkSpectrumAddsAPointAtZero()
	{
		Cache.Add(Dark(Start));

		var column = SecondColumns(0, 1).Single();

		column.Count.Should().Be(1);
		column.Min.Should().Be(0);
		column.Max.Should().Be(0);
	}

	[Fact]
	public void ASpectrumOfADeviceThatDoesNotCoverTheWavelengthAddsAnUncoveredPoint()
	{
		Cache.Add(StoreSamples.Spectrum(Start, Spectrometer.Atp2000));

		var column = SecondColumns(0, 1).Single();

		column.Count.Should().Be(0);
		column.UncoveredCount.Should().Be(1);
	}

	[Fact]
	public void BothDevicesAlternatingAtAWavelengthBothCoverDrawOneLine()
	{
		const ushort CoveredByBothNm = 950;
		for (var i = 0; i < 20; i++)
		{
			var device = i % 2 == 0 ? Spectrometer.Atp2000 : Spectrometer.Atp8000;
			Cache.Add(StoreSamples.Spectrum(At(i), device, nm: CoveredByBothNm, seq: (byte)i));
		}

		var series = Cache.Fold(Query(CoveredByBothNm, SampleDelta, At(0), At(20), Second));

		series.Columns.Should().AllSatisfy(column =>
		{
			column.Count.Should().Be(1);
			column.UncoveredCount.Should().Be(0);
			column.BreakCount.Should().Be(0);
		});
		series.Columns.Should().HaveCount(20)
			.And.OnlyContain(column => !TrendColumns.OpensGap(column), "no gap column breaks the line");
		Requests.Should().BeEmpty("the key stays the same across the device switch");
	}

	[Fact]
	public void AHeartbeatAddsNoPoint()
	{
		Cache.Add(Heartbeat(Start));

		var column = SecondColumns(0, 1).Single();

		column.Count.Should().Be(0);
		column.UncoveredCount.Should().Be(0);
		Cache.Extent.Should().BeNull();
	}

	[Fact]
	public void TheFirstSpectrumSetsTheKeyWithoutAReRead()
	{
		Cache.Add(StoreSamples.Spectrum(Start, nm: OtherNm));

		Requests.Should().BeEmpty();
		SecondColumns(0, 1).Single().Min.Should().Be(RampMean(Spectrometer.Atp8000, OtherNm, SampleDelta));
	}

	[Fact]
	public void PointsBreaksAndMarksOlderThanADayBeforeNowAreDropped()
	{
		Cache.Add(StoreSamples.Spectrum(At(0), seq: 1));
		Cache.Add(new ReceivedFrame(FailedHeartbeat(1), At(0.5)));
		Cache.Add(StoreSamples.Spectrum(At(1), seq: 5));
		var now = At(1) + TimeSpan.FromDays(1) + TimeSpan.FromMilliseconds(1);
		Time.SetUtcNow(now);

		Cache.Add(StoreSamples.Spectrum(now, seq: 6));

		SecondColumns(0, 2).Should().AllSatisfy(column =>
			column.Should().Match<IntensityColumn>(c =>
				c.Count == 0 && c.BreakCount == 0 && c.Failed == BoardState.None));
		Cache.Extent!.Value.First.Should().Be(now);
	}

	[Fact]
	public void AFoldOfADayCountsOnlyThePointsOfItsRange()
	{
		const int Day = 86_400;
		var spectrum = StoreSamples.Spectrum(Start);
		for (var i = 0; i < Day; i++)
		{
			Cache.Add(spectrum with { Frame = spectrum.Frame with { Seq = (byte)i }, ReceivedAt = At(i) });
		}

		var lastTenMinutes = Cache.Fold(Query(SampleNm, SampleDelta, At(Day - 600), At(Day), Width(600, 2048)));
		var wholeDay = Cache.Fold(Query(SampleNm, SampleDelta, Start, At(Day), Width(Day, 2048)));

		lastTenMinutes.Columns.Sum(column => column.Count).Should().Be(600);
		wholeDay.Columns.Sum(column => column.Count).Should().Be(Day);
	}

	[Fact]
	public void TheMomentLookupReadsTheRecentSpectraAtOrBefore()
	{
		AddSpectra(0, 200);

		var found = Cache.SpectrumAt(At(150.5));

		found.Should().NotBeNull();
		found!.ReceivedAt.Should().Be(At(150));
		found.Seq.Should().Be(150);
		Cache.SpectrumAt(At(78.999)).Should().BeNull("the moment is before the recent spectra");
		Cache.SpectrumAt(At(500))!.ReceivedAt.Should().Be(At(199));
	}
}
