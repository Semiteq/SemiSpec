using AwesomeAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

using SpectrumVisualizer.Tests.Unit.Core.Store;
using SpectrumVisualizer.Tests.Unit.UI.Messages;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Messages;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Store;

/// <summary>
/// Failed batches, a failed open and a failure after the open: one report each, every lost frame counted.
/// </summary>
[Trait("Component", "UI")]
[Trait("Area", "Store")]
[Trait("Category", "Unit")]
public sealed class SpectrumStoreFailureTests : SpectrumStoreTestBase
{
	[Fact(Timeout = TestTimeoutMs)]
	public async Task AFailedBatchCountsItsFramesAndReportsOnce()
	{
		await StartOpenedAsync();
		FailWritesOfSeq99();

		await FailOneBatchAsync(expectedDropped: 1);
		await FailOneBatchAsync(expectedDropped: 2);
		await StopAsync();

		var entry = Panel.Entries.Should().ContainSingle().Subject;
		entry.View.Title.Should().Be(Resources.StoreWriteTitle);
		entry.View.Severity.Should().Be(MessageSeverity.Warning);
		entry.RepeatCount.Should().Be(1);
		BatchFailureTimes().Should().HaveCount(
			2, "the report writes the first batch's line and the store the repeat's");
		Logger.Levels().Should().NotContain(LogLevel.Error);
		Logger.Failures().Should().AllBeOfType<SqliteException>();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AWriteAfterAFailureRunReportsAgainOnlyAfterASuccess()
	{
		await StartOpenedAsync();
		FailWritesOfSeq99();

		await FailOneBatchAsync(expectedDropped: 1);
		await FailOneBatchAsync(expectedDropped: 2);
		Feed(StoreSamples.Spectrum(StoreSamples.Start, seq: 1));
		await Waits.UntilAsync(() => Count("spectrum") == 1);
		await FailOneBatchAsync(expectedDropped: 3);
		await StopAsync();

		var entry = Panel.Entries.Should().ContainSingle().Subject;
		entry.View.Title.Should().Be(Resources.StoreWriteTitle);
		entry.RepeatCount.Should().Be(2, "the second report coalesces into the first entry");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AFailedOpenReportsOneErrorAndCountsEveryFrameAsDropped()
	{
		var blocker = Folder.FileIn("blocker");
		await File.WriteAllTextAsync(
			blocker, "a file where the store folder belongs", TestContext.Current.CancellationToken);
		var path = Path.Combine(blocker, "spectra-1.db");
		CreateStore(path: path);
		StartStore();

		await Waits.UntilAsync(() => Panel.Entries.Count == 1);
		await FeedAsync(StoreSamples.Spectrum(StoreSamples.Start), StoreSamples.Heartbeat(StoreSamples.Start));
		await StopAsync();

		var entry = Panel.Entries.Should().ContainSingle().Subject;
		entry.View.Title.Should().Be(Resources.StoreOpenTitle);
		entry.View.Severity.Should().Be(MessageSeverity.Error);
		Store.DroppedFrames.Should().Be(2, "history is off, so every frame is missing from it");
		File.Exists(path).Should().BeFalse();
		Logger.Failures().Should().ContainSingle().Which.Should().BeAssignableTo<IOException>();
		Logger.Levels().Should().NotContain(LogLevel.Information);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AnOpenThatThrowsReportsOneErrorAndCountsEveryFrameAsDropped()
	{
		var path = Folder.FileIn("spectra-\0.db");
		CreateStore(path: path);
		StartStore();

		await Waits.UntilAsync(() => Panel.Entries.Count == 1);
		await FeedAsync(StoreSamples.Spectrum(StoreSamples.Start), StoreSamples.Heartbeat(StoreSamples.Start));
		await StopAsync();

		var entry = Panel.Entries.Should().ContainSingle().Subject;
		entry.View.Title.Should().Be(Resources.StoreOpenTitle);
		entry.View.Severity.Should().Be(MessageSeverity.Error);
		Store.DroppedFrames.Should().Be(2);
		Logger.Failures().Should().ContainSingle().Which.Should().BeOfType<ArgumentException>();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AFailureAfterTheOpenReportsOnceAndCountsEveryLaterFrameAsDropped()
	{
		var clock = new RetentionClock(StoreSamples.Start);
		await StartOpenedAsync(clock);
		await FeedAsync(StoreSamples.Spectrum(StoreSamples.Start, seq: 1));
		await Waits.UntilAsync(() => Count("spectrum") == 1);
		var failure = new InvalidOperationException("the clock failed");
		clock.NowFailure = failure;

		clock.Advance(TimeSpan.FromSeconds(61));

		await Waits.UntilAsync(() => Panel.Entries.Count == 1);
		await FeedAsync(StoreSamples.Spectrum(StoreSamples.Start, seq: 2), StoreSamples.Heartbeat(StoreSamples.Start));
		await StopAsync();

		var entry = Panel.Entries.Should().ContainSingle().Subject;
		entry.View.Title.Should().Be(Resources.StoreStoppedTitle);
		entry.View.Severity.Should().Be(MessageSeverity.Error);
		Count("spectrum").Should().Be(1);
		Store.DroppedFrames.Should().Be(2);
		Logger.Failures().Should().ContainSingle().Which.Should().BeSameAs(failure);
		LatestStoppedLine().Should().EndWith("checkpoint Skipped");
		var delete = () => File.Delete(StorePath);
		delete.Should().NotThrow("the writer closed the file after the failure");
	}
}
