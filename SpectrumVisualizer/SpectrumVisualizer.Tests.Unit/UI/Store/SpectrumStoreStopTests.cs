using System.Diagnostics;
using System.Threading.Channels;

using AwesomeAssertions;

using Microsoft.Extensions.Logging;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Tests.Unit.Core.Store;
using SpectrumVisualizer.Tests.Unit.UI.Messages;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Store;

/// <summary>Stop and dispose: the drain, the close budget and the checkpoint.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Store")]
[Trait("Category", "Unit")]
public sealed class SpectrumStoreStopTests : SpectrumStoreTestBase
{
	[Fact(Timeout = TestTimeoutMs)]
	public async Task AStopPastItsBudgetCountsTheUndrainedFramesAsDropped()
	{
		const int Fed = 600;
		LinkFrames = Channel.CreateUnbounded<ReceivedFrame>();
		await StartOpenedAsync();
		var frame = StoreSamples.Spectrum(StoreSamples.Start);
		Task stop;

		using (HoldWriteLock())
		{
			await FeedAsync([.. Enumerable.Repeat(frame, Fed)]);
			await Distributor.StopAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);
			stop = Store.StopAsync(new CancellationToken(canceled: true));
		}

		await stop.WaitAsync(StopLimit, TestContext.Current.CancellationToken);

		(Count("spectrum") + Store.DroppedFrames).Should().Be(Fed);
		Store.DroppedFrames.Should().BeGreaterThanOrEqualTo(Fed - BatchSize, "a batch holds at most 256 frames");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AStopAgainstALockHeldThroughItEndsInsideTheCloseLimit()
	{
		const int Fed = 600;
		LinkFrames = Channel.CreateUnbounded<ReceivedFrame>();
		await StartOpenedAsync();
		TimeSpan elapsed;

		using (HoldWriteLock())
		{
			await FeedAsync([.. Enumerable.Repeat(StoreSamples.Spectrum(StoreSamples.Start), Fed)]);
			using var drain = new CancellationTokenSource(CloseLimit - CloseReserve);
			var stopwatch = Stopwatch.StartNew();
			await Store.StopAsync(drain.Token).WaitAsync(StopLimit, TestContext.Current.CancellationToken);
			elapsed = stopwatch.Elapsed;
		}

		elapsed.Should().BeLessThan(CloseLimit);
		(Count("spectrum") + Store.DroppedFrames).Should().Be(Fed);
		LatestStoppedLine().Should().EndWith("checkpoint Busy");
		var failedBatches = BatchFailureTimes();
		failedBatches.Should().HaveCountGreaterThanOrEqualTo(2);
		(failedBatches[^1] - failedBatches[^2]).Should().BeLessThan(
			TimeSpan.FromSeconds(1.6), "a batch after the stop gives up after the 1 s busy timeout, not 2 s");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AStopDuringARetentionPassEndsInsideTheCloseLimitAndStartsNoOther()
	{
		const int Fed = 600;
		LinkFrames = Channel.CreateUnbounded<ReceivedFrame>();
		var clock = new RetentionClock(StoreSamples.Start);
		await StartOpenedAsync(clock);
		TimeSpan elapsed;

		using (HoldWriteLock())
		{
			// The due timer resumes the writer inline on the advancing thread, so the pass runs off the test thread.
			var retentionPass = Task.Run(() => clock.Advance(TimeSpan.FromSeconds(61)), TestContext.Current.CancellationToken);
			await Waits.UntilAsync(() => clock.Reads == 1);
			await FeedAsync([.. Enumerable.Repeat(StoreSamples.Spectrum(StoreSamples.Start), Fed)]);
			await Distributor.StopAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);
			using var drain = new CancellationTokenSource(CloseLimit - CloseReserve);
			var stopwatch = Stopwatch.StartNew();
			var stop = Store.StopAsync(drain.Token);
			await Waits.UntilAsync(() => LogTimes("The spectrum store rows").Count == 1);
			var nextRetention = Task.Run(() => clock.Advance(TimeSpan.FromSeconds(61)), TestContext.Current.CancellationToken);
			await stop.WaitAsync(StopLimit, TestContext.Current.CancellationToken);
			elapsed = stopwatch.Elapsed;
			await Task.WhenAll(retentionPass, nextRetention).WaitAsync(StopLimit, TestContext.Current.CancellationToken);
		}

		elapsed.Should().BeLessThan(CloseLimit);
		(Count("spectrum") + Store.DroppedFrames).Should().Be(Fed);
		var retention = LogTimes("The spectrum store rows").Should().ContainSingle(
			"no retention pass starts after the stop").Subject;
		var firstFailedBatch = BatchFailureTimes().Should().NotBeEmpty().And.Subject.First();
		(firstFailedBatch - retention).Should().BeLessThan(
			TimeSpan.FromSeconds(1.6), "the batch after the stop gives up after the 1 s busy timeout, not 2 s");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task DisposeCountsTheQueuedFramesAsDropped()
	{
		const int Fed = 600;
		LinkFrames = Channel.CreateUnbounded<ReceivedFrame>();
		await StartOpenedAsync();

		using (HoldWriteLock())
		{
			await FeedAsync([.. Enumerable.Repeat(StoreSamples.Spectrum(StoreSamples.Start), Fed)]);
			Store.Dispose();
			await Waits.UntilAsync(StoppedLogged);
		}

		Count("spectrum").Should().Be(0);
		Store.DroppedFrames.Should().Be(Fed);
		LatestStoppedLine().Should().EndWith("checkpoint Skipped");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task DisposeOnlyCancelsAndTheWriterClosesTheConnection()
	{
		await StartOpenedAsync();
		Feed(StoreSamples.Spectrum(StoreSamples.Start));
		await Waits.UntilAsync(() => Count("spectrum") == 1);

		Store.Dispose();

		await Waits.UntilAsync(StoppedLogged);
		var delete = () => File.Delete(StorePath);
		delete.Should().NotThrow();
		Logger.Levels().Should().NotContain(level => level >= LogLevel.Warning);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task StopDrainsTheQueueThenCheckpoints()
	{
		const int Fed = 600;
		LinkFrames = Channel.CreateUnbounded<ReceivedFrame>();
		await StartOpenedAsync();

		// Open across the stop, so the WAL outlives the writer's close and shows what the checkpoint did to it.
		using var idle = OpenPlain();
		Scalar(idle, "SELECT count(*) FROM spectrum");
		await FeedAsync([.. Enumerable.Repeat(StoreSamples.Spectrum(StoreSamples.Start), Fed)]);
		await StopAsync();

		Count("spectrum").Should().Be(Fed);
		Store.DroppedFrames.Should().Be(0);
		var wal = new FileInfo(StorePath + "-wal");
		wal.Exists.Should().BeTrue("the idle connection keeps the WAL file");
		wal.Length.Should().Be(0, "the checkpoint truncates the WAL");
		LatestStoppedLine().Should().Be(
			$"The spectrum store stopped: {Fed} frames stored, 0 frames dropped, checkpoint Truncated");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AStopWithAFullQueueDrainsInsideTheCloseLimit()
	{
		const int Fed = QueueCapacity + BatchSize;
		LinkFrames = Channel.CreateUnbounded<ReceivedFrame>();
		await StartOpenedAsync();

		using (HoldWriteLock())
		{
			await FeedAsync([.. Enumerable.Repeat(StoreSamples.Spectrum(StoreSamples.Start), Fed)]);
		}

		using var drain = new CancellationTokenSource(CloseLimit - CloseReserve);
		var stopwatch = Stopwatch.StartNew();
		await Store.StopAsync(drain.Token).WaitAsync(StopLimit, TestContext.Current.CancellationToken);

		stopwatch.Elapsed.Should().BeLessThan(CloseLimit);
		(Count("spectrum") + Store.DroppedFrames).Should().Be(Fed);
		Store.DroppedFrames.Should().BeLessThanOrEqualTo(BatchSize, "only frames past the full queue are dropped");
		Count("spectrum").Should().BeGreaterThanOrEqualTo(QueueCapacity, "the full queue drains before the drain token");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task StopBeforeStartCompletesAtOnce()
	{
		CreateStore();

		var stop = Store.StopAsync(TestContext.Current.CancellationToken);

		stop.IsCompletedSuccessfully.Should().BeTrue();
		Store.Start();
		Distributor.Start();
		Feed(StoreSamples.Spectrum(StoreSamples.Start));
		await Task.Delay(NoWriteWait, TestContext.Current.CancellationToken);
		File.Exists(StorePath).Should().BeFalse("Start after a stop opens no file");
		Logger.Collector.Count.Should().Be(0);
	}
}
