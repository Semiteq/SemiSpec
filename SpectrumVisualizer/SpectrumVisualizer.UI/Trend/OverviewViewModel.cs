using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.UI.Messages;

namespace SpectrumVisualizer.UI.Trend;

/// <summary>
/// The overview strip: the whole cache folded to a band, and where the trend window sits along it; every member runs
/// on the UI thread.
/// </summary>
public sealed class OverviewViewModel : ReactiveObject, IDisposable
{
	public const int BandColumns = 250;

	private const double RefoldDelayDivisor = 1000.0;

	private static readonly TimeSpan _fastestRefold = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan _slowestRefold = TimeSpan.FromSeconds(60);

	// The band ends at the newest point, which the fold's half-open range would leave out.
	private static readonly TimeSpan _newestPointInclusion = TimeSpan.FromMilliseconds(1);

	private readonly IntensityCache _cache;
	private readonly TrendChartViewModel _chart;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly IScheduler _uiScheduler;
	private readonly ILogger<OverviewViewModel> _logger;
	private readonly SerialDisposable _nextRefold = new();
	private readonly CompositeDisposable _disposables = [];

	public OverviewViewModel(
		IntensityCache cache,
		TrendChartViewModel chart,
		MessagePanelViewModel messagePanel,
		IScheduler uiScheduler,
		ILogger<OverviewViewModel> logger)
	{
		_cache = cache;
		_chart = chart;
		_messagePanel = messagePanel;
		_uiScheduler = uiScheduler;
		_logger = logger;

		_disposables.Add(_nextRefold);
		_disposables.Add(cache.HistoryLanded
			.Merge(cache.ReReadRequested.Select(_ => Unit.Default))
			.ObserveOn(uiScheduler)
			.StartWith(Unit.Default)
			.Subscribe(_ => Reporting(Refold), ReportFailure));
		// The chart requests a redraw after every refresh, which is where its window and the cache's extent move.
		_disposables.Add(chart.RedrawRequested.Subscribe(_ => Reporting(PlaceWindow), ReportFailure));
	}

	/// <summary>The cache folded to about 250 columns over its extent; null before the first spectrum.</summary>
	public IntensitySeries? Band
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public bool HasExtent
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>The strip's left bound: the band's first column start, which may precede the oldest point.</summary>
	public DateTimeOffset ExtentFirst
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>The strip's right bound: the newest point.</summary>
	public DateTimeOffset ExtentLast
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public double WindowStartFraction
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public double WindowWidthFraction
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = 1.0;

	/// <summary>Centres the trend window on the time at the fraction of the strip and stops following.</summary>
	public void NavigateToFraction(double fraction)
	{
		if (!HasExtent)
		{
			return;
		}

		_chart.CentreOn(OverviewGeometry.TimeAtFraction(ExtentFirst, ExtentLast, fraction));
	}

	public void Dispose()
	{
		_disposables.Dispose();
	}

	/// <summary>Refolds the band, then schedules the next refold at a thousandth of the extent, 2 s to 60 s.</summary>
	private void Refold()
	{
		try
		{
			Band = _cache.Key is { } key && _cache.FirstPointAt is { } first && _cache.NewestAt is { } newest
				? Fold(key, first, newest)
				: null;
			PlaceWindow();
		}
		finally
		{
			var delay = HasExtent ? (ExtentLast - ExtentFirst) / RefoldDelayDivisor : _fastestRefold;
			var clampedDelay = TimeSpan.FromTicks(Math.Clamp(delay.Ticks, _fastestRefold.Ticks, _slowestRefold.Ticks));
			_nextRefold.Disposable = _uiScheduler.Schedule(clampedDelay, () => Reporting(Refold));
		}
	}

	private IntensitySeries Fold(TrendKey key, DateTimeOffset first, DateTimeOffset newest)
	{
		var milliseconds = Math.Ceiling((newest - first).TotalMilliseconds / BandColumns);
		var columnWidth = TimeSpan.FromMilliseconds(Math.Max(1.0, milliseconds));
		var query = IntensityQuery
			.Create(key.WavelengthNm, key.Delta, first, newest + _newestPointInclusion, columnWidth)
			.Value;

		return OverviewGeometry.Connected(_cache.Fold(query));
	}

	private void PlaceWindow()
	{
		if (_cache.FirstPointAt is not { } first || _cache.NewestAt is not { } newest)
		{
			HasExtent = false;
			return;
		}

		var bandStart = Band is { Columns: [var firstColumn, ..] } ? firstColumn.Start : first;
		ExtentFirst = bandStart < first ? bandStart : first;
		ExtentLast = newest;
		HasExtent = true;
		(WindowStartFraction, WindowWidthFraction) =
			OverviewGeometry.WindowFraction(ExtentFirst, ExtentLast, _chart.From, _chart.To);
	}

	/// <summary>A failed step is reported and skipped, so the next trigger runs it again.</summary>
	private void Reporting(Action step)
	{
		try
		{
			step();
		}
		catch (Exception exception)
		{
			ReportFailure(exception);
		}
	}

	private void ReportFailure(Exception exception)
	{
		_messagePanel.TryReportFailure(new ExceptionalError(exception.Message, exception), _logger, _uiScheduler);
	}
}
