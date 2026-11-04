using System.Reactive.Concurrency;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.Tests.Unit.Core.Store;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Spectrum;
using SpectrumVisualizer.UI.Trend;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SpectrumVisualizer.Tests.Unit.UI.Di;

/// <summary>The window's open starts the trend feed before the distributor; its close stops the feed, then the store.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class AppLifecycleTrendTests : AppLifecycleTestBase
{
	private const string FeedStarted = "The trend feed started";
	private const string FeedStopped = "The trend feed stopped";
	private const string DistributorStarted = "The frame distributor started";
	private const string StoreStopped =
		"The spectrum store stopped: {Stored} frames stored, {Dropped} frames dropped, checkpoint {Checkpoint}";

	// Long enough for a store that does not wait for the feed to stop and log its line.
	private static readonly TimeSpan _storeStopGrace = TimeSpan.FromMilliseconds(500);

	[AvaloniaFact]
	public async Task OpeningTheWindowStartsTheTrendFeedBeforeTheDistributor()
	{
		using var provider = BuildContainer();
		var window = ShowMainWindow(provider);

		await PushOneFrameThenCloseAsync(provider, window, SpectrumFrame(Spectrometer.Atp8000, 7));

		provider.GetRequiredService<IntensityCache>().Extent
			.Should().NotBeNull("the feed took the first frame the distributor handed out");
		var templates = LoggedTemplates();
		var feedStarted = templates.IndexOf(FeedStarted);
		feedStarted.Should().BeGreaterThanOrEqualTo(0);
		templates.IndexOf(DistributorStarted).Should().BeGreaterThan(feedStarted);
	}

	[AvaloniaFact]
	public async Task ClosingTheWindowWaitsForTheTrendFeedsReadBeforeTheStoreStops()
	{
		var readEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var readGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var provider = BuildContainer(services => services.Replace(ServiceDescriptor.Singleton(container =>
			new TrendFeed(
				container.GetRequiredService<FrameDistributor>(),
				container.GetRequiredService<IntensityCache>(),
				container.GetRequiredService<TrendChartViewModel>(),
				container.GetRequiredService<SwitchingSpectrumSource>(),
				async (_, _, from, _, _) =>
				{
					readEntered.TrySetResult();
					await readGate.Task;
					return Result.Ok(new IntensityChunk(from, []));
				},
				(_, _) => Task.FromResult(Result.Ok<StoredSpectrum?>(null)),
				container.GetRequiredService<MessagePanelViewModel>(),
				container.GetRequiredService<IScheduler>(),
				DefaultScheduler.Instance,
				container.GetRequiredService<ILogger<TrendFeed>>()))));
		var window = ShowMainWindow(provider);
		var closed = new TaskCompletionSource();
		window.Closed += (_, _) => closed.TrySetResult();
		ChangeTheTrendKey(provider.GetRequiredService<IntensityCache>());
		await readEntered.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

		window.Close();
		await Waits.UntilAsync(() => LoggedTemplates().Contains(DistributorStopped));
		await Task.Delay(_storeStopGrace, TestContext.Current.CancellationToken);
		var storeStoppedBeforeTheReadEnded = LoggedTemplates().Contains(StoreStopped);
		readGate.SetResult();

		await closed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
		storeStoppedBeforeTheReadEnded.Should().BeFalse("the close awaits the trend feed's stop, which awaits its read");
		var templates = LoggedTemplates();
		var feedStopped = templates.IndexOf(FeedStopped);
		feedStopped.Should().BeGreaterThan(templates.IndexOf(DistributorStopped));
		templates.IndexOf(StoreStopped).Should().BeGreaterThan(feedStopped);
	}

	/// <summary>Two spectra at different wavelengths, one second apart and ending now, so the feed starts a re-read.</summary>
	private static void ChangeTheTrendKey(IntensityCache cache)
	{
		var now = TimeProvider.System.GetUtcNow();
		cache.Add(StoreSamples.Spectrum(now.AddSeconds(-1), seq: 1));
		cache.Add(StoreSamples.Spectrum(now, nm: StoreSamples.SampleNm + 50, seq: 2));
	}
}
