using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using ReactiveUI.Avalonia;

using SemiSpec.Core.Store;
using SemiSpec.UI.Localization;
using SemiSpec.UI.Trend;

using Xunit;

using static SemiSpec.Tests.Unit.Core.Store.StoreSamples;

namespace SemiSpec.Tests.Unit.UI.Trend;

[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendFeedFailureTests : TrendFeedTestBase
{
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
		Cache.Fold(query).Columns.Sum(column => column.Count)
			.Should().Be(121, "the recent spectra stay at the new key");
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
}
