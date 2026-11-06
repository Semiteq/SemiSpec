using AwesomeAssertions;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Store;
using SemiSpec.Core.Trend;
using SemiSpec.Tests.Unit.Core.Store;

using Xunit;

using static SemiSpec.Tests.Unit.Core.Store.StoreSamples;

namespace SemiSpec.Tests.Unit.Core.Trend;

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
		Requests.Should().Equal(new ReReadRequest(1, new TrendKey(nm, delta), Start));
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
	public void AReturnToThePreviousKeyAsksForAReReadLikeAnyChange()
	{
		AddSpectra(0, 200);
		AddSpectra(200, 230, nm: OtherNm);

		Cache.Add(StoreSamples.Spectrum(At(230), seq: 230));

		Requests.Should().Equal(
			new ReReadRequest(1, new TrendKey(OtherNm, SampleDelta), At(80)),
			new ReReadRequest(2, new TrendKey(SampleNm, SampleDelta), At(110)));
		SecondColumns(0, 110).Sum(column => column.Count).Should().Be(0, "the older points come with the re-read");
		SecondColumns(110, 231).Should().AllSatisfy(column => column.Count.Should().Be(1));
	}
}
