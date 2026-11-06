using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using ScottPlot;

using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.UI.Chart;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Spectrum;

namespace SpectrumVisualizer.UI.Trend;

/// <summary>
/// The trend plot: the cache folded to one column per pixel over the navigation window, the failure strip and the
/// shown moment; every member runs on the UI thread.
/// </summary>
public sealed class TrendChartViewModel : ReactiveObject, IDisposable
{
	private static readonly TimeSpan _changeSample = TimeSpan.FromMilliseconds(100);
	private static readonly TimeSpan _liveTick = TimeSpan.FromSeconds(1);

	private readonly IntensityCache _cache;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly TimeProvider _timeProvider;
	private readonly IScheduler _uiScheduler;
	private readonly ILogger<TrendChartViewModel> _logger;
	private readonly TrendPlotLayers _layers;
	private readonly Subject<Unit> _redrawRequests = new();
	private readonly CompositeDisposable _disposables = [];
	private readonly TrendNavigationModel _navigation;
	private double _dataAreaPixels = TrendColumns.MaximumColumns;
	private TrendFold? _lastFold;
	private bool _disposed;

	public TrendChartViewModel(
		IntensityCache cache,
		SwitchingSpectrumSource source,
		MessagePanelViewModel messagePanel,
		TimeProvider timeProvider,
		IScheduler uiScheduler,
		ILogger<TrendChartViewModel> logger)
	{
		_cache = cache;
		_messagePanel = messagePanel;
		_timeProvider = timeProvider;
		_uiScheduler = uiScheduler;
		_logger = logger;
		var now = timeProvider.GetUtcNow();
		_navigation = new TrendNavigationModel(now - TrendPresets.Default.Width, now, now);
		Width = _navigation.Width;
		_layers = new TrendPlotLayers(Plot);

		_disposables.Add(cache.Changed
			.Sample(_changeSample, uiScheduler)
			// While following, the right edge is now, so a silence shows as an empty span within a second.
			.Merge(Observable.Interval(_liveTick, uiScheduler).Where(_ => IsFollowing).Select(_ => Unit.Default))
			.StartWith(Unit.Default)
			.Subscribe(_ => Refresh(), ReportPipelineFailure));
		_disposables.Add(source
			.WhenAnyValue(switching => switching.ShownMoment)
			.Subscribe(MarkMoment, ReportPipelineFailure));
	}

	public Plot Plot { get; } = new();

	public DateTimeOffset From => _navigation.From;

	public DateTimeOffset To => _navigation.To;

	public TimeSpan Width
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>True while the window's right edge moves with the clock.</summary>
	public bool IsFollowing
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = true;

	public bool HasPoints
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>One signal per redraw request, raised at once on the caller's thread; completes on dispose.</summary>
	public IObservable<Unit> RedrawRequested => _redrawRequests.AsObservable();

	/// <summary>Zooms around the anchor; a window that no longer holds now stops following.</summary>
	public void ZoomAt(double factor, DateTimeOffset anchor)
	{
		if (!HasPoints)
		{
			return;
		}

		var now = _timeProvider.GetUtcNow();
		var liveEdgeShift = SnapToLiveEdge(now);
		_navigation.Zoom(factor, anchor + liveEdgeShift, now);
		Refresh();
	}

	/// <summary>Moves the window; a pan that leaves now outside it stops following.</summary>
	public void PanBy(TimeSpan delta)
	{
		if (!HasPoints)
		{
			return;
		}

		var now = _timeProvider.GetUtcNow();
		SnapToLiveEdge(now);
		_navigation.Pan(delta, now);
		Refresh();
	}

	/// <summary>Moves the window's centre to the moment, clamped at the first point, and stops following.</summary>
	public void CentreOn(DateTimeOffset moment)
	{
		if (!HasPoints)
		{
			return;
		}

		_navigation.Pan(moment - (_navigation.From + (_navigation.Width / 2)), _timeProvider.GetUtcNow());
		_navigation.DetachSticky();
		Refresh();
	}

	/// <summary>Sets the width, keeping the right edge unless the pan floor moves it.</summary>
	public void SetWidth(TimeSpan width)
	{
		_navigation.SetWidth(width);
		Refresh();
	}

	public void FollowLive()
	{
		_navigation.JumpToNow(_timeProvider.GetUtcNow());
		Refresh();
	}

	/// <summary>The width of the plot's data area; the fold takes one column per pixel of it.</summary>
	public void SetDataAreaWidth(double pixels)
	{
		if (_disposed || pixels <= 0 || pixels.Equals(_dataAreaPixels))
		{
			return;
		}

		_dataAreaPixels = pixels;
		Refresh();
	}

	/// <summary>
	/// The hover text of the last fold's column at the moment; null outside the window or before a point.
	/// </summary>
	public string? ReadoutAt(DateTimeOffset moment)
	{
		var index = _lastFold?.Query.ColumnOf(moment) ?? -1;
		return index < 0 ? null : TrendReadout.For(_lastFold!.Series, index, _lastFold.Extent.Key);
	}

	public void ApplyPalette(ChartColors colors)
	{
		_layers.Recolor(colors);
		RequestRedraw();
	}

	/// <summary>Idempotent; a width the view posted before the dispose finds it disposed and does nothing.</summary>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_disposables.Dispose();
		_redrawRequests.OnCompleted();
		_redrawRequests.Dispose();
		Plot.Dispose();
	}

	/// <summary>A failed refresh is reported and skipped, so the next trigger refreshes again.</summary>
	private void Refresh()
	{
		if (_disposed)
		{
			return;
		}

		try
		{
			_lastFold = _cache.FoldCurrent(FollowExtent);
			_layers.Draw(_lastFold, _navigation.From, _navigation.To);

			HasPoints = _lastFold is not null;
			Width = _navigation.Width;
			IsFollowing = _navigation.IsSticky;
			RequestRedraw();
		}
		catch (Exception exception)
		{
			ReportPipelineFailure(exception);
		}
	}

	/// <summary>
	/// Runs under the cache's lock: the first point opens the window at now, a moved first point moves the pan floor,
	/// the live edge follows the clock; then one column per pixel over the window.
	/// </summary>
	private IntensityQuery FollowExtent(TrendExtent extent)
	{
		var now = _timeProvider.GetUtcNow();
		_navigation.FirstSample = extent.First;
		if (!HasPoints)
		{
			_navigation.JumpToNow(now);
		}

		_navigation.OnLiveEdge(now);
		var columnWidth = TrendColumns.ColumnWidth(_navigation.Width, _dataAreaPixels);
		return TrendColumns.QueryThrough(extent.Key, _navigation.From, _navigation.To, columnWidth);
	}

	/// <summary>
	/// While following, moves the window, drawn up to a second ago, onto now and returns how far it moved; an anchor
	/// read off the drawn window moves with it.
	/// </summary>
	private TimeSpan SnapToLiveEdge(DateTimeOffset now)
	{
		var drawnTo = _navigation.To;
		_navigation.OnLiveEdge(now);
		return _navigation.To - drawnTo;
	}

	private void MarkMoment(DateTimeOffset? moment)
	{
		_layers.MarkMoment(moment);
		RequestRedraw();
	}

	private void RequestRedraw()
	{
		_redrawRequests.OnNext(Unit.Default);
	}

	private void ReportPipelineFailure(Exception exception)
	{
		_messagePanel.TryReportFailure(new ExceptionalError(exception.Message, exception), _logger, _uiScheduler);
	}
}
