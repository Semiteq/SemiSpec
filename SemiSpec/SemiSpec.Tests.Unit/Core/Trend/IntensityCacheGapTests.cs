using AwesomeAssertions;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Store;
using SemiSpec.Tests.Unit.Core.Store;

using Xunit;

namespace SemiSpec.Tests.Unit.Core.Trend;

[Trait("Component", "Core")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class IntensityCacheGapTests : IntensityCacheTestBase
{
	[Fact]
	public void AGapOfMoreThanTenSecondsBetweenPointsFoldsANoSpectraBreak()
	{
		Cache.LandHistory(0, Chunk(0, 10));
		Cache.LandHistory(0, Chunk(21, 30));

		var columns = SecondColumns(0, 30);

		NoSpectraColumnsOf(columns).Should().Equal(21);
		columns[21].BreakCount.Should().Be(1);
		columns[21].LostCount.Should().Be(0);
	}

	[Fact]
	public void TheGapBetweenTheStoredDayAndTheFirstLiveSpectrumFoldsANoSpectraBreak()
	{
		Cache.LandHistory(0, Chunk(0, 10));

		Cache.Add(StoreSamples.Spectrum(At(60), seq: 1));

		var columns = SecondColumns(0, 61);
		NoSpectraColumnsOf(columns).Should().Equal(60);
		columns.Sum(column => column.Count).Should().Be(11, "the stored points stay beside the live one");
	}

	[Fact]
	public void AGapOfExactlyTenSecondsIsNoBreak()
	{
		Cache.Add(StoreSamples.Spectrum(At(0), seq: 1));
		Cache.Add(StoreSamples.Spectrum(At(10), seq: 2));
		Cache.Add(StoreSamples.Spectrum(At(20.001), seq: 3));

		NoSpectraColumnsOf(SecondColumns(0, 21)).Should().Equal([20], "only the gap of 10.001 s is over the limit");
	}

	[Fact]
	public void AGapAcrossTheFoldStartBreaksAtTheFirstPointInRange()
	{
		Cache.Add(StoreSamples.Spectrum(At(0), seq: 1));
		Cache.Add(StoreSamples.Spectrum(At(20), seq: 2));
		Cache.Add(StoreSamples.Spectrum(At(21), seq: 3));

		var columns = SecondColumns(10, 30);

		NoSpectraColumnsOf(columns).Should().Equal([10], "the point at 0 s before the range is the predecessor");
	}

	[Fact]
	public void AHeartbeatOnlyStretchStillBreaksAtTheNextSpectrum()
	{
		AddHeartbeatStretch();

		Cache.Add(StoreSamples.Spectrum(At(16), seq: 21));

		var column = SecondColumns(16, 17).Single();
		column.BreakCount.Should().Be(1);
		column.NoSpectraCount.Should().Be(1);
		column.LostCount.Should().Be(0);
	}

	[Fact]
	public void UncoveredPointsCountAsSpectraForTheGapRule()
	{
		Cache.Add(StoreSamples.Spectrum(At(0), seq: 0));
		for (var i = 1; i <= 6; i++)
		{
			Cache.Add(StoreSamples.Spectrum(At(5 * i), Spectrometer.Atp2000, seq: (byte)i));
		}

		Cache.Add(StoreSamples.Spectrum(At(35), seq: 7));

		var columns = SecondColumns(0, 36);
		columns.Sum(column => column.UncoveredCount).Should().Be(6);
		columns.Sum(column => column.NoSpectraCount).Should().Be(0, "a spectrum arrived every 5 s");
	}

	[Fact]
	public void ASpectrumAfterAHeartbeatStretchWithASeqGapCarriesBothBreaks()
	{
		AddHeartbeatStretch();

		Cache.Add(StoreSamples.Spectrum(At(16), seq: 22));

		var column = SecondColumns(16, 17).Single();
		column.BreakCount.Should().Be(2);
		column.LostCount.Should().Be(1, "the heartbeats kept the seq session, so the skipped seq 21 counts");
		column.NoSpectraCount.Should().Be(1);
	}

	private static int[] NoSpectraColumnsOf(IntensityColumn[] columns)
	{
		return [.. Enumerable.Range(0, columns.Length).Where(index => columns[index].NoSpectraCount > 0)];
	}

	/// <summary>A spectrum with seq 20 at 0 s, then heartbeats repeating that seq every 3 s up to 15 s.</summary>
	private void AddHeartbeatStretch()
	{
		Cache.Add(StoreSamples.Spectrum(At(0), seq: 20));
		for (var i = 1; i <= 5; i++)
		{
			Cache.Add(StoreSamples.Heartbeat(At(3 * i), seq: 20));
		}
	}
}
