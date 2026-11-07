using System.Reactive.Concurrency;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Reactive.Testing;

using SemiSpec.Core.Store;
using SemiSpec.UI.Trend;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Trend;

[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendFeedMomentTests : TrendFeedTestBase
{
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
			Distributor,
			Cache,
			Chart,
			Switching,
			ReadPointsAsync,
			ReadSpectrumAsync,
			StoreOpened.Task,
			MessagePanel,
			ui,
			Throttle,
			FeedLogger);
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
