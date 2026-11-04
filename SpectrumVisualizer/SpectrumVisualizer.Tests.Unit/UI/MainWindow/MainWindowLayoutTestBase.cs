using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using ReactiveUI.Avalonia;

using SpectrumVisualizer.Tests.Unit.UI.Link;
using SpectrumVisualizer.Tests.Unit.UI.Spectrum;
using SpectrumVisualizer.Tests.Unit.UI.Store;
using SpectrumVisualizer.Tests.Unit.UI.Trend;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Logging;
using SpectrumVisualizer.UI.MainWindow;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Spectrum;

using MainWindowView = SpectrumVisualizer.UI.MainWindow.MainWindow;

namespace SpectrumVisualizer.Tests.Unit.UI.MainWindow;

/// <summary>A shown main window over a real link and the fake port, with the signal pane's parts and insets.</summary>
public abstract class MainWindowLayoutTestBase : IDisposable
{
	protected const string PortName = "COM3";

	/// <summary>A chart width at which the ATP-8000 wavelength axis still carries four labelled ticks.</summary>
	protected const int ChartFloorWidth = 400;

	protected const double LargeWidth = 2560;

	protected const double PaneBorder = 1;
	protected const double PanePadding = 16;
	protected const double RowSpacing = 8;
	protected const double LabelToField = 12;
	protected const double SectionHeaderAbove = 16;

	protected static readonly string[] PaneLabels =
		["DeviceLabel", "WavelengthLabel", "WindowLabel", "AmplitudeLabel", "SnrLabel", "QLabel", "UpdatedLabel"];

	protected static readonly string[] PaneValues =
		["DeviceValue", "WavelengthValue", "WindowValue", "AmplitudeValue", "SnrValue", "QValue", "UpdatedValue"];

	protected static readonly string[] PaneHeaders = ["SignalHeader", "ChartHeader"];

	protected static readonly string[] PaneControls =
		["InvertSpectrum", "LogarithmicScale", "ResetScale", "StickToZero"];

	private ConnectionBarViewModel? _connectionBar;
	private AppStatusBarViewModel? _statusBar;
	private SpectrumViewModel? _spectrum;
	private MainWindowViewModel? _viewModel;
	private MainWindowView? _window;

	protected MainWindowLayoutTestBase()
	{
		SerialLink = new SerialLink(PortProvider, LinkScheduler, NullLogger<SerialLink>.Instance);
		Store = new UnstartedStore(MessagePanel);
		SwitchingSource = new SwitchingSpectrumSource(SpectrumSource);
		Trend = new TrendStand(SwitchingSource, MessagePanel);
	}

	protected TestScheduler LinkScheduler { get; } = new();

	private protected FakeSerialPortProvider PortProvider { get; } = new(PortName);

	private protected FakePortStream Port { get; } = new();

	protected SerialLink SerialLink { get; }

	private protected FakeDisplayedSpectrumSource SpectrumSource { get; } = new();

	protected SwitchingSpectrumSource SwitchingSource { get; }

	protected MessagePanelViewModel MessagePanel { get; } = new(TimeProvider.System);

	private protected UnstartedStore Store { get; }

	private protected TrendStand Trend { get; }

	protected AppStatusBarViewModel StatusBar =>
		_statusBar ?? throw new InvalidOperationException("Realise the window first.");

	protected SpectrumViewModel Spectrum =>
		_spectrum ?? throw new InvalidOperationException("Realise the window first.");

	public void Dispose()
	{
		_window?.Close();
		_viewModel?.Dispose();
		_connectionBar?.Dispose();
		_statusBar?.Dispose();
		_spectrum?.Dispose();
		Trend.Dispose();
		SwitchingSource.Dispose();
		SpectrumSource.Dispose();
		SerialLink.Dispose();
		Store.Dispose();
		Port.Dispose();
		MessagePanel.Dispose();
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
			MessagePanel,
			AvaloniaScheduler.Instance,
			NullLogger<ConnectionBarViewModel>.Instance);
		_statusBar = new AppStatusBarViewModel(
			SerialLink,
			Store.Store,
			MessagePanel,
			AvaloniaScheduler.Instance,
			NullLogger<AppStatusBarViewModel>.Instance);
		_spectrum = new SpectrumViewModel(
			SwitchingSource,
			MessagePanel,
			TimeProvider.System,
			AvaloniaScheduler.Instance,
			NullLogger<SpectrumViewModel>.Instance);
		_viewModel = new MainWindowViewModel(
			MessagePanel,
			_connectionBar,
			_statusBar,
			_spectrum,
			Trend.Trend,
			new LogLevelControl(),
			NullLogger<MainWindowViewModel>.Instance);
		_window = new MainWindowView { DataContext = _viewModel };
		_window.Show();
		Dispatcher.UIThread.RunJobs();

		return _window;
	}
}
