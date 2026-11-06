using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SpectrumVisualizer.UI.Logging;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Spectrum;

namespace SpectrumVisualizer.UI.MainWindow;

public sealed class MainWindowViewModel : ReactiveObject, IDisposable
{
	private readonly CompositeDisposable _disposables = [];
	private readonly Subject<Unit> _exitRequests = new();

	private readonly LogLevelControl _logLevelControl;
	private readonly ILogger<MainWindowViewModel> _logger;

	public MainWindowViewModel(
		MessagePanelViewModel messagePanel,
		ConnectionBarViewModel connectionBar,
		AppStatusBarViewModel statusBar,
		SpectrumViewModel spectrum,
		LogLevelControl logLevelControl,
		ILogger<MainWindowViewModel> logger)
	{
		MessagePanel = messagePanel;
		ConnectionBar = connectionBar;
		StatusBar = statusBar;
		Spectrum = spectrum;
		_logLevelControl = logLevelControl;
		_logger = logger;

		_disposables.Add(_exitRequests);
		_disposables.Add(ExitCommand = ReactiveCommand.Create(() => _exitRequests.OnNext(Unit.Default)));
		_disposables.Add(ToggleVerboseLoggingCommand = ReactiveCommand.Create(ToggleVerboseLogging));
	}

	/// <summary>The process-wide panel, owned by the container and shown in the window's panel row.</summary>
	public MessagePanelViewModel MessagePanel { get; }

	public ConnectionBarViewModel ConnectionBar { get; }

	public AppStatusBarViewModel StatusBar { get; }

	public SpectrumViewModel Spectrum { get; }

	public IObservable<Unit> ExitRequests => _exitRequests.AsObservable();

	/// <summary>
	/// Mirrors the log level control, which raises no notification; the toggle command is its one writer.
	/// </summary>
	public bool IsVerboseLogging => _logLevelControl.IsVerbose;

	public ReactiveCommand<Unit, Unit> ExitCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleVerboseLoggingCommand { get; }

	public void Dispose()
	{
		_disposables.Dispose();
	}

	private void ToggleVerboseLogging()
	{
		_logLevelControl.IsVerbose = !_logLevelControl.IsVerbose;
		this.RaisePropertyChanged(nameof(IsVerboseLogging));

		_logger.LogInformation("Minimum log level set to {Level}", _logLevelControl.MinimumLevel);
		_logger.LogDebug("Debug events now reach the log file");
	}
}
