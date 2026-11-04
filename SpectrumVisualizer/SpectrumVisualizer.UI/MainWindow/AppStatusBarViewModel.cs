using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Store;

namespace SpectrumVisualizer.UI.MainWindow;

/// <summary>The colour of the status bar's dot; the link text beside it always names the state as well.</summary>
public enum LinkTone
{
	Neutral,
	Ok,
	Fault,
}

/// <summary>
/// The link's current state and the run's dropped frames as the status bar shows them. The fault entries and the
/// restored entry it writes go to the message panel.
/// </summary>
public sealed class AppStatusBarViewModel : ReactiveObject, IDisposable
{
	private readonly CompositeDisposable _disposables = [];
	private readonly MessagePanelViewModel _messagePanel;
	private readonly IScheduler _uiScheduler;
	private readonly ILogger<AppStatusBarViewModel> _logger;
	private readonly ObservableAsPropertyHelper<string> _droppedFramesText;

	private bool _hasSeenFault;

	public AppStatusBarViewModel(
		SerialLink serialLink,
		SpectrumStore store,
		MessagePanelViewModel messagePanel,
		IScheduler uiScheduler,
		ILogger<AppStatusBarViewModel> logger)
	{
		_messagePanel = messagePanel;
		_uiScheduler = uiScheduler;
		_logger = logger;
		Status = serialLink.Status;

		_disposables.Add(serialLink
			.StatusUpdates(uiScheduler)
			.Subscribe(Apply, exception => _logger.LogError(exception, "The status bar stopped following the link")));
		_disposables.Add(DroppedFramesOf(store, uiScheduler)
			.Select(dropped => Resources.FormatStatusDroppedFramesFormat(dropped.ToString(Resources.Culture)))
			.ToProperty(this, bar => bar.DroppedFramesText, out _droppedFramesText));
	}

	public LinkStatus Status
	{
		get;
		private set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(LinkText));
			this.RaisePropertyChanged(nameof(LinkTone));
			this.RaisePropertyChanged(nameof(PortText));
			this.RaisePropertyChanged(nameof(Atp2000Text));
			this.RaisePropertyChanged(nameof(Atp8000Text));
			this.RaisePropertyChanged(nameof(LostSpectraText));
		}
	}

	public string LinkText => Status.State switch
	{
		LinkState.Disconnected => Resources.StatusNotConnected,
		LinkState.Connecting => Resources.StatusConnecting,
		LinkState.PortUnavailable => Resources.StatusPortUnavailable,
		LinkState.Waiting => Resources.StatusWaiting,
		LinkState.Receiving => Resources.StatusReceiving,
		LinkState.NotResponding => Resources.StatusNotResponding,
		LinkState.OtherDevice => Resources.StatusOtherDevice,
		_ => throw new ArgumentOutOfRangeException(nameof(Status), Status.State, null)
	};

	public LinkTone LinkTone => Status.State switch
	{
		LinkState.Receiving => LinkTone.Ok,
		LinkState.PortUnavailable or LinkState.NotResponding or LinkState.OtherDevice => LinkTone.Fault,
		_ => LinkTone.Neutral
	};

	public string PortText => Status.PortName ?? Resources.StatusNoValue;

	public string Atp2000Text => SpectrometerText(Spectrometer.Atp2000);

	public string Atp8000Text => SpectrometerText(Spectrometer.Atp8000);

	public string LostSpectraText => Resources.FormatStatusLostSpectraFormat(
		IsDisconnected ? Resources.StatusNoValue : Status.LostSpectra.ToString(Resources.Culture));

	/// <summary>The link's channel drops plus the store's; a number in every state, as both describe the run's history.</summary>
	public string DroppedFramesText => _droppedFramesText.Value;

	private bool IsDisconnected => Status.State == LinkState.Disconnected;

	public void Dispose()
	{
		_disposables.Dispose();
	}

	private IObservable<long> DroppedFramesOf(SpectrumStore store, IScheduler uiScheduler)
	{
		var linkDropped = this
			.WhenAnyValue(bar => bar.Status)
			.Select(status => status.DroppedFrames);
		var storeDropped = store.DroppedFramesChanged
			.ObserveOn(uiScheduler)
			.StartWith(store.DroppedFrames);

		return linkDropped
			.CombineLatest(storeDropped, (link, stored) => link + stored)
			.DistinctUntilChanged();
	}

	private string SpectrometerText(Spectrometer device)
	{
		var state = Status.Board switch
		{
			null => Resources.StatusSpectrometerNoData,
			{ } board when !board.HasFlag(device.ConnectedBit) => Resources.StatusSpectrometerNotConnected,
			{ } board when board.HasFlag(device.FailedBit) => Resources.StatusSpectrometerFailed,
			_ => Resources.StatusSpectrometerConnected
		};

		return Resources.FormatStatusSpectrometerFormat(device.DisplayName, state);
	}

	private void Apply(LinkStatus status)
	{
		try
		{
			Status = status;

			if (status.Fault is { } fault)
			{
				_hasSeenFault = true;
				_messagePanel.ReportFailure(fault, _logger);

				return;
			}

			// A new Connect starts clean: its first Receiving restores nothing the operator saw fail.
			if (status.State == LinkState.Connecting)
			{
				_hasSeenFault = false;
			}

			if (status.State == LinkState.Receiving && _hasSeenFault)
			{
				ReportRestored();
			}
		}
		catch (Exception handlerFailure)
		{
			_messagePanel.TryReportFailure(new ExceptionalError(handlerFailure), _logger, _uiScheduler);
		}
	}

	private void ReportRestored()
	{
		var restored = new MessageView(
			Resources.LinkRestoredTitle,
			Resources.LinkRestoredDetail,
			string.Empty,
			MessageSeverity.Info);

		// The one entry with no error behind it, so its log line is written here rather than by ResultReporting.
		_logger.LogInformation("{Title}. {Detail}", restored.Title, restored.Detail);

		// Cleared after the entry lands: a throw out of the report would otherwise leave the flag down and no
		// later Receiving would write the restored entry either.
		_messagePanel.Report(restored);
		_hasSeenFault = false;
	}
}
