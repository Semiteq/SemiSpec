using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using ReactiveUI.Avalonia;

using SpectrumVisualizer.Tests.Unit.UI.Link;
using SpectrumVisualizer.Tests.Unit.UI.Spectrum;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Logging;
using SpectrumVisualizer.UI.MainWindow;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Spectrum;

using MainWindowView = SpectrumVisualizer.UI.MainWindow.MainWindow;

namespace SpectrumVisualizer.Tests.Unit.UI.MainWindow;

/// <summary>A shown main window over a real link and the fake port, for the desktop UI checklist.</summary>
public abstract class MainWindowLayoutTestBase : IDisposable
{
	protected const string PortName = "COM3";

	private readonly MessagePanelViewModel _messagePanel = new(TimeProvider.System);
	private ConnectionBarViewModel? _connectionBar;
	private AppStatusBarViewModel? _statusBar;
	private SpectrumViewModel? _spectrum;
	private MainWindowViewModel? _viewModel;
	private MainWindowView? _window;

	protected MainWindowLayoutTestBase()
	{
		SerialLink = new SerialLink(PortProvider, LinkScheduler, NullLogger<SerialLink>.Instance);
	}

	protected TestScheduler LinkScheduler { get; } = new();

	private protected FakeSerialPortProvider PortProvider { get; } = new(PortName);

	private protected FakePortStream Port { get; } = new();

	protected SerialLink SerialLink { get; }

	private protected FakeDisplayedSpectrumSource SpectrumSource { get; } = new();

	protected AppStatusBarViewModel StatusBar =>
		_statusBar ?? throw new InvalidOperationException("Realise the window first.");

	public void Dispose()
	{
		_window?.Close();
		_viewModel?.Dispose();
		_connectionBar?.Dispose();
		_statusBar?.Dispose();
		_spectrum?.Dispose();
		SpectrumSource.Dispose();
		SerialLink.Dispose();
		Port.Dispose();
		_messagePanel.Dispose();
		GC.SuppressFinalize(this);
	}

	protected static T Named<T>(Control root, string name)
		where T : Control
	{
		return root.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name)
			?? throw new InvalidOperationException($"No control named {name}.");
	}

	/// <summary>Every text the row draws: its text blocks and the access text inside each button.</summary>
	protected static IEnumerable<TextBlock> Texts(Visual row)
	{
		return row.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible);
	}

	protected MainWindowView Realise()
	{
		_connectionBar = new ConnectionBarViewModel(
			PortProvider,
			SerialLink,
			_messagePanel,
			AvaloniaScheduler.Instance,
			NullLogger<ConnectionBarViewModel>.Instance);
		_statusBar = new AppStatusBarViewModel(
			SerialLink,
			_messagePanel,
			AvaloniaScheduler.Instance,
			NullLogger<AppStatusBarViewModel>.Instance);
		_spectrum = new SpectrumViewModel(
			SpectrumSource,
			_messagePanel,
			TimeProvider.System,
			AvaloniaScheduler.Instance,
			NullLogger<SpectrumViewModel>.Instance);
		_viewModel = new MainWindowViewModel(
			_messagePanel,
			_connectionBar,
			_statusBar,
			_spectrum,
			new LogLevelControl(),
			NullLogger<MainWindowViewModel>.Instance);
		_window = new MainWindowView { DataContext = _viewModel };
		_window.Show();
		Dispatcher.UIThread.RunJobs();

		return _window;
	}
}
