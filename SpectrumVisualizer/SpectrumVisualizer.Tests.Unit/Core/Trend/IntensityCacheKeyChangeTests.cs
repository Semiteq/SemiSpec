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
public sealed class IntensityCacheKeyChangeTests : IntensityCacheTestBase
{
	[Theory]
	[InlineData(OtherNm, SampleDelta)]
	[InlineData(SampleNm, 10)]
	public void AKeyChangeRecomputesTheRecentSpectraAndAsksForAReRead(ushort nm, byte delta)
	{
		AddSpectra(0, 10);

		Cache.Add(StoreSamples.Spectrum(At(10), nm: nm, delta: delta, seq: 10));

		var expected = RampMean(Spectrometer.Atp8000, nm, delta);
		SecondColumns(0, 11).Should().AllSatisfy(column =>
		{
			column.Count.Should().Be(1);
			column.Min.Should().Be(expected);
		});
		Requests.Should().Equal(new ReReadRequest(1, new TrendKey(nm, delta), Start, ReadsHistory: true));
	}

	[Fact]
	public void AKeyChangeDropsThePointsOlderThanTheRecentSpectraAndKeepsEveryBreakAndMark()
	{
		AddSpectra(0, 20);
		AddSpectra(20, 200, seqStep: 3);
		Cache.Add(new ReceivedFrame(FailedHeartbeat(30 + 2), At(30.5)));

		Cache.Add(StoreSamples.Spectrum(At(200), nm: OtherNm, seq: 202));

		Requests.Single().RecentStart.Should().Be(At(80));
		var older = SecondColumns(0, 80);
		older.Sum(column => column.Count + column.UncoveredCount).Should().Be(0);
		older[20].Should().Match<IntensityColumn>(column => column.BreakCount == 1 && column.LostCount == 2);
		older[30].Failed.Should().Be(BoardState.Atp8000MeasurementFailed);
		SecondColumns(80, 201).Should().AllSatisfy(column =>
			column.Min.Should().Be(RampMean(Spectrometer.Atp8000, OtherNm, SampleDelta)));
	}

	[Fact]
	public void AReturnToThePreviousKeyBeforeAChunkLandsRestoresItsPointsWithoutAReRead()
	{
		AddSpectra(0, 200);
		AddSpectra(200, 230, nm: OtherNm);

		Cache.Add(StoreSamples.Spectrum(At(230), seq: 230));

		var expected = RampMean(Spectrometer.Atp8000, SampleNm, SampleDelta);
		SecondColumns(0, 231).Should().AllSatisfy(column =>
		{
			column.Count.Should().Be(1);
			column.Min.Should().Be(expected);
		});
		Requests.Should().Equal(
			new ReReadRequest(1, new TrendKey(OtherNm, SampleDelta), At(80), ReadsHistory: true),
			new ReReadRequest(2, new TrendKey(SampleNm, SampleDelta), At(80), ReadsHistory: false));
	}

	[Fact]
	public void AReturnAfterAChunkLandedAsksForAReRead()
	{
		AddSpectra(0, 200);
		Cache.Add(StoreSamples.Spectrum(At(200), nm: OtherNm, seq: 200));
		Cache.LandHistory(1, Chunk(0, 80, value: 1));

		Cache.Add(StoreSamples.Spectrum(At(201), seq: 201));

		Requests.Should().HaveCount(2);
		Requests[1].Should().Match<ReReadRequest>(request => request.Generation == 2 && request.ReadsHistory);
		SecondColumns(0, 80).Sum(column => column.Count).Should().Be(0);
	}

	[Fact]
	public void AReturnAtTheEndOfTheReturnWindowRestoresTheKeyWithoutARead()
	{
		AddSpectra(0, 200);
		AddSpectra(200, 320, nm: OtherNm);

		Cache.Add(StoreSamples.Spectrum(At(320), seq: 64));

		Requests.Should().HaveCount(2);
		Requests[1].ReadsHistory.Should().BeFalse("the kept points end at 80 s and the window reaches 240 s past that");
	}

	[Fact]
	public void AReturnPastTheReturnWindowAsksForAReReadOfThePointsBeforeTheRecentSpectra()
	{
		AddSpectra(0, 200);
		AddSpectra(200, 321, nm: OtherNm);

		Cache.Add(StoreSamples.Spectrum(At(321), seq: 65));

		Requests.Should().HaveCount(2);
		Requests[1].ReadsHistory.Should().BeTrue();
		Requests[1].RecentStart.Should().Be(At(201), "the recent spectra hold the last 120 s again");
	}

	[Fact]
	public void AReturnToAKeyWhoseHistoryWasReadInFullRestoresItWithoutARead()
	{
		AddSpectra(0, 200);
		Cache.Add(StoreSamples.Spectrum(At(200), nm: OtherNm, seq: 200));
		Cache.LandHistory(1, Chunk(0, 80, value: 1));
		Cache.CompleteHistory(1);
		Cache.Add(StoreSamples.Spectrum(At(201), nm: ThirdNm, seq: 201));

		Cache.Add(StoreSamples.Spectrum(At(202), nm: OtherNm, seq: 202));

		Requests.Select(request => request.ReadsHistory).Should().Equal(true, true, false);
		SecondColumns(0, 80).Sum(column => column.Count).Should().Be(80, "the landed history came back with the key");
	}

	[Fact]
	public void ACompleteMarkOfAnOlderGenerationLeavesTheHistoryIncomplete()
	{
		AddSpectra(0, 200);
		Cache.Add(StoreSamples.Spectrum(At(200), nm: OtherNm, seq: 200));
		Cache.Add(StoreSamples.Spectrum(At(201), nm: ThirdNm, seq: 201));
		Cache.CompleteHistory(1);
		Cache.Add(StoreSamples.Spectrum(At(202), nm: SampleNm, seq: 202));

		Cache.Add(StoreSamples.Spectrum(At(203), nm: ThirdNm, seq: 203));

		Requests.Select(request => request.ReadsHistory).Should().Equal(true, true, true, true);
	}

	[Fact]
	public void AChangeAfterAReturnAsksForAReRead()
	{
		AddSpectra(0, 200);
		Cache.Add(StoreSamples.Spectrum(At(200), nm: OtherNm, seq: 200));
		Cache.Add(StoreSamples.Spectrum(At(201), seq: 201));

		Cache.Add(StoreSamples.Spectrum(At(202), nm: OtherNm, seq: 202));

		Requests.Select(request => request.ReadsHistory).Should().Equal(true, false, true);
		Requests[2].Generation.Should().Be(3);
	}
}
