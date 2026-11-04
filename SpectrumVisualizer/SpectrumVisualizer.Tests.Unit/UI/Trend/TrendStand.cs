using System.Threading.Channels;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Spectrum;
using SpectrumVisualizer.UI.Trend;

namespace SpectrumVisualizer.Tests.Unit.UI.Trend;

/// <summary>
/// An idle trend section for the main window tests: no frame arrives, every store read is empty and the UI scheduler
/// is a test scheduler that never advances.
/// </summary>
public sealed class TrendStand : IDisposable
{
	private readonly IntensityCache _cache;
	private readonly TrendChartViewModel _chart;
	private readonly OverviewViewModel _overview;
	private readonly FrameDistributor _distributor;
	private readonly TrendFeed _feed;

	public TrendStand(SwitchingSpectrumSource source, MessagePanelViewModel messagePanel)
	{
		var uiScheduler = new TestScheduler();
		_cache = new IntensityCache(TimeProvider.System, NullLogger<IntensityCache>.Instance);
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
			(_, _, from, to, _) => Task.FromResult(Result.Ok(new IntensityChunk(from, to, []))),
			(_, _) => Task.FromResult(Result.Ok<StoredSpectrum?>(null)),
			messagePanel,
			uiScheduler,
			uiScheduler,
			NullLogger<TrendFeed>.Instance);
		Trend = new TrendViewModel(_chart, _overview, _feed);
	}

	public TrendViewModel Trend { get; }

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
