using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Trend;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SpectrumVisualizer.Tests.Unit.UI.Di;

/// <summary>The window's open starts the trend feed before the distributor, and its close stops it before the store.</summary>
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
	public async Task ClosingTheWindowStopsTheTrendFeedBeforeTheStore()
	{
		using var provider = BuildContainer();
		var window = ShowMainWindow(provider);
		var closed = new TaskCompletionSource();
		window.Closed += (_, _) => closed.TrySetResult();

		window.Close();

		await closed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
		var templates = LoggedTemplates();
		var distributorStopped = templates.IndexOf(DistributorStopped);
		var feedStopped = templates.IndexOf(FeedStopped);
		distributorStopped.Should().BeGreaterThanOrEqualTo(0);
		feedStopped.Should().BeGreaterThan(distributorStopped);
		templates.IndexOf(StoreStopped).Should().BeGreaterThan(feedStopped);
	}
}
