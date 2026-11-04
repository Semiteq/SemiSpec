using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SpectrumVisualizer.UI.Messages;

namespace SpectrumVisualizer.UI.Spectrum;

/// <summary>The spectrum screen: shows the source's latest spectrum on the chart and in the signal pane.</summary>
public sealed class SpectrumViewModel : ReactiveObject, IDisposable
{
	private readonly CompositeDisposable _disposables = [];
	private readonly MessagePanelViewModel _messagePanel;
	private readonly IScheduler _uiScheduler;
	private readonly ILogger<SpectrumViewModel> _logger;

	public SpectrumViewModel(
		IDisplayedSpectrumSource source,
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

		_disposables.Add(source.Changed
			.ObserveOn(uiScheduler)
			.StartWith(Unit.Default)
			.Select(_ => source.Current)
			.WhereNotNull()
			.DistinctUntilChanged<DisplayedSpectrum>(ReferenceEqualityComparer.Instance)
			.Do(Show)
			.Subscribe(_ => { }, ReportPipelineFailure));

		_disposables.Add(this
			.WhenAnyValue(spectrum => spectrum.IsInverted, spectrum => spectrum.IsLogarithmic)
			.Subscribe(display => Chart.SetDisplay(display.Item1, display.Item2)));

		var canStickToZero = Chart.WhenAnyValue(chart => chart.HasSpectrum);
		var canResetScale = Chart.WhenAnyValue(
			chart => chart.HasSpectrum,
			chart => chart.IsFollowing,
			(hasSpectrum, isFollowing) => hasSpectrum && !isFollowing);

		_disposables.Add(ResetScaleCommand = ReactiveCommand.Create(Chart.ResetScale, canResetScale));
		_disposables.Add(StickToZeroCommand = ReactiveCommand.Create(Chart.StickToZero, canStickToZero));
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

	public void Dispose()
	{
		_disposables.Dispose();
	}

	private void Show(DisplayedSpectrum spectrum)
	{
		Chart.Show(spectrum);
		SignalInfo.Spectrum = spectrum;
	}

	private void ReportPipelineFailure(Exception exception)
	{
		_messagePanel.TryReportFailure(new ExceptionalError(exception.Message, exception), _logger, _uiScheduler);
	}
}
