using AwesomeAssertions;

using Microsoft.Extensions.Time.Testing;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.Tests.Unit.Core.Store;
using SpectrumVisualizer.Tests.Unit.UI.Trend;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Store.StoreSamples;

namespace SpectrumVisualizer.Tests.Unit.Core.Trend;

[Trait("Component", "Core")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class IntensityCacheTests : SpectrumHistoryTestBase
{
	private const ushort OtherNm = 1400;
	private const ushort ThirdNm = 1500;

	private static readonly TimeSpan _second = TimeSpan.FromSeconds(1);

	private readonly FakeTimeProvider _time = new(Start);
	private readonly IntensityCache _cache;
	private readonly List<ReReadRequest> _requests = [];

	public IntensityCacheTests()
	{
		_cache = new IntensityCache(_time);
		_cache.ReReadRequested.Subscribe(_requests.Add);
	}

	[Fact]
	public void ASpectrumAddsOnePointAtTheFrameMean()
	{
		_cache.Add(StoreSamples.Spectrum(Start));

		var column = SecondColumns(0, 1).Single();

		column.Count.Should().Be(1);
		column.Min.Should().Be(RampMean(Spectrometer.Atp8000, SampleNm, SampleDelta));
		column.Max.Should().Be(column.Min);
	}

	[Fact]
	public void ADarkSpectrumAddsAPointAtZero()
	{
		_cache.Add(Dark(Start));

		var column = SecondColumns(0, 1).Single();

		column.Count.Should().Be(1);
		column.Min.Should().Be(0);
		column.Max.Should().Be(0);
	}

	[Fact]
	public void ASpectrumOfADeviceThatDoesNotCoverTheWavelengthAddsAnUncoveredPoint()
	{
		_cache.Add(StoreSamples.Spectrum(Start, Spectrometer.Atp2000));

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
			_cache.Add(StoreSamples.Spectrum(At(i), device, nm: CoveredByBothNm, seq: (byte)i));
		}

		var series = _cache.Fold(Query(CoveredByBothNm, SampleDelta, At(0), At(20), _second));

		series.Columns.Should().AllSatisfy(column =>
		{
			column.Count.Should().Be(1);
			column.UncoveredCount.Should().Be(0);
			column.BreakCount.Should().Be(0);
		});
		TrendColumns.Map(series, time => time.ToUnixTimeMilliseconds()).Should().HaveCount(20)
			.And.OnlyContain(column => !double.IsNaN(column.Min), "no gap column breaks the line");
		_requests.Should().BeEmpty("the key stays the same across the device switch");
	}

	[Fact]
	public void AHeartbeatAddsNoPoint()
	{
		_cache.Add(Heartbeat(Start));

		var column = SecondColumns(0, 1).Single();

		column.Count.Should().Be(0);
		column.UncoveredCount.Should().Be(0);
		_cache.Extent.Should().BeNull();
	}

	[Fact]
	public void TheFirstSpectrumSetsTheKeyWithoutAReRead()
	{
		_cache.Add(StoreSamples.Spectrum(Start, nm: OtherNm));

		_requests.Should().BeEmpty();
		SecondColumns(0, 1).Single().Min.Should().Be(RampMean(Spectrometer.Atp8000, OtherNm, SampleDelta));
	}

	[Theory]
	[InlineData(OtherNm, SampleDelta)]
	[InlineData(SampleNm, 10)]
	public void AKeyChangeRecomputesTheRecentSpectraAndAsksForAReRead(ushort nm, byte delta)
	{
		AddSpectra(0, 10);

		_cache.Add(StoreSamples.Spectrum(At(10), nm: nm, delta: delta, seq: 10));

		var expected = RampMean(Spectrometer.Atp8000, nm, delta);
		SecondColumns(0, 11).Should().AllSatisfy(column =>
		{
			column.Count.Should().Be(1);
			column.Min.Should().Be(expected);
		});
		_requests.Should().Equal(new ReReadRequest(1, new TrendKey(nm, delta), Start, ReadsHistory: true));
	}

	[Fact]
	public void AKeyChangeDropsThePointsOlderThanTheRecentSpectraAndKeepsEveryBreakAndMark()
	{
		AddSpectra(0, 20);
		AddSpectra(20, 200, seqStep: 3);
		_cache.Add(new ReceivedFrame(FailedHeartbeat(30 + 2), At(30.5)));

		_cache.Add(StoreSamples.Spectrum(At(200), nm: OtherNm, seq: 202));

		_requests.Single().RecentStart.Should().Be(At(80));
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

		_cache.Add(StoreSamples.Spectrum(At(230), seq: 230));

		var expected = RampMean(Spectrometer.Atp8000, SampleNm, SampleDelta);
		SecondColumns(0, 231).Should().AllSatisfy(column =>
		{
			column.Count.Should().Be(1);
			column.Min.Should().Be(expected);
		});
		_requests.Should().Equal(
			new ReReadRequest(1, new TrendKey(OtherNm, SampleDelta), At(80), ReadsHistory: true),
			new ReReadRequest(2, new TrendKey(SampleNm, SampleDelta), At(80), ReadsHistory: false));
	}

	[Fact]
	public void AReturnAfterAChunkLandedAsksForAReRead()
	{
		AddSpectra(0, 200);
		_cache.Add(StoreSamples.Spectrum(At(200), nm: OtherNm, seq: 200));
		_cache.LandHistory(1, Chunk(0, 80, value: 1));

		_cache.Add(StoreSamples.Spectrum(At(201), seq: 201));

		_requests.Should().HaveCount(2);
		_requests[1].Should().Match<ReReadRequest>(request => request.Generation == 2 && request.ReadsHistory);
		SecondColumns(0, 80).Sum(column => column.Count).Should().Be(0);
	}

	[Fact]
	public void AReturnAtTheEndOfTheReturnWindowRestoresTheKeyWithoutARead()
	{
		AddSpectra(0, 200);
		AddSpectra(200, 320, nm: OtherNm);

		_cache.Add(StoreSamples.Spectrum(At(320), seq: 64));

		_requests.Should().HaveCount(2);
		_requests[1].ReadsHistory.Should().BeFalse("the kept points end at 80 s and the window reaches 240 s past that");
	}

	[Fact]
	public void AReturnPastTheReturnWindowAsksForAReReadOfThePointsBeforeTheRecentSpectra()
	{
		AddSpectra(0, 200);
		AddSpectra(200, 321, nm: OtherNm);

		_cache.Add(StoreSamples.Spectrum(At(321), seq: 65));

		_requests.Should().HaveCount(2);
		_requests[1].ReadsHistory.Should().BeTrue();
		_requests[1].RecentStart.Should().Be(At(201), "the recent spectra hold the last 120 s again");
	}

	[Fact]
	public void AReturnToAKeyWhoseHistoryWasReadInFullRestoresItWithoutARead()
	{
		AddSpectra(0, 200);
		_cache.Add(StoreSamples.Spectrum(At(200), nm: OtherNm, seq: 200));
		_cache.LandHistory(1, Chunk(0, 80, value: 1));
		_cache.CompleteHistory(1);
		_cache.Add(StoreSamples.Spectrum(At(201), nm: ThirdNm, seq: 201));

		_cache.Add(StoreSamples.Spectrum(At(202), nm: OtherNm, seq: 202));

		_requests.Select(request => request.ReadsHistory).Should().Equal(true, true, false);
		SecondColumns(0, 80).Sum(column => column.Count).Should().Be(80, "the landed history came back with the key");
	}

	[Fact]
	public void ACompleteMarkOfAnOlderGenerationLeavesTheHistoryIncomplete()
	{
		AddSpectra(0, 200);
		_cache.Add(StoreSamples.Spectrum(At(200), nm: OtherNm, seq: 200));
		_cache.Add(StoreSamples.Spectrum(At(201), nm: ThirdNm, seq: 201));
		_cache.CompleteHistory(1);
		_cache.Add(StoreSamples.Spectrum(At(202), nm: SampleNm, seq: 202));

		_cache.Add(StoreSamples.Spectrum(At(203), nm: ThirdNm, seq: 203));

		_requests.Select(request => request.ReadsHistory).Should().Equal(true, true, true, true);
	}

	[Fact]
	public void AChangeAfterAReturnAsksForAReRead()
	{
		AddSpectra(0, 200);
		_cache.Add(StoreSamples.Spectrum(At(200), nm: OtherNm, seq: 200));
		_cache.Add(StoreSamples.Spectrum(At(201), seq: 201));

		_cache.Add(StoreSamples.Spectrum(At(202), nm: OtherNm, seq: 202));

		_requests.Select(request => request.ReadsHistory).Should().Equal(true, false, true);
		_requests[2].Generation.Should().Be(3);
	}

	[Fact]
	public void PointsBreaksAndMarksOlderThanADayBeforeNowAreDropped()
	{
		_cache.Add(StoreSamples.Spectrum(At(0), seq: 1));
		_cache.Add(new ReceivedFrame(FailedHeartbeat(1), At(0.5)));
		_cache.Add(StoreSamples.Spectrum(At(1), seq: 5));
		var now = At(1) + TimeSpan.FromDays(1) + TimeSpan.FromMilliseconds(1);
		_time.SetUtcNow(now);

		_cache.Add(StoreSamples.Spectrum(now, seq: 6));

		SecondColumns(0, 2).Should().AllSatisfy(column =>
			column.Should().Match<IntensityColumn>(c => c.Count == 0 && c.BreakCount == 0 && c.Failed == BoardState.None));
		_cache.Extent!.Value.First.Should().Be(now);
	}

	[Fact]
	public void AFoldOfADayCountsOnlyThePointsOfItsRange()
	{
		const int Day = 86_400;
		var spectrum = StoreSamples.Spectrum(Start);
		for (var i = 0; i < Day; i++)
		{
			_cache.Add(spectrum with { Frame = spectrum.Frame with { Seq = (byte)i }, ReceivedAt = At(i) });
		}

		var lastTenMinutes = _cache.Fold(Query(SampleNm, SampleDelta, At(Day - 600), At(Day), Width(600, 2048)));
		var wholeDay = _cache.Fold(Query(SampleNm, SampleDelta, Start, At(Day), Width(Day, 2048)));

		lastTenMinutes.Columns.Sum(column => column.Count).Should().Be(600);
		wholeDay.Columns.Sum(column => column.Count).Should().Be(Day);
	}

	[Fact(Timeout = 120_000)]
	public async Task FoldingWhileFramesAndChunksLandDoesNotThrow()
	{
		AddSpectra(0, 200);
		_cache.Add(StoreSamples.Spectrum(At(200), nm: OtherNm, seq: 200));
		var query = Query(SampleNm, SampleDelta, Start, At(3600), _second);
		var second = 201;

		await RenderWhileMutating.RunAsync(
			() => _cache.Fold(query),
			() =>
			{
				_cache.Add(StoreSamples.Spectrum(At(second), nm: OtherNm, seq: (byte)second));
				_cache.LandHistory(1, Chunk(second % 80, (second % 80) + 1));
				second++;
			},
			"a fold must never see the point lists mid-change");
	}

	[Fact]
	public void AHistoryChunkOfAnOldKeyGenerationIsIgnored()
	{
		AddSpectra(0, 200);
		_cache.Add(StoreSamples.Spectrum(At(200), nm: OtherNm, seq: 200));
		_cache.Add(StoreSamples.Spectrum(At(201), nm: ThirdNm, seq: 201));

		_cache.LandHistory(1, Chunk(0, 80, value: 1));
		SecondColumns(0, 80).Sum(column => column.Count).Should().Be(0);

		_cache.LandHistory(2, Chunk(0, 80, value: 1));
		SecondColumns(0, 80).Sum(column => column.Count).Should().Be(80);
	}

	[Fact]
	public void HistoryChunksLandInTimeOrderWhateverTheirArrivalOrder()
	{
		AddSpectra(0, 200);
		_cache.Add(StoreSamples.Spectrum(At(200), nm: OtherNm, seq: 200));

		_cache.LandHistory(1, Chunk(40, 80));
		_cache.LandHistory(1, Chunk(0, 40));

		var columns = _cache.Fold(Query(SampleNm, SampleDelta, Start, At(80), TimeSpan.FromSeconds(20))).Columns;
		columns.Select(column => column.Count).Should().Equal(20, 20, 20, 20);
		columns.Select(column => column.Last).Should().Equal(19, 39, 59, 79);
		_cache.Extent!.Value.First.Should().Be(Start);
		_cache.Extent!.Value.Newest.Should().Be(At(200));
	}

	[Fact]
	public async Task TheFoldEqualsTheStoreSeriesOverTheSameFrames()
	{
		var frames = MixedFrames(60);
		Store([.. frames]);
		frames.ForEach(_cache.Add);

		foreach (var width in new[] { _second, TimeSpan.FromSeconds(7), TimeSpan.FromMilliseconds(333) })
		{
			var query = Query(SampleNm, SampleDelta, At(0.25), At(59.5), width);
			var stored = await History.IntensitySeriesAsync(query, Token);

			var folded = _cache.Fold(query).Columns
				.Select(column => column with { BreakCount = 0, LostCount = 0, NoSpectraCount = 0, Failed = BoardState.None });

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
		frames.ForEach(_cache.Add);
		var liveBreaks = BreaksOf(SecondColumns(0, 180));

		_cache.Add(StoreSamples.Spectrum(At(300), nm: OtherNm, seq: 46));
		var request = _requests.Single();
		var chunk = await History.IntensityPointsAsync(
			OtherNm, SampleDelta, At(300) - TimeSpan.FromDays(1), request.RecentStart, Token);
		_cache.LandHistory(request.Generation, chunk.Value);

		var reRead = SecondColumns(0, 180);
		BreaksOf(reRead).Should().Equal(liveBreaks);
		liveBreaks.Should().HaveCount(2);
		reRead.Where(column => column.Count > 0).Should().HaveCount(180 - 16).And.AllSatisfy(column =>
			column.Min.Should().Be(RampMean(Spectrometer.Atp8000, OtherNm, SampleDelta)));
	}

	[Fact]
	public void AfterTheDisposeFramesAndChunksAreIgnored()
	{
		var cache = new IntensityCache(_time);
		cache.Add(StoreSamples.Spectrum(At(0), seq: 0));
		cache.Add(StoreSamples.Spectrum(At(1), nm: OtherNm, seq: 1));
		cache.Dispose();

		var lateFrame = () => cache.Add(StoreSamples.Spectrum(At(2), nm: OtherNm, seq: 2));
		var lateChunk = () => cache.LandHistory(1, Chunk(0, 1));

		lateFrame.Should().NotThrow();
		lateChunk.Should().NotThrow();
		cache.Extent!.Value.Newest.Should().Be(At(1));
	}

	[Fact]
	public void ASubscriberThatDisposesTheCacheOnAKeyChangeStopsTheChangedSignal()
	{
		var cache = new IntensityCache(_time);
		cache.Add(StoreSamples.Spectrum(At(0), seq: 0));
		var changes = 0;
		cache.Changed.Subscribe(_ => changes++);
		cache.ReReadRequested.Subscribe(_ => cache.Dispose());

		var keyChange = () => cache.Add(StoreSamples.Spectrum(At(1), nm: OtherNm, seq: 1));

		keyChange.Should().NotThrow();
		changes.Should().Be(0);
	}

	[Fact]
	public void ASubscriberThatDisposesTheCacheOnALandedChunkStopsTheChangedSignal()
	{
		var cache = new IntensityCache(_time);
		cache.Add(StoreSamples.Spectrum(At(0), seq: 0));
		cache.Add(StoreSamples.Spectrum(At(1), nm: OtherNm, seq: 1));
		var changes = 0;
		cache.Changed.Subscribe(_ => changes++);
		cache.HistoryLanded.Subscribe(_ => cache.Dispose());

		var landing = () => cache.LandHistory(1, Chunk(0, 1));

		landing.Should().NotThrow();
		changes.Should().Be(0);
	}

	[Fact]
	public void TheMomentLookupReadsTheRecentSpectraAtOrBefore()
	{
		AddSpectra(0, 200);

		var found = _cache.SpectrumAt(At(150.5));

		found.Should().NotBeNull();
		found!.ReceivedAt.Should().Be(At(150));
		found.Seq.Should().Be(150);
		_cache.SpectrumAt(At(78.999)).Should().BeNull("the moment is before the recent spectra");
		_cache.SpectrumAt(At(500))!.ReceivedAt.Should().Be(At(199));
	}

	private static DateTimeOffset At(double seconds)
	{
		return Start + TimeSpan.FromSeconds(seconds);
	}

	private static TimeSpan Width(int seconds, int columns)
	{
		return TimeSpan.FromMilliseconds(Math.Ceiling(seconds * 1000.0 / columns));
	}

	private static BoardFrame FailedHeartbeat(byte seq)
	{
		return new BoardFrame(
			BoardState.Atp8000Connected | BoardState.Atp8000MeasurementFailed, seq, null);
	}

	/// <summary>A chunk of one point per second over [fromSecond, toSecond), valued at its second unless given.</summary>
	private static IntensityChunk Chunk(int fromSecond, int toSecond, double? value = null)
	{
		var points = Enumerable.Range(fromSecond, toSecond - fromSecond)
			.Select(second => new TrendPoint(At(second).ToUnixTimeMilliseconds(), value ?? second))
			.ToArray();
		return new IntensityChunk(At(fromSecond), points);
	}

	/// <summary>
	/// Spectra each second at the sample key and ATP-8000, darks every seventh, ATP-2000 every fifth, a heartbeat after
	/// every third and a gap at every eleventh.
	/// </summary>
	private static List<ReceivedFrame> MixedFrames(int seconds)
	{
		var frames = new List<ReceivedFrame>();
		for (var i = 0; i < seconds; i++)
		{
			if (i % 11 == 10)
			{
				continue;
			}

			frames.Add(i % 7 == 6 ? Dark(At(i)) : StoreSamples.Spectrum(At(i), i % 5 == 4 ? Spectrometer.Atp2000 : Spectrometer.Atp8000));
			if (i % 3 == 2)
			{
				frames.Add(Heartbeat(At(i + 0.5)));
			}
		}

		return frames;
	}

	private static List<(int Column, int Breaks, int Lost)> BreaksOf(IntensityColumn[] columns)
	{
		return [.. columns
			.Select((column, index) => (index, column.BreakCount, column.LostCount))
			.Where(entry => entry.BreakCount > 0)];
	}

	/// <summary>Spectra at each second of [fromSecond, toSecond), the seq stepping by seqStep from the second.</summary>
	private void AddSpectra(int fromSecond, int toSecond, ushort nm = SampleNm, int seqStep = 1)
	{
		var seqOffset = seqStep - 1;
		for (var i = fromSecond; i < toSecond; i++)
		{
			_cache.Add(StoreSamples.Spectrum(At(i), nm: nm, seq: (byte)(i + seqOffset)));
		}
	}

	/// <summary>The cache folded over [fromSecond, toSecond) in one-second columns.</summary>
	private IntensityColumn[] SecondColumns(int fromSecond, int toSecond)
	{
		return _cache.Fold(Query(SampleNm, SampleDelta, At(fromSecond), At(toSecond), _second)).Columns;
	}
}
