using System.Reactive.Concurrency;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Store;
using SemiSpec.Core.Trend;
using SemiSpec.Tests.Unit.Core.Store;
using SemiSpec.Tests.Unit.UI.Link;
using SemiSpec.UI.Link;
using SemiSpec.UI.Messages;
using SemiSpec.UI.Spectrum;
using SemiSpec.UI.Store;
using SemiSpec.UI.Trend;

using Xunit;

using static SemiSpec.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SemiSpec.Tests.Unit.UI.Di;

/// <summary>
/// The window's open starts the trend feed before the distributor; its close stops the feed, then the store; the next
/// open shows the stored points.
/// </summary>
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class AppLifecycleTrendTests : AppLifecycleTestBase
{
	private const ushort ProtocolSampleNm = 1310;
	private const byte ProtocolSampleDelta = 5;
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
				container.GetRequiredService<SpectrumStore>().Opened,
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
		storeStoppedBeforeTheReadEnded.Should()
			.BeFalse("the close awaits the trend feed's stop, which awaits its read");
		var templates = LoggedTemplates();
		var feedStopped = templates.IndexOf(FeedStopped);
		feedStopped.Should().BeGreaterThan(templates.IndexOf(DistributorStopped));
		templates.IndexOf(StoreStopped).Should().BeGreaterThan(feedStopped);
	}

	[AvaloniaFact]
	public async Task TheTrendShowsTheStoredPointsAfterARestart()
	{
		List<DateTimeOffset> fed;
		using (var first = BuildContainer())
		{
			var window = ShowMainWindow(first);
			fed = await FeedSpectraThenCloseAsync(first, window, count: 5);
		}

		using var second = BuildContainer();
		var restarted = ShowMainWindow(second);
		var cache = second.GetRequiredService<IntensityCache>();

		await Waits.UntilAsync(() => fed.All(moment => PointsAt(cache, moment) == 1));
		var span = IntensityQuery.Create(
			ProtocolSampleNm, ProtocolSampleDelta, fed[0], fed[^1].AddMilliseconds(1), TimeSpan.FromMilliseconds(1));
		cache.Fold(span.Value).Columns.Sum(column => column.Count).Should().Be(fed.Count);
		restarted.Close();
	}

	/// <summary>The cache's points in the millisecond of the moment, at the key of the protocol samples.</summary>
	private static int PointsAt(IntensityCache cache, DateTimeOffset moment)
	{
		var query = IntensityQuery.Create(
			ProtocolSampleNm, ProtocolSampleDelta, moment, moment.AddMilliseconds(1), TimeSpan.FromMilliseconds(1));
		return cache.Fold(query.Value).Columns.Sum(column => column.Count);
	}

	/// <summary>Spectra through the link, one at a time; returns their receive times once the window closed.</summary>
	private async Task<List<DateTimeOffset>> FeedSpectraThenCloseAsync(
		IServiceProvider provider, Window window, int count)
	{
		await using var port = new FakePortStream();
		PortProvider.QueueOpen(port);
		var received = new List<DateTimeOffset>();
		using var subscription = provider.GetRequiredService<FrameDistributor>().Frames
			.Subscribe(frame =>
			{
				lock (received)
				{
					received.Add(frame.ReceivedAt);
				}
			});
		var closed = new TaskCompletionSource();
		window.Closed += (_, _) => closed.TrySetResult();
		provider.GetRequiredService<SerialLink>().Connect(PortName);
		await port.WaitForPendingReadAsync().WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

		for (var seq = 1; seq <= count; seq++)
		{
			port.PushBytes(FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, (byte)seq)));
			var expected = seq;
			await Waits.UntilAsync(() =>
			{
				lock (received)
				{
					return received.Count == expected;
				}
			});
		}

		window.Close();
		await closed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
		lock (received)
		{
			return [.. received];
		}
	}

	/// <summary>
	/// Two spectra at different wavelengths, one second apart and ending now, so the feed starts a re-read.
	/// </summary>
	private static void ChangeTheTrendKey(IntensityCache cache)
	{
		var now = TimeProvider.System.GetUtcNow();
		cache.Add(StoreSamples.Spectrum(now.AddSeconds(-1), seq: 1));
		cache.Add(StoreSamples.Spectrum(now, nm: StoreSamples.SampleNm + 50, seq: 2));
	}
}
