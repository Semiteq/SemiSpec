using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

using AwesomeAssertions;

using SemiSpec.Core.Protocol;
using SemiSpec.Tests.Unit.Core.Store;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Store;

/// <summary>Frames from the distributor reach the file, and a full queue and retention behave.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Store")]
[Trait("Category", "Unit")]
public sealed class SpectrumStoreWriteTests : SpectrumStoreTestBase
{
	[Fact(Timeout = TestTimeoutMs)]
	public async Task EveryFrameFromTheDistributorIsStored()
	{
		await StartOpenedAsync();

		await FeedAsync(
			StoreSamples.Spectrum(StoreSamples.Start, seq: 1),
			StoreSamples.Heartbeat(StoreSamples.Start.AddSeconds(1), seq: 2),
			StoreSamples.Spectrum(StoreSamples.Start.AddSeconds(2), Spectrometer.Atp2000, seq: 3),
			StoreSamples.Heartbeat(StoreSamples.Start.AddSeconds(3), seq: 4));
		await StopAsync();

		Scalar("SELECT group_concat(seq) FROM (SELECT seq FROM spectrum ORDER BY id)").Should().Be("1,3");
		Scalar("SELECT group_concat(seq) FROM (SELECT seq FROM heartbeat ORDER BY id)").Should().Be("2,4");
		Store.DroppedFrames.Should().Be(0);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task TheSubscriberOnlyEnqueues()
	{
		const int Fed = QueueCapacity + BatchSize + 100;
		LinkFrames = Channel.CreateUnbounded<ReceivedFrame>();
		CreateStore();
		var beforeStore = new ConcurrentQueue<long>();
		var afterStore = new ConcurrentQueue<long>();
		using var before = Distributor.Frames.Subscribe(_ => beforeStore.Enqueue(Stopwatch.GetTimestamp()));
		Store.Start();
		using var after = Distributor.Frames.Subscribe(_ => afterStore.Enqueue(Stopwatch.GetTimestamp()));
		Distributor.Start();
		await OpenedAsync();
		using var holder = HoldWriteLock();

		Feed([.. Enumerable.Repeat(StoreSamples.Spectrum(StoreSamples.Start), Fed)]);

		await Waits.UntilAsync(() => afterStore.Count == Fed);
		var slowest = beforeStore.Zip(afterStore, Stopwatch.GetElapsedTime).Max();
		slowest.Should().BeLessThan(TimeSpan.FromMilliseconds(50));
		Store.DroppedFrames.Should().BePositive("the last frames met a full queue");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AFullQueueCountsEachDroppedFrame()
	{
		const int Fed = 1500;
		LinkFrames = Channel.CreateUnbounded<ReceivedFrame>();
		await StartOpenedAsync();
		var signals = 0;
		using var signalled = Store.DroppedFramesChanged.Subscribe(_ => Interlocked.Increment(ref signals));
		var frame = StoreSamples.Spectrum(StoreSamples.Start);

		using (HoldWriteLock())
		{
			await FeedAsync([.. Enumerable.Repeat(frame, Fed)]);
		}

		await StopAsync();

		(Count("spectrum") + Store.DroppedFrames).Should().Be(Fed);
		Store.DroppedFrames.Should().BePositive();
		Volatile.Read(ref signals).Should().BePositive();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task RetentionRunsOnceAMinuteAlsoWithoutFrames()
	{
		var now = StoreSamples.Start.AddHours(25);
		var firstPass = now.AddSeconds(61);
		var clock = new RetentionClock(now);
		await StartOpenedAsync(clock);
		Feed(
			StoreSamples.Spectrum(StoreSamples.Start, seq: 1),
			StoreSamples.Heartbeat(StoreSamples.Start, seq: 2),
			StoreSamples.Spectrum(firstPass - TimeSpan.FromHours(23) - TimeSpan.FromMinutes(59), seq: 5),
			StoreSamples.Spectrum(now, seq: 3));
		await Waits.UntilAsync(() => Count("spectrum") == 3 && Count("heartbeat") == 1);

		clock.Advance(TimeSpan.FromSeconds(59));
		Feed(StoreSamples.Spectrum(now, seq: 6));

		// A due pass would run before this batch, so once the batch lands the heartbeat count proves none ran.
		await Waits.UntilAsync(() => Count("spectrum") == 4);
		Count("heartbeat").Should().Be(1, "no minute has passed");

		clock.Advance(firstPass - now - TimeSpan.FromSeconds(59));

		await Waits.UntilAsync(() => Count("spectrum") == 3 && Count("heartbeat") == 0);
		Scalar("SELECT group_concat(seq) FROM (SELECT seq FROM spectrum ORDER BY id)").Should().Be("5,3,6",
			"the row 23 h 59 min old at the pass survives");
		Feed(StoreSamples.Spectrum(StoreSamples.Start, seq: 4));
		await Waits.UntilAsync(() => Count("spectrum") == 4);

		clock.Advance(TimeSpan.FromSeconds(61));

		await Waits.UntilAsync(() => Count("spectrum") == 2);
		Scalar("SELECT group_concat(seq) FROM (SELECT seq FROM spectrum ORDER BY id)").Should().Be("3,6");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task FramesFromTwoSequenceRunsShareOneFile()
	{
		await StartOpenedAsync();
		ReceivedFrame[] firstSession = [.. Enumerable.Range(0, 3)
			.Select(i => StoreSamples.Spectrum(StoreSamples.Start.AddSeconds(i), seq: (byte)i))];
		ReceivedFrame[] secondSession = [.. Enumerable.Range(0, 3)
			.Select(i => StoreSamples.Spectrum(StoreSamples.Start.AddMinutes(1).AddSeconds(i), seq: (byte)i))];

		Feed(firstSession);
		await Waits.UntilAsync(() => Count("spectrum") == 3);
		await FeedAsync([StoreSamples.Heartbeat(StoreSamples.Start.AddSeconds(30)), .. secondSession]);
		await StopAsync();

		Count("spectrum").Should().Be(6);
		Count("heartbeat").Should().Be(1);
		Scalar("SELECT group_concat(seq) FROM (SELECT seq FROM spectrum ORDER BY id)").Should().Be("0,1,2,0,1,2");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task TheRetentionPathSweepsTheFilesOfOtherInstancesOnceAnHour()
	{
		var clock = new RetentionClock(StoreSamples.Start);
		await StartOpenedAsync(clock);
		var leftover = Folder.FileIn("spectra-2.db");
		await File.WriteAllBytesAsync(leftover, [], TestContext.Current.CancellationToken);

		await RetentionPassAsync(clock, TimeSpan.FromSeconds(61), expectedSpectra: 1);
		await RetentionPassAsync(clock, TimeSpan.FromMinutes(59), expectedSpectra: 2);
		var keptBeforeTheHour = File.Exists(leftover);
		await RetentionPassAsync(clock, TimeSpan.FromMinutes(1), expectedSpectra: 3);

		keptBeforeTheHour.Should().BeTrue("59 min passed since the first pass");
		File.Exists(leftover).Should().BeFalse("an hour passed since the first pass");
		File.Exists(StorePath).Should().BeTrue("the sweep keeps the own file");
		LogTimes("The spectrum store sweep deleted").Should().HaveCount(2, "the open sweeps once, then the hour");
	}
}
