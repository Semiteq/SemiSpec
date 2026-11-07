using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.Reactive.Testing;

using SemiSpec.Core.Trend;
using SemiSpec.Tests.Unit.Core.Store;
using SemiSpec.UI.Trend;

using Xunit;

using static SemiSpec.Tests.Unit.Core.Store.StoreSamples;

namespace SemiSpec.Tests.Unit.UI.Trend;

[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendFeedRereadTests : TrendFeedTestBase
{
	[AvaloniaFact]
	public async Task TheFeedFillsTheCacheFromTheDistributor()
	{
		Feed.Start();
		Distributor.Start();

		WriteToChannel(StoreSamples.Spectrum(At(0), seq: 1));
		WriteToChannel(Heartbeat(At(1), seq: 1));
		WriteToChannel(StoreSamples.Spectrum(At(2), seq: 2));

		await Waits.UntilAsync(() => Cache.Extent?.Newest == At(2));
		Cache.Extent!.Value.First.Should().Be(At(0));
		Cache.Extent!.Value.Key.Should().Be(new TrendKey(SampleNm, SampleDelta));
	}

	[AvaloniaFact]
	public void EveryKeyChangeLogsAtDebugWithItsGeneration()
	{
		Feed.Start();
		AddAt(0, SampleNm);
		AddAt(1, OtherNm);
		Add(StoreSamples.Spectrum(At(2), nm: OtherNm, delta: 10, seq: NextSeq()));

		var records = FeedLogger.Collector.GetSnapshot()
			.Where(record => record.Message.StartsWith("The trend re-reads", StringComparison.Ordinal))
			.ToList();
		records.Should().AllSatisfy(record => record.Level.Should().Be(LogLevel.Debug));
		records.Select(record => record.Message).Should().Equal(
			$"The trend re-reads the stored history at {OtherNm} nm, delta {SampleDelta}, generation 1",
			$"The trend re-reads the stored history at {OtherNm} nm, delta 10, generation 2");
	}

	[AvaloniaFact]
	public async Task AKeyChangeReadsTheVisibleRangeFirst()
	{
		Feed.Start();
		ChangeKeyAfterTwentyMinutes(OtherNm);
		var (from, to) = (Chart.From, Chart.To);
		var plan = PlanFor(from, to);

		PassThrottle();

		await Waits.UntilAsync(() => PointsCalls.Count == plan.Count);
		var first = PointsCalls.First();
		first.WavelengthNm.Should().Be(OtherNm);
		first.Delta.Should().Be(SampleDelta);
		first.From.Should().Be(from);
		first.To.Should().Be(Requests[^1].RecentStart);
		RangesOf(PointsCalls).Should().Equal(plan);
	}

	[AvaloniaFact]
	public async Task AReadUsesTheWindowFromBeforeItsFirstRangeNotALaterPan()
	{
		var uiHop = new TestScheduler();
		using var feed = new TrendFeed(
			Distributor,
			Cache,
			Chart,
			Switching,
			ReadPointsAsync,
			ReadSpectrumAsync,
			StoreOpened.Task,
			MessagePanel,
			uiHop,
			Throttle,
			FeedLogger);
		feed.Start();
		ChangeKeyAfterTwentyMinutes(OtherNm);
		HoldPoints();
		PassThrottle();
		PointsCalls.Should().BeEmpty("the read waits for the UI dispatch");

		Chart.PanBy(TimeSpan.FromMinutes(-5));
		var plan = PlanFor(Chart.From, Chart.To);
		uiHop.AdvanceBy(1);
		await Waits.UntilAsync(() => PointsCalls.Count == 1);
		Chart.PanBy(TimeSpan.FromMinutes(-5));
		ReleasePoints();

		await Waits.UntilAsync(() => PointsCalls.Count == plan.Count);
		RangesOf(PointsCalls).Should().Equal(plan);
	}

	[AvaloniaFact]
	public async Task ASecondKeyChangeCancelsTheReadInFlight()
	{
		Feed.Start();
		ChangeKeyAfterTwentyMinutes(OtherNm);
		HoldPoints();
		PassThrottle();
		await Waits.UntilAsync(() => PointsCalls.Count == 1);
		var held = PointsCalls.Single();

		AddAt(1201, ThirdNm);
		PassThrottle();

		held.Token.IsCancellationRequested.Should().BeTrue();
		await Waits.UntilAsync(() => PointsCalls.Any(call => call.WavelengthNm == ThirdNm));
	}

	[AvaloniaFact]
	public async Task KeyChangesInsideTheThrottleReadOnce()
	{
		Feed.Start();
		ChangeKeyAfterTwentyMinutes(OtherNm);
		Throttle.AdvanceBy(TimeSpan.FromMilliseconds(200).Ticks);
		AddAt(1200.2, ThirdNm);
		var plan = PlanFor(Chart.From, Chart.To);

		PassThrottle();

		await Waits.UntilAsync(() => PointsCalls.Count == plan.Count);
		await Feed.StopAsync().WaitAsync(Waits.Limit);
		PointsCalls.Should().OnlyContain(call => call.WavelengthNm == ThirdNm);
		RangesOf(PointsCalls).Should().Equal(plan);
	}
}
