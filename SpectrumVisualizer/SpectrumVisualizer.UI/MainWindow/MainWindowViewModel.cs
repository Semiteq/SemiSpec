using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ReactiveUI;

using SpectrumVisualizer.UI.Logging;
using SpectrumVisualizer.UI.Messages;

namespace SpectrumVisualizer.UI.MainWindow;

public sealed class MainWindowViewModel : ReactiveObject, IDisposable
{
	private readonly CompositeDisposable _disposables = [];
	private readonly Subject<Unit> _exitRequests = new();

	private readonly LogLevelControl _logLevelControl;

	public MainWindowViewModel(
		MessagePanelViewModel messagePanel,
		AppStatusBarViewModel statusBar,
		LogLevelControl logLevelControl)
	{
		MessagePanel = messagePanel;
		StatusBar = statusBar;
		_logLevelControl = logLevelControl;

		_disposables.Add(_exitRequests);
		_disposables.Add(ExitCommand = ReactiveCommand.Create(() => _exitRequests.OnNext(Unit.Default)));
		_disposables.Add(ToggleVerboseLoggingCommand = ReactiveCommand.Create(ToggleVerboseLogging));
	}

	/// <summary>The process-wide panel, owned by the container and shown in the window's panel row.</summary>
	public MessagePanelViewModel MessagePanel { get; }

	public AppStatusBarViewModel StatusBar { get; }

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
	}
}
