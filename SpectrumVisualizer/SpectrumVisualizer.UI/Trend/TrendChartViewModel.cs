using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.TickGenerators;

using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.UI.Chart;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Spectrum;

namespace SpectrumVisualizer.UI.Trend;

/// <summary>
/// The trend plot: the cache folded to one column per pixel over the navigation window, the failure strip and the
/// shown moment; every member runs on the UI thread.
/// </summary>
public sealed class TrendChartViewModel : ReactiveObject, IDisposable
{
	private const double YPadding = 0.05;

	private static readonly TimeSpan _changeSample = TimeSpan.FromMilliseconds(100);
	private static readonly TimeSpan _liveTick = TimeSpan.FromSeconds(1);

	// While following, the window ends at the newest point, which the fold's half-open range would leave out.
	private static readonly TimeSpan _newestPointInclusion = TimeSpan.FromMilliseconds(1);

	private readonly IntensityCache _cache;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly TimeProvider _timeProvider;
	private readonly IScheduler _uiScheduler;
	private readonly ILogger<TrendChartViewModel> _logger;
	private readonly EnvelopeLine _envelope = new();
	private readonly TrendMarkerStrip _strip = new();
	private readonly VerticalLine _momentLine;
	private readonly Subject<Unit> _redrawRequests = new();
	private readonly CompositeDisposable _disposables = [];
	private TrendNavigationModel _navigation;
	private double _dataAreaPixels = TrendColumns.MaximumColumns;
	private LastFold? _lastFold;

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
		_navigation = new TrendNavigationModel(now - TrendPresets.Default.Width, now, now, isSticky: true);
		Width = _navigation.Width;

		Plot.Add.Plottable(_envelope);
		Plot.Add.Plottable(_strip);
		_momentLine = Plot.Add.VerticalLine(0.0);
		_momentLine.IsVisible = false;
		Plot.Axes.Bottom.TickGenerator = new DateTimeAutomatic();
		Plot.Axes.Bottom.Label.Text = Resources.TrendAxisTime;
		Plot.Axes.Left.Label.Text = Resources.TrendAxisIntensity;
		Plot.Axes.SetLimitsY(0.0, 1.0);
		Plot.Font.Automatic();

		_disposables.Add(cache.Changed
			.Sample(_changeSample, uiScheduler)
			.Merge(LiveTicks(uiScheduler))
			.StartWith(Unit.Default)
			.Subscribe(_ => RefreshReporting(), ReportPipelineFailure));
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
		if (_cache.NewestAt is null)
		{
			return;
		}

		var now = _timeProvider.GetUtcNow();
		_navigation.Zoom(factor, anchor);
		if (now > _navigation.To || now < _navigation.From)
		{
			_navigation.DetachSticky();
		}

		Refresh();
	}

	/// <summary>Moves the window; a pan that leaves now outside it stops following.</summary>
	public void PanBy(TimeSpan delta)
	{
		if (_cache.NewestAt is null)
		{
			return;
		}

		_navigation.Pan(delta, _timeProvider.GetUtcNow());
		Refresh();
	}

	/// <summary>Moves the window's centre to the moment, clamped at the first point, and stops following.</summary>
	public void CentreOn(DateTimeOffset moment)
	{
		if (_cache.NewestAt is null)
		{
			return;
		}

		_navigation.Pan(moment - (_navigation.From + (_navigation.Width / 2)), _timeProvider.GetUtcNow());
		_navigation.DetachSticky();
		Refresh();
	}

	/// <summary>Sets the width and keeps the right edge; following stays as it was.</summary>
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
		if (pixels <= 0 || pixels.Equals(_dataAreaPixels))
		{
			return;
		}

		_dataAreaPixels = pixels;
		Refresh();
	}

	/// <summary>The hover text of the last fold's column at the moment; null outside the window or before a point.</summary>
	public string? ReadoutAt(DateTimeOffset moment)
	{
		if (_lastFold is not { } fold)
		{
			return null;
		}

		var index = fold.Query.ColumnOf(moment);
		return index < 0 || index >= fold.Series.Columns.Length
			? null
			: TrendReadout.For(fold.Series, index, fold.Key);
	}

	public void ApplyPalette(ChartColors colors)
	{
		_envelope.Recolor(colors.Envelope);
		_strip.Recolor(colors.FailureStrip);
		lock (Plot.Sync)
		{
			_momentLine.Color = colors.MomentLine;
		}

		RequestRedraw();
	}

	public void Dispose()
	{
		_disposables.Dispose();
		_redrawRequests.OnCompleted();
		_redrawRequests.Dispose();
		Plot.Dispose();
	}

	/// <summary>A failed refresh is reported and skipped, so the next cache change refreshes again.</summary>
	private void RefreshReporting()
	{
		try
		{
			Refresh();
		}
		catch (Exception exception)
		{
			ReportPipelineFailure(exception);
		}
	}

	private void Refresh()
	{
		var key = _cache.Key;
		var first = _cache.FirstPointAt;
		var newest = _cache.NewestAt;
		var hasPoints = key is not null && first is not null && newest is not null;
		if (hasPoints)
		{
			var now = _timeProvider.GetUtcNow();
			TrackExtent(first!.Value, now);
			_navigation.OnLiveEdge(now);
		}

		_lastFold = hasPoints ? Fold(key!.Value) : null;
		var columns = _lastFold is { } fold ? TrendColumns.Map(fold.Series, LocalTimeAxis.ToAxis) : [];
		lock (Plot.Sync)
		{
			_envelope.ReplaceColumns(columns);
			_strip.ReplaceSpans(_lastFold is { } marked ? FailureSpans(marked.Series) : []);
			Plot.Axes.SetLimitsX(LocalTimeAxis.ToAxis(_navigation.From), LocalTimeAxis.ToAxis(_navigation.To));
			if (VerticalRange(columns) is { } range)
			{
				Plot.Axes.SetLimitsY(range.Bottom, range.Top);
			}

			Plot.Axes.Left.Label.Text = key is { } shownKey
				? Resources.FormatTrendAxisIntensityFormat(shownKey.WavelengthNm, shownKey.Delta)
				: Resources.TrendAxisIntensity;
		}

		HasPoints = hasPoints;
		Width = _navigation.Width;
		IsFollowing = _navigation.IsSticky;
		RequestRedraw();
	}

	/// <summary>The first point opens the window at now; a moved first point moves only the pan floor.</summary>
	private void TrackExtent(DateTimeOffset first, DateTimeOffset now)
	{
		if (!HasPoints)
		{
			_navigation = new TrendNavigationModel(now - _navigation.Width, now, first, isSticky: true);
		}
		else if (first != _navigation.FirstSample)
		{
			_navigation = new TrendNavigationModel(_navigation.From, _navigation.To, first, _navigation.IsSticky);
		}
	}

	private LastFold Fold(TrendKey key)
	{
		var columnWidth = TrendColumns.ColumnWidth(_navigation.Width, _dataAreaPixels);
		var query = IntensityQuery
			.Create(key.WavelengthNm, key.Delta, _navigation.From, _navigation.To + _newestPointInclusion, columnWidth)
			.Value;
		return new LastFold(query, _cache.Fold(query), key);
	}

	/// <summary>While following, the window's right edge is now, so a silence shows as an empty span within a second.</summary>
	private IObservable<Unit> LiveTicks(IScheduler uiScheduler)
	{
		return Observable.Interval(_liveTick, uiScheduler)
			.Where(_ => IsFollowing)
			.Select(_ => Unit.Default);
	}

	private static List<StripSpan> FailureSpans(IntensitySeries series)
	{
		return
		[
			.. TrendColumns.FailureRanges(series)
				.Select(range => new StripSpan(LocalTimeAxis.ToAxis(range.From), LocalTimeAxis.ToAxis(range.To))),
		];
	}

	/// <summary>The drawn values padded by 5 % of their span, plus or minus 1 when flat; null when none is drawn.</summary>
	private static (double Bottom, double Top)? VerticalRange(IReadOnlyList<EnvelopeColumn> columns)
	{
		var bottom = double.PositiveInfinity;
		var top = double.NegativeInfinity;
		foreach (var column in columns)
		{
			if (EnvelopePath.IsGap(column))
			{
				continue;
			}

			bottom = Math.Min(bottom, column.Min);
			top = Math.Max(top, column.Max);
		}

		if (double.IsInfinity(bottom))
		{
			return null;
		}

		if (bottom.Equals(top))
		{
			return (bottom - 1.0, top + 1.0);
		}

		var padding = (top - bottom) * YPadding;
		return (bottom - padding, top + padding);
	}

	private void MarkMoment(DateTimeOffset? moment)
	{
		lock (Plot.Sync)
		{
			_momentLine.IsVisible = moment is not null;
			if (moment is { } shown)
			{
				_momentLine.X = LocalTimeAxis.ToAxis(shown);
			}
		}

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

	private sealed record LastFold(IntensityQuery Query, IntensitySeries Series, TrendKey Key);
}
