using System.Threading.Channels;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Store;
using SemiSpec.Core.Trend;
using SemiSpec.Tests.Unit.Core.Store;
using SemiSpec.UI.Link;
using SemiSpec.UI.Messages;
using SemiSpec.UI.Spectrum;
using SemiSpec.UI.Trend;

namespace SemiSpec.Tests.Unit.UI.Trend;

/// <summary>
/// An idle trend section for the main window tests: no frame arrives unless a test adds one, every store read is empty
/// and the UI scheduler is a test scheduler that advances only through <see cref="Sample"/>.
/// </summary>
public sealed class TrendStand : IDisposable
{
	private readonly TestScheduler _uiScheduler = new();
	private readonly IntensityCache _cache;
	private readonly TrendChartViewModel _chart;
	private readonly OverviewViewModel _overview;
	private readonly FrameDistributor _distributor;
	private readonly TrendFeed _feed;

	public TrendStand(SwitchingSpectrumSource source, MessagePanelViewModel messagePanel)
	{
		var uiScheduler = _uiScheduler;
		_cache = new IntensityCache(TimeProvider.System);
		_chart = new TrendChartViewModel(
			_cache, source, messagePanel, TimeProvider.System, uiScheduler, NullLogger<TrendChartViewModel>.Instance);
		_overview = new OverviewViewModel(
			_cache, _chart, messagePanel, uiScheduler, NullLogger<OverviewViewModel>.Instance);
		_distributor = new FrameDistributor(
			Channel.CreateUnbounded<ReceivedFrame>().Reader,
			messagePanel,
			uiScheduler,
			NullLogger<FrameDistributor>.Instance);
		_feed = new TrendFeed(
			_distributor,
			_cache,
			_chart,
			source,
			(_, _, from, _, _) => Task.FromResult(Result.Ok(new IntensityChunk(from, []))),
			(_, _) => Task.FromResult(Result.Ok<StoredSpectrum?>(null)),
			Task.FromResult(false),
			messagePanel,
			uiScheduler,
			uiScheduler,
			NullLogger<TrendFeed>.Instance);
		Trend = new TrendViewModel(_chart, _overview, _feed);
	}

	public TrendViewModel Trend { get; }

	/// <summary>One ATP-8000 spectrum per second over the span that ends now, each with the next seq.</summary>
	public void AddSpectraEndingNow(TimeSpan span)
	{
		var now = TimeProvider.System.GetUtcNow();
		var seconds = (int)span.TotalSeconds;
		for (var second = seconds; second > 0; second--)
		{
			_cache.Add(StoreSamples.Spectrum(now.AddSeconds(-second), seq: unchecked((byte)(seconds - second + 1))));
		}
	}

	/// <summary>Lets the 100 ms sample of the cache's changes reach the chart and the overview.</summary>
	public void Sample()
	{
		_uiScheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks);
	}

	public void Dispose()
	{
		Trend.Dispose();
		_feed.Dispose();
		_distributor.Dispose();
		_overview.Dispose();
		_chart.Dispose();
		_cache.Dispose();
	}
}
