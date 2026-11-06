using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Tests.Unit.Core.Store;
using SpectrumVisualizer.Tests.Unit.UI.Trend;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Store.StoreSamples;

namespace SpectrumVisualizer.Tests.Unit.Core.Trend;

[Trait("Component", "Core")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class IntensityCacheHistoryTests : IntensityCacheTestBase
{
	[Fact(Timeout = 120_000)]
	public async Task FoldingWhileFramesAndChunksLandDoesNotThrow()
	{
		AddSpectra(0, 200);
		Cache.Add(StoreSamples.Spectrum(At(200), nm: OtherNm, seq: 200));
		var query = Query(SampleNm, SampleDelta, Start, At(3600), Second);
		var second = 201;

		await RenderWhileMutating.RunAsync(
			() => Cache.Fold(query),
			() =>
			{
				Cache.Add(StoreSamples.Spectrum(At(second), nm: OtherNm, seq: (byte)second));
				Cache.LandHistory(1, Chunk(second % 80, (second % 80) + 1));
				second++;
			},
			"a fold must never see the point lists mid-change");
	}

	[Fact]
	public void AHistoryChunkOfAnOldKeyGenerationIsIgnored()
	{
		AddSpectra(0, 200);
		Cache.Add(StoreSamples.Spectrum(At(200), nm: OtherNm, seq: 200));
		Cache.Add(StoreSamples.Spectrum(At(201), nm: ThirdNm, seq: 201));

		Cache.LandHistory(1, Chunk(0, 80, value: 1));
		SecondColumns(0, 80).Sum(column => column.Count).Should().Be(0);

		Cache.LandHistory(2, Chunk(0, 80, value: 1));
		SecondColumns(0, 80).Sum(column => column.Count).Should().Be(80);
	}

	[Fact]
	public void HistoryChunksLandInTimeOrderWhateverTheirArrivalOrder()
	{
		AddSpectra(0, 200);
		Cache.Add(StoreSamples.Spectrum(At(200), nm: OtherNm, seq: 200));

		Cache.LandHistory(1, Chunk(40, 80));
		Cache.LandHistory(1, Chunk(0, 40));

		var columns = Cache.Fold(Query(SampleNm, SampleDelta, Start, At(80), TimeSpan.FromSeconds(20))).Columns;
		columns.Select(column => column.Count).Should().Equal(20, 20, 20, 20);
		columns.Select(column => column.Last).Should().Equal(19, 39, 59, 79);
		Cache.Extent!.Value.First.Should().Be(Start);
		Cache.Extent!.Value.Newest.Should().Be(At(200));
	}

	[Fact]
	public async Task TheFoldEqualsTheStoreSeriesOverTheSameFrames()
	{
		var frames = MixedFrames(60);
		Store([.. frames]);
		frames.ForEach(Cache.Add);

		foreach (var width in new[] { Second, TimeSpan.FromSeconds(7), TimeSpan.FromMilliseconds(333) })
		{
			var query = Query(SampleNm, SampleDelta, At(0.25), At(59.5), width);
			var stored = await History.IntensitySeriesAsync(query, Token);

			var folded = Cache.Fold(query).Columns
				.Select(column => column with
				{
					BreakCount = 0,
					LostCount = 0,
					NoSpectraCount = 0,
					Failed = BoardState.None,
				});

			folded.Should().Equal(stored.Value.Columns);
		}
	}

	[Fact]
	public async Task AReReadAtANewKeyKeepsTheLiveBreaks()
	{
		var frames = new List<ReceivedFrame>();
		for (var i = 0; i < 300; i++)
		{
			if (i is >= 100 and < 116)
			{
				continue;
			}

			frames.Add(StoreSamples.Spectrum(At(i), seq: (byte)(i < 50 ? i : i + 2)));
		}

		Store([.. frames]);
		frames.ForEach(Cache.Add);
		var liveBreaks = BreaksOf(SecondColumns(0, 180));

		Cache.Add(StoreSamples.Spectrum(At(300), nm: OtherNm, seq: 46));
		var request = Requests.Single();
		var chunk = await History.IntensityPointsAsync(
			OtherNm, SampleDelta, At(300) - TimeSpan.FromDays(1), request.RecentStart, Token);
		Cache.LandHistory(request.Generation, chunk.Value);

		var reRead = SecondColumns(0, 180);
		BreaksOf(reRead).Should().Equal(liveBreaks);
		liveBreaks.Should().HaveCount(2);
		reRead.Where(column => column.Count > 0).Should().HaveCount(180 - 16).And.AllSatisfy(column =>
			column.Min.Should().Be(RampMean(Spectrometer.Atp8000, OtherNm, SampleDelta)));
	}
}
