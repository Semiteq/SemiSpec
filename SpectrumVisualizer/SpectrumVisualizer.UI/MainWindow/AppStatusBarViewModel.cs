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

namespace SpectrumVisualizer.UI.MainWindow;

/// <summary>The colour of the status bar's dot; the link text beside it always names the state as well.</summary>
public enum LinkTone
{
	Neutral,
	Ok,
	Fault,
}

/// <summary>
/// The link's current state as the status bar shows it. The fault entries and the restored entry it writes go to
/// the message panel.
/// </summary>
public sealed class AppStatusBarViewModel : ReactiveObject, IDisposable
{
	private readonly CompositeDisposable _disposables = [];
	private readonly MessagePanelViewModel _messagePanel;
	private readonly IScheduler _uiScheduler;
	private readonly ILogger<AppStatusBarViewModel> _logger;

	private bool _hasSeenFault;

	public AppStatusBarViewModel(
		SerialLink serialLink,
		MessagePanelViewModel messagePanel,
		IScheduler uiScheduler,
		ILogger<AppStatusBarViewModel> logger)
	{
		_messagePanel = messagePanel;
		_uiScheduler = uiScheduler;
		_logger = logger;
		Status = serialLink.Status;

		// The seed follows the scheduler hop, so the bar shows the link's state from construction on.
		_disposables.Add(Observable
			.FromEventPattern<LinkStatus>(
				handler => serialLink.StatusChanged += handler,
				handler => serialLink.StatusChanged -= handler)
			.Select(pattern => pattern.EventArgs)
			.ObserveOn(uiScheduler)
			.StartWith(serialLink.Status)
			.Subscribe(Apply, exception => _logger.LogError(exception, "The status bar stopped following the link")));
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

	public string PortText => IsDisconnected ? Resources.StatusNoValue : Status.PortName ?? Resources.StatusNoValue;

	public string Atp2000Text => Resources.FormatStatusSpectrometerFormat(
		"ATP-2000",
		SpectrometerText(BoardState.Atp2000Connected, BoardState.Atp2000MeasurementFailed));

	public string Atp8000Text => Resources.FormatStatusSpectrometerFormat(
		"ATP-8000",
		SpectrometerText(BoardState.Atp8000Connected, BoardState.Atp8000MeasurementFailed));

	public string LostSpectraText => Resources.FormatStatusLostSpectraFormat(
		IsDisconnected ? Resources.StatusNoValue : Status.LostSpectra.ToString(Resources.Culture));

	private bool IsDisconnected => Status.State == LinkState.Disconnected;

	public void Dispose()
	{
		_disposables.Dispose();
	}

	private string SpectrometerText(BoardState connectedBit, BoardState failedBit)
	{
		return Status.Board switch
		{
			null => Resources.StatusSpectrometerNoData,
			{ } board when !board.HasFlag(connectedBit) => Resources.StatusSpectrometerNotConnected,
			{ } board when board.HasFlag(failedBit) => Resources.StatusSpectrometerFailed,
			_ => Resources.StatusSpectrometerConnected
		};
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
