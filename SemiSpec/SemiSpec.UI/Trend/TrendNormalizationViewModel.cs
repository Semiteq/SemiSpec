using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiSpec.Core.Trend;
using SemiSpec.UI.Link;
using SemiSpec.UI.Localization;
using SemiSpec.UI.Messages;
using SemiSpec.UI.Spectrum;

namespace SemiSpec.UI.Trend;

/// <summary>
/// The trend's normalization switch, Off, x100 and x1000: a factor segment starts or switches the cache's
/// normalization, Off stops it, and the segments mirror the cache, its one writer; every member runs on the UI thread.
/// </summary>
public sealed class TrendNormalizationViewModel : ReactiveObject, IDisposable
{
	public const int SmallFactor = 100;
	public const int LargeFactor = 1000;

	private static readonly TimeSpan _availabilityTick = TimeSpan.FromSeconds(1);

	private readonly IntensityCache _cache;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly TimeProvider _timeProvider;
	private readonly IScheduler _uiScheduler;
	private readonly ILogger<TrendNormalizationViewModel> _logger;
	private readonly CompositeDisposable _disposables = [];
	private LinkState _linkState = LinkState.Disconnected;

	/// <summary>linkStatuses starts with the current status and emits on the UI scheduler.</summary>
	public TrendNormalizationViewModel(
		IntensityCache cache,
		IObservable<LinkStatus> linkStatuses,
		MessagePanelViewModel messagePanel,
		TimeProvider timeProvider,
		IScheduler uiScheduler,
		ILogger<TrendNormalizationViewModel> logger)
	{
		_cache = cache;
		_messagePanel = messagePanel;
		_timeProvider = timeProvider;
		_uiScheduler = uiScheduler;
		_logger = logger;
		Factor = cache.Normalization.Factor;

		_disposables.Add(cache.NormalizationChanged
			.ObserveOn(uiScheduler)
			.Subscribe(Mirror, ReportPipelineFailure));
		_disposables.Add(linkStatuses.Subscribe(
			status =>
			{
				_linkState = status.State;
				CanPress = ComputeCanPress();
			},
			ReportPipelineFailure));
		_disposables.Add(cache.Changed.Select(_ => Unit.Default)
			.Merge(Observable.Interval(_availabilityTick, uiScheduler).Select(_ => Unit.Default))
			.Sample(_availabilityTick, uiScheduler)
			.StartWith(Unit.Default)
			.Subscribe(_ => CanPress = ComputeCanPress(), ReportPipelineFailure));

		var canPress = this.WhenAnyValue(normalization => normalization.CanPress);
		_disposables.Add(NormalizeSmallCommand = ReactiveCommand.Create(() => Press(SmallFactor), canPress));
		_disposables.Add(NormalizeLargeCommand = ReactiveCommand.Create(() => Press(LargeFactor), canPress));
		_disposables.Add(TurnOffCommand = ReactiveCommand.Create(
			TurnOff,
			this.WhenAnyValue(normalization => normalization.Factor).Select(factor => factor is not null)));
	}

	/// <summary>The factor the trend is normalized to; null while it shows raw intensity.</summary>
	public int? Factor
	{
		get;
		private set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(IsOff));
			this.RaisePropertyChanged(nameof(IsSmallFactor));
			this.RaisePropertyChanged(nameof(IsLargeFactor));
		}
	}

	public bool IsOff => Factor is null;

	public bool IsSmallFactor => Factor == SmallFactor;

	public bool IsLargeFactor => Factor == LargeFactor;

	/// <summary>True while a fresh live spectrum whose spectrometer covers the wavelength is at hand.</summary>
	public bool CanPress
	{
		get;
		private set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(UnavailableReason));
		}
	}

	/// <summary>Why the factor segments are disabled; null while they can be pressed.</summary>
	public string? UnavailableReason => CanPress ? null : Resources.TrendNormalizeUnavailableTip;

	public ReactiveCommand<Unit, Unit> NormalizeSmallCommand { get; }

	public ReactiveCommand<Unit, Unit> NormalizeLargeCommand { get; }

	/// <summary>Enabled whenever the trend is normalized, also while the factor segments are disabled.</summary>
	public ReactiveCommand<Unit, Unit> TurnOffCommand { get; }

	public void Dispose()
	{
		_disposables.Dispose();
	}

	/// <summary>The active factor's segment changes nothing; the other one fixes a new reference at its factor.</summary>
	private void Press(int factor)
	{
		if (Factor == factor)
		{
			return;
		}

		if (_cache.StartNormalization(factor) is { IsFailed: true } refused)
		{
			_messagePanel.ReportFailure(refused.Errors[0], _logger);
		}

		Factor = _cache.Normalization.Factor;
	}

	private void TurnOff()
	{
		_cache.StopNormalization();
		Factor = _cache.Normalization.Factor;
	}

	private void Mirror(NormalizationState state)
	{
		Factor = state.Factor;
		if (state.EndedByKeyChange)
		{
			_messagePanel.Report(new MessageView(
				Resources.NormalizationResetTitle,
				Resources.NormalizationResetDetail,
				string.Empty,
				MessageSeverity.Info));
			_logger.LogInformation("Normalization switched off: the wavelength or the measurement window changed");
		}
	}

	private bool ComputeCanPress()
	{
		return _cache.CanNormalize
			&& _cache.NewestSpectrumAt is { } newest
			&& !SpectrumStaleness.IsStale(_linkState, newest, _timeProvider.GetUtcNow());
	}

	private void ReportPipelineFailure(Exception exception)
	{
		_messagePanel.TryReportFailure(new ExceptionalError(exception.Message, exception), _logger, _uiScheduler);
	}
}
