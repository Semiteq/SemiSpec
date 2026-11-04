using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using ReactiveUI;

using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Localization;

namespace SpectrumVisualizer.UI.MainWindow;

/// <summary>The connection row: the port list and the commands that connect the link and disconnect it.</summary>
public sealed class ConnectionBarViewModel : ReactiveObject, IDisposable
{
	private readonly CompositeDisposable _disposables = [];
	private readonly ISerialPortProvider _serialPortProvider;
	private readonly SerialLink _serialLink;
	private readonly ObservableAsPropertyHelper<bool> _isIdle;

	public ConnectionBarViewModel(ISerialPortProvider serialPortProvider, SerialLink serialLink, IScheduler uiScheduler)
	{
		_serialPortProvider = serialPortProvider;
		_serialLink = serialLink;

		_disposables.Add(serialLink
			.StatusUpdates(uiScheduler)
			.Select(status => status.State == LinkState.Disconnected)
			.ToProperty(this, bar => bar.IsIdle, out _isIdle));

		var canConnect = this.WhenAnyValue(
			bar => bar.IsIdle,
			bar => bar.SelectedPort,
			(isIdle, port) => isIdle && port is not null);
		var canDisconnect = this.WhenAnyValue(bar => bar.IsIdle)
			.Select(isIdle => !isIdle);

		_disposables.Add(RefreshCommand = ReactiveCommand.Create(RefreshPorts));
		_disposables.Add(ConnectCommand = ReactiveCommand.Create(Connect, canConnect));
		_disposables.Add(DisconnectCommand = ReactiveCommand.CreateFromTask(serialLink.DisconnectAsync, canDisconnect));
	}

	public ObservableCollection<string> Ports { get; } = [];

	public string PortPlaceholder => Ports.Count == 0 ? Resources.ConnectionPortPlaceholder : string.Empty;

	public string? SelectedPort
	{
		get;
		set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>True while the link is Disconnected: the port can change and Connect can start a session.</summary>
	public bool IsIdle => _isIdle.Value;

	public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

	public ReactiveCommand<Unit, Unit> ConnectCommand { get; }

	public ReactiveCommand<Unit, Unit> DisconnectCommand { get; }

	/// <summary>Reads the port list again and keeps the selection while its port is still listed.</summary>
	public void RefreshPorts()
	{
		var selectedPort = SelectedPort;
		var portNames = _serialPortProvider.PortNames();

		// Clearing the bound list pushes null into SelectedPort, so the selection is restored after the adds.
		Ports.Clear();
		foreach (var portName in portNames)
		{
			Ports.Add(portName);
		}

		SelectedPort = selectedPort is not null && portNames.Contains(selectedPort) ? selectedPort : null;
		this.RaisePropertyChanged(nameof(PortPlaceholder));
	}

	public void Dispose()
	{
		_disposables.Dispose();
	}

	private void Connect()
	{
		if (SelectedPort is { } portName)
		{
			_serialLink.Connect(portName);
		}
	}
}
