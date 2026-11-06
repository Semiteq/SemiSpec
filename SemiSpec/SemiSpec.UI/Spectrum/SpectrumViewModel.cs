using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiSpec.Core.Spectrum;
using SemiSpec.UI.Localization;
using SemiSpec.UI.Messages;

namespace SemiSpec.UI.Spectrum;

/// <summary>The spectrum screen: shows the source's latest spectrum on the chart and in the signal pane.</summary>
public sealed class SpectrumViewModel : ReactiveObject, IDisposable
{
	private readonly CompositeDisposable _disposables = [];
	private readonly MessagePanelViewModel _messagePanel;
	private readonly IScheduler _uiScheduler;
	private readonly ILogger<SpectrumViewModel> _logger;

	public SpectrumViewModel(
		SwitchingSpectrumSource source,
		MessagePanelViewModel messagePanel,
		TimeProvider timeProvider,
		IScheduler uiScheduler,
		ILogger<SpectrumViewModel> logger)
	{
		_messagePanel = messagePanel;
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

		var shownMoment = source.WhenAnyValue(switching => switching.ShownMoment);
		_disposables.Add(shownMoment
			.Select(HistoricalTextOf)
			.BindTo(Chart, chart => chart.HistoricalText));

		var canStickToZero = Chart.WhenAnyValue(chart => chart.HasSpectrum);
		var canResetScale = Chart.WhenAnyValue(
			chart => chart.HasSpectrum,
			chart => chart.IsFollowing,
			(hasSpectrum, isFollowing) => hasSpectrum && !isFollowing);

		_disposables.Add(ResetScaleCommand = ReactiveCommand.Create(Chart.ResetScale, canResetScale));
		_disposables.Add(StickToZeroCommand = ReactiveCommand.Create(Chart.StickToZero, canStickToZero));
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
		set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public bool IsLogarithmic
	{
		get;
		set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public ReactiveCommand<Unit, Unit> ResetScaleCommand { get; }

	public ReactiveCommand<Unit, Unit> StickToZeroCommand { get; }

	public ReactiveCommand<Unit, Unit> BackToLiveCommand { get; }

	public void Dispose()
	{
		_disposables.Dispose();
	}

	private static string HistoricalTextOf(DateTimeOffset? moment)
	{
		return moment is { } shown
			? Resources.FormatSpectrumHistoricalFormat(ClockText.TimeOfDay(shown))
			: string.Empty;
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
