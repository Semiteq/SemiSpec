using System.Reactive.Concurrency;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging;
using Microsoft.Reactive.Testing;

using ReactiveUI.Avalonia;

using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.Tests.Unit.Core.Store;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Trend;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Store.StoreSamples;

namespace SpectrumVisualizer.Tests.Unit.UI.Trend;

[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendFeedTests : TrendFeedTestBase
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

	// The pool side of the read starts at once after the UI dispatch, so no test can pan between the two: a pan before
	// the dispatch moves the read's window, and a pan after its first range started changes no range.
	[AvaloniaFact]
	public async Task AReadUsesTheWindowFromBeforeItsFirstRangeNotALaterPan()
	{
		var uiHop = new TestScheduler();
		using var feed = new TrendFeed(
			Distributor, Cache, Chart, Switching, ReadPointsAsync, ReadSpectrumAsync, MessagePanel, uiHop, Throttle, FeedLogger);
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
	public async Task AFailedReadReportsOnceAndKeepsTheLivePart()
	{
		PointsResult = _ => Result.Fail(new Error("the store file is locked"));
		Feed.Start();
		ChangeKeyAfterTwentyMinutes(OtherNm);

		PassThrottle();

		await Waits.UntilAsync(() => MessagePanel.Entries.Count == 1);
		await Feed.StopAsync().WaitAsync(Waits.Limit);
		PointsCalls.Should().ContainSingle("the read ends at its first failed range");
		MessagePanel.Entries.Should().ContainSingle().Which.View.Title.Should().Be(Resources.TrendHistoryTitle);
		var query = IntensityQuery.Create(OtherNm, SampleDelta, At(0), At(1201), TimeSpan.FromSeconds(1)).Value;
		Cache.Fold(query).Columns.Sum(column => column.Count).Should().Be(121, "the recent spectra stay at the new key");
	}

	[AvaloniaFact]
	public async Task AReadThatFailsAfterTheStopReportsNothing()
	{
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var feed = new TrendFeed(
			Distributor,
			Cache,
			Chart,
			Switching,
			async (_, _, _, _, _) =>
			{
				entered.TrySetResult();
				await gate.Task;
				return Result.Fail<IntensityChunk>("the store file is locked");
			},
			(_, _) => Task.FromResult(Result.Ok<StoredSpectrum?>(null)),
			MessagePanel,
			AvaloniaScheduler.Instance,
			Throttle,
			FeedLogger);
		feed.Start();
		ChangeKeyAfterTwentyMinutes(OtherNm);
		PassThrottle();
		await entered.Task.WaitAsync(Waits.Limit);

		var stop = feed.StopAsync();
		gate.SetResult();

		await stop.WaitAsync(Waits.Limit);
		MessagePanel.Entries.Should().BeEmpty("the read ended after the stop, which ignores its failure");
	}

	[AvaloniaFact]
	public async Task ARereadBeforeTheStoreFileExistsReportsOnceAndCreatesNoFile()
	{
		using var folder = new TempFolder();
		var missing = folder.FileIn("spectra-1.db");
		var history = new SpectrumHistory(missing);
		using var feed = new TrendFeed(
			Distributor,
			Cache,
			Chart,
			Switching,
			history.IntensityPointsAsync,
			history.SpectrumAtAsync,
			MessagePanel,
			AvaloniaScheduler.Instance,
			Throttle,
			FeedLogger);
		feed.Start();
		ChangeKeyAfterTwentyMinutes(OtherNm);

		PassThrottle();

		await Waits.UntilAsync(() => MessagePanel.Entries.Count == 1);
		await feed.StopAsync().WaitAsync(Waits.Limit);
		MessagePanel.Entries.Should().ContainSingle().Which.View.Title.Should().Be(Resources.TrendHistoryTitle);
		File.Exists(missing).Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task AClickDuringARereadShowsTheMomentAndLeavesTheRereadRunning()
	{
		Feed.Start();
		ChangeKeyAfterTwentyMinutes(OtherNm);
		var plan = PlanFor(Chart.From, Chart.To);
		SpectrumResult = call => Result.Ok<StoredSpectrum?>(Stored(call.Moment));
		HoldPoints();
		PassThrottle();
		await Waits.UntilAsync(() => PointsCalls.Count == 1);
		var held = PointsCalls.Single();

		Feed.ReadMoment(At(10));

		await Waits.UntilAsync(() => Switching.ShownMoment == At(10));
		held.Token.IsCancellationRequested.Should().BeFalse("the click runs in its own pipeline");
		ReleasePoints();
		await Waits.UntilAsync(() => PointsCalls.Count == plan.Count);
		RangesOf(PointsCalls).Should().Equal(plan);
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

	[AvaloniaFact]
	public async Task BackToLiveCancelsTheMomentReadInFlight()
	{
		Feed.Start();
		AddSpectra(0, 300);
		Feed.ReadMoment(At(250));
		Switching.ShownMoment.Should().Be(At(250), "the moment lies inside the recent spectra");
		SpectrumResult = call => Result.Ok<StoredSpectrum?>(Stored(call.Moment));
		HoldSpectrum();
		Feed.ReadMoment(At(10));
		await Waits.UntilAsync(() => SpectrumCalls.Count == 1);

		Switching.ReturnToLive();

		SpectrumCalls.Single().Token.IsCancellationRequested.Should().BeTrue();
		ReleaseSpectrum();
		await Feed.StopAsync().WaitAsync(Waits.Limit);
		Switching.ShownMoment.Should().BeNull("the cancelled read shows nothing");
	}

	[AvaloniaFact]
	public async Task BackToLiveDropsAMomentReadAlreadyQueuedForTheUiThread()
	{
		var ui = new CountingScheduler();
		using var feed = new TrendFeed(
			Distributor, Cache, Chart, Switching, ReadPointsAsync, ReadSpectrumAsync, MessagePanel, ui, Throttle, FeedLogger);
		feed.Start();
		AddSpectra(0, 300);
		feed.ReadMoment(At(250));
		ui.Start();
		Switching.ShownMoment.Should().Be(At(250), "the moment lies inside the recent spectra");
		SpectrumResult = call => Result.Ok<StoredSpectrum?>(Stored(call.Moment));
		HoldSpectrum();
		feed.ReadMoment(At(10));
		await Waits.UntilAsync(() => SpectrumCalls.Count == 1);
		var queuedBefore = ui.Scheduled;
		ReleaseSpectrum();
		await Waits.UntilAsync(() => ui.Scheduled > queuedBefore);

		Switching.ReturnToLive();
		ui.Start();

		Switching.ShownMoment.Should().BeNull("the read emitted before Back to live, and the chart stays live");
	}

	[AvaloniaFact]
	public async Task StopCancelsTheReadsAndAwaitsThem()
	{
		Feed.Start();
		ChangeKeyAfterTwentyMinutes(OtherNm);
		HoldPoints();
		HoldSpectrum();
		PassThrottle();
		Feed.ReadMoment(At(10));
		await Waits.UntilAsync(() => PointsCalls.Count == 1 && SpectrumCalls.Count == 1);

		var stop = Feed.StopAsync();

		PointsCalls.Single().Token.IsCancellationRequested.Should().BeTrue();
		SpectrumCalls.Single().Token.IsCancellationRequested.Should().BeTrue();
		stop.IsCompleted.Should().BeFalse("both reads are still held");
		ReleasePoints();
		ReleaseSpectrum();
		await stop.WaitAsync(Waits.Limit);
	}

	/// <summary>Counts the work queued on it, so a test sees a UI hop waiting without running it.</summary>
	private sealed class CountingScheduler : TestScheduler
	{
		// Written by the pool thread of a read, read by the test thread.
		private int _scheduled;

		public int Scheduled => Volatile.Read(ref _scheduled);

		public override IDisposable ScheduleAbsolute<TState>(
			TState state, long dueTime, Func<IScheduler, TState, IDisposable> action)
		{
			var scheduled = base.ScheduleAbsolute(state, dueTime, action);
			Interlocked.Increment(ref _scheduled);
			return scheduled;
		}
	}
}
