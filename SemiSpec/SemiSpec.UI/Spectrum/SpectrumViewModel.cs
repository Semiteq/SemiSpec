using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiSpec.Core.Spectrum;
using SemiSpec.UI.Chart;
using SemiSpec.UI.Link;
using SemiSpec.UI.Localization;
using SemiSpec.UI.Messages;

namespace SemiSpec.UI.Spectrum;

/// <summary>
/// The spectrum screen: shows the source's latest spectrum on the chart and in the readout, and owns the chart's
/// modes and the forms of its axis flyouts.
/// </summary>
public sealed class SpectrumViewModel : ReactiveObject, IDisposable
{
	private static readonly TimeSpan _staleTick = TimeSpan.FromSeconds(1);

	private readonly CompositeDisposable _disposables = [];
	private readonly SwitchingSpectrumSource _source;
	private readonly SerialLink _serialLink;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly TimeProvider _timeProvider;
	private readonly IScheduler _uiScheduler;
	private readonly ILogger<SpectrumViewModel> _logger;
	private readonly ObservableAsPropertyHelper<string> _stateText;

	public SpectrumViewModel(
		SwitchingSpectrumSource source,
		SerialLink serialLink,
		MessagePanelViewModel messagePanel,
		TimeProvider timeProvider,
		IScheduler uiScheduler,
		ILogger<SpectrumViewModel> logger)
	{
		_source = source;
		_serialLink = serialLink;
		_messagePanel = messagePanel;
		_timeProvider = timeProvider;
		_uiScheduler = uiScheduler;
		_logger = logger;
		Chart = new SpectrumChartViewModel();
		SignalInfo = new SignalInfoViewModel(timeProvider);

		_disposables.Add(source.CurrentChanged
			.ObserveOn(uiScheduler)
			.StartWith(Unit.Default)
			.Select(_ => ReadCurrent(source))
			.WhereNotNull()
			.DistinctUntilChanged(spectrum => spectrum.Measurement, ReferenceEqualityComparer.Instance)
			.Subscribe(Show, ReportPipelineFailure));

		_disposables.Add(this
			.WhenAnyValue(
				spectrum => spectrum.IsInverted,
				spectrum => spectrum.IsLogarithmic,
				(inverted, logarithmic) => new SpectrumDisplay(inverted, logarithmic))
			.Subscribe(Chart.SetDisplay, ReportPipelineFailure));
		_disposables.Add(this
			.WhenAnyValue(spectrum => spectrum.IsFromZero)
			.Subscribe(Chart.SetFromZero, ReportPipelineFailure));

		var shownMoment = source.WhenAnyValue(switching => switching.ShownMoment);
		_disposables.Add(StateTextChanges(shownMoment)
			.Select(_ => ComposeStateText())
			.DistinctUntilChanged()
			.ToProperty(this, spectrum => spectrum.StateText, out _stateText, string.Empty));

		_disposables.Add(ToggleInvertCommand = ReactiveCommand.Create(() => Reseeding(() => IsInverted = !IsInverted)));
		_disposables.Add(ToggleLogarithmicCommand = ReactiveCommand.Create(
			() => Reseeding(() => IsLogarithmic = !IsLogarithmic)));
		_disposables.Add(ToggleFromZeroCommand = ReactiveCommand.Create(() => Reseeding(() => IsFromZero = !IsFromZero)));
		_disposables.Add(AutoscaleCommand = ReactiveCommand.Create(() => Reseeding(Chart.ToggleAutoscale)));
		_disposables.Add(VerticalForm = new AxisRangeForm(Chart.VerticalRange, Chart.SetVerticalRange));
		_disposables.Add(HorizontalForm = new AxisRangeForm(Chart.HorizontalRange, Chart.SetHorizontalRange));
		_disposables.Add(BackToLiveCommand = ReactiveCommand.Create(
			source.ReturnToLive,
			shownMoment.Select(moment => moment is not null)));
		_disposables.Add(Chart);
	}

	public SpectrumChartViewModel Chart { get; }

	public SignalInfoViewModel SignalInfo { get; }

	public bool IsInverted
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public bool IsLogarithmic
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>The following scale runs from zero to the maximum plus 5 %; off by default.</summary>
	public bool IsFromZero
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>The receive time of the latest live spectrum shown; null before the first one.</summary>
	public DateTimeOffset? LastLiveSpectrumAt
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>The chart's one-line state: a past moment or stale data, then the modes that are not default.</summary>
	public string StateText => _stateText.Value;

	public ReactiveCommand<Unit, Unit> ToggleInvertCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleLogarithmicCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleFromZeroCommand { get; }

	/// <summary>A frozen scale returns to following in the current mode; a following one freezes where it stands.</summary>
	public ReactiveCommand<Unit, Unit> AutoscaleCommand { get; }

	/// <summary>The intensity axis flyout's Maximum and Minimum.</summary>
	public AxisRangeForm VerticalForm { get; }

	/// <summary>The wavelength axis flyout's Start and End.</summary>
	public AxisRangeForm HorizontalForm { get; }

	public ReactiveCommand<Unit, Unit> BackToLiveCommand { get; }

	public void Dispose()
	{
		_disposables.Dispose();
	}

	/// <summary>A mode change moves the limits, so an open axis flyout shows them again.</summary>
	private void Reseeding(Action change)
	{
		change();
		VerticalForm.Seed();
		HorizontalForm.Seed();
	}

	private IObservable<Unit> StateTextChanges(IObservable<DateTimeOffset?> shownMoment)
	{
		var modes = this.WhenAnyValue(
			spectrum => spectrum.IsInverted,
			spectrum => spectrum.IsLogarithmic,
			spectrum => spectrum.IsFromZero,
			spectrum => spectrum.LastLiveSpectrumAt,
			(_, _, _, _) => Unit.Default);
		var following = Chart
			.WhenAnyValue(chart => chart.IsFollowing, chart => chart.HasSpectrum, (_, _) => Unit.Default);

		return modes
			.Merge(following)
			.Merge(shownMoment.Select(_ => Unit.Default))
			.Merge(_serialLink.StatusUpdates(_uiScheduler).Select(_ => Unit.Default))
			.Merge(Observable
				.Interval(_staleTick, DefaultScheduler.Instance)
				.ObserveOn(_uiScheduler)
				.Select(_ => Unit.Default));
	}

	private string ComposeStateText()
	{
		var parts = new List<string>();
		if (_source.ShownMoment is { } moment)
		{
			parts.Add(Resources.FormatSpectrumHistoricalFormat(ClockText.TimeOfDay(moment)));
		}
		else if (LastLiveSpectrumAt is { } lastLive
			&& SpectrumStaleness.IsStale(_serialLink.Status.State, lastLive, _timeProvider.GetUtcNow()))
		{
			parts.Add(Resources.FormatSpectrumStaleFormat(ClockText.TimeOfDay(lastLive)));
		}

		if (IsLogarithmic)
		{
			parts.Add(Resources.SpectrumStateLog);
		}

		if (IsInverted)
		{
			parts.Add(Resources.SpectrumStateInverted);
		}

		if (IsFromZero)
		{
			parts.Add(Resources.SpectrumStateFromZero);
		}

		if (Chart is { HasSpectrum: true, IsFollowing: false })
		{
			parts.Add(Resources.SpectrumStateFrozen);
		}

		return string.Join(Resources.ChartStateSeparator, parts);
	}

	/// <summary>A failed read is reported and skipped, so the next signal reads again.</summary>
	private DisplayedSpectrum? ReadCurrent(IDisplayedSpectrumSource source)
	{
		try
		{
			return source.Current;
		}
		catch (Exception exception)
		{
			ReportPipelineFailure(exception);
			return null;
		}
	}

	/// <summary>A failed show is reported and skipped, so the next spectrum still shows.</summary>
	private void Show(DisplayedSpectrum spectrum)
	{
		try
		{
			Chart.Show(spectrum);
			SignalInfo.Spectrum = spectrum;
			if (!spectrum.IsHistorical)
			{
				LastLiveSpectrumAt = spectrum.ReceivedAt;
			}
		}
		catch (Exception exception)
		{
			ReportPipelineFailure(exception);
		}
	}

	private void ReportPipelineFailure(Exception exception)
	{
		_messagePanel.TryReportFailure(new ExceptionalError(exception.Message, exception), _logger, _uiScheduler);
	}
}
