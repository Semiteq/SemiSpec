using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.Reactive.Testing;

using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.Tests.Unit.Core.Store;
using SpectrumVisualizer.UI.Trend;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Store.StoreSamples;

namespace SpectrumVisualizer.Tests.Unit.UI.Trend;

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
	public void EveryKeyChangeLogsAtDebugWithItsRunningCount()
	{
		Feed.Start();
		AddAt(0, SampleNm);
		AddAt(1, OtherNm);
		Add(StoreSamples.Spectrum(At(2), nm: OtherNm, delta: 10, seq: NextSeq()));

		var records = FeedLogger.Collector.GetSnapshot()
			.Where(record => record.Message.StartsWith("The trend key changed", StringComparison.Ordinal))
			.ToList();
		records.Should().AllSatisfy(record => record.Level.Should().Be(LogLevel.Debug));
		records.Select(record => record.Message).Should().Equal(
			$"The trend key changed to {OtherNm} nm, delta {SampleDelta}, key change 1",
			$"The trend key changed to {OtherNm} nm, delta 10, key change 2");
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

	[AvaloniaFact]
	public async Task AReturnToThePreviousKeyCancelsThePendingReadAndStartsNone()
	{
		Feed.Start();
		ChangeKeyAfterTwentyMinutes(OtherNm);
		HoldPoints();
		PassThrottle();
		await Waits.UntilAsync(() => PointsCalls.Count == 1);
		var held = PointsCalls.Single();

		AddAt(1201, SampleNm);
		PassThrottle();

		Requests[^1].ReadsHistory.Should().BeFalse();
		held.Token.IsCancellationRequested.Should().BeTrue();
		ReleasePoints();
		await Feed.StopAsync().WaitAsync(Waits.Limit);
		PointsCalls.Should().ContainSingle();
	}

	[AvaloniaFact]
	public async Task AReadThatLandsEveryRangeLetsAReturnRestoreTheKeyWithoutARead()
	{
		Feed.Start();
		ChangeKeyAfterTwentyMinutes(OtherNm);
		var plan = PlanFor(Chart.From, Chart.To);
		PassThrottle();
		await Waits.UntilAsync(() => PointsCalls.Count == plan.Count);
		await Feed.StopAsync().WaitAsync(Waits.Limit);

		AddAt(1201, ThirdNm);
		AddAt(1202, OtherNm);

		Requests.Select(request => request.ReadsHistory).Should().Equal(true, true, false);
	}
}
