using System.Reactive.Concurrency;
using System.Reactive.Linq;

using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using ReactiveUI.Avalonia;

using SpectrumVisualizer.Tests.Unit.UI.Link;
using SpectrumVisualizer.Tests.Unit.UI.Spectrum;
using SpectrumVisualizer.Tests.Unit.UI.Store;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Logging;
using SpectrumVisualizer.UI.MainWindow;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Spectrum;

using Xunit;

using MainWindowView = SpectrumVisualizer.UI.MainWindow.MainWindow;

namespace SpectrumVisualizer.Tests.Unit.UI.MainWindow;

[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowViewTests : IDisposable
{
	private readonly MessagePanelViewModel _messagePanel = new(TimeProvider.System);
	private readonly SerialLink _serialLink;
	private readonly UnstartedStore _store;
	private readonly ConnectionBarViewModel _connectionBar;
	private readonly AppStatusBarViewModel _statusBar;
	private readonly FakeDisplayedSpectrumSource _spectrumSource = new();
	private readonly SpectrumViewModel _spectrum;
	private readonly MainWindowViewModel _viewModel;
	private readonly MainWindowView _window;

	public MainWindowViewTests()
	{
		var portProvider = new FakeSerialPortProvider();
		_serialLink = new SerialLink(portProvider, ImmediateScheduler.Instance, NullLogger<SerialLink>.Instance);
		_connectionBar = new ConnectionBarViewModel(
			portProvider,
			_serialLink,
			_messagePanel,
			AvaloniaScheduler.Instance,
			NullLogger<ConnectionBarViewModel>.Instance);
		_store = new UnstartedStore(_messagePanel);
		_statusBar = new AppStatusBarViewModel(
			_serialLink,
			_store.Store,
			_messagePanel,
			AvaloniaScheduler.Instance,
			NullLogger<AppStatusBarViewModel>.Instance);
		_spectrum = new SpectrumViewModel(
			_spectrumSource,
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
	}

	[AvaloniaFact]
	public void TheRealisedWindowShowsTheMenuTheContentPanesAndTheStatusBarWithTheMessagePanelHidden()
	{
		Realise();

		var menuBar = Named<AppMenuBar>(_window, "MenuBar");
		menuBar.IsEffectivelyVisible.Should().BeTrue();
		Named<Menu>(menuBar, "MainMenu").Items.OfType<MenuItem>().Select(item => item.Name)
			.Should().Equal("FileMenu", "ViewMenu");

		var content = Named<Grid>(_window, "ContentArea");
		var spectrumPane = Named<Border>(_window, "SpectrumPane");
		var signalPane = Named<Border>(_window, "SignalPane");
		new Control[] { spectrumPane, signalPane }.Select(pane => pane.Parent).Should().AllBeEquivalentTo(content);
		new Control[] { spectrumPane, signalPane }.Select(Grid.GetColumn).Should().Equal(0, 1);
		spectrumPane.Child.Should().BeOfType<SpectrumChartView>()
			.Which.DataContext.Should().BeSameAs(_spectrum.Chart);
		signalPane.Child.Should().BeOfType<SpectrumPanel>()
			.Which.DataContext.Should().BeSameAs(_spectrum);
		signalPane.Bounds.Width.Should().Be(SpectrumPanel.PaneWidth);
		_window.GetVisualDescendants().OfType<Control>()
			.Should().NotContain(control => control.Name == "ContentPlaceholder");

		Named<Border>(_window, "MessagePanel").IsVisible.Should().BeFalse("no message has arrived yet");

		var statusBar = Named<Border>(_window, "StatusBar");
		statusBar.IsEffectivelyVisible.Should().BeTrue();
		statusBar.Child.Should().BeOfType<AppStatusBar>();
	}

	[AvaloniaFact]
	public void TheStatusBarRealisesTheDotAndEverySegmentOfADisconnectedLink()
	{
		Realise();

		var bar = Named<Border>(_window, "StatusBar").Child.Should().BeOfType<AppStatusBar>().Which;
		var dot = Named<Ellipse>(bar, "LinkDot");
		dot.IsEffectivelyVisible.Should().BeTrue();
		dot.Classes.Where(name => name.StartsWith("tone-", StringComparison.Ordinal)).Should().Equal("tone-neutral");

		string[] segments =
		[
			"LinkText", "PortText", "Atp2000Text", "Atp8000Text", "LostSpectraText", "DroppedFramesText",
		];
		segments.Select(name => Named<TextBlock>(bar, name))
			.Should().OnlyContain(segment => segment.IsEffectivelyVisible && segment.Bounds.Width > 0);
		segments.Select(name => Named<TextBlock>(bar, name).Text).Should().Equal(
			Resources.StatusNotConnected,
			Resources.StatusNoValue,
			Resources.FormatStatusSpectrometerFormat("ATP-2000", Resources.StatusSpectrometerNoData),
			Resources.FormatStatusSpectrometerFormat("ATP-8000", Resources.StatusSpectrometerNoData),
			Resources.FormatStatusLostSpectraFormat(Resources.StatusNoValue),
			Resources.FormatStatusDroppedFramesFormat(0));
	}

	[AvaloniaFact]
	public void TheRowsStandTopToBottomAsMenuConnectionRowContentMessagePanelAndStatusBar()
	{
		Realise();

		new Control[]
			{
				Named<AppMenuBar>(_window, "MenuBar"),
				Named<Border>(_window, "ConnectionRow"),
				Named<Grid>(_window, "ContentArea"),
				Named<Border>(_window, "MessagePanel"),
				Named<Border>(_window, "StatusBar"),
			}
			.Select(Grid.GetRow)
			.Should().Equal(0, 1, 2, 3, 4);
	}

	[AvaloniaFact]
	public void TheConnectionRowShowsTheLabelTheEmptyPortListAndThreeButtons()
	{
		Realise();

		var connectionRow = Named<Border>(_window, "ConnectionRow");
		connectionRow.IsEffectivelyVisible.Should().BeTrue();
		var bar = connectionRow.Child.Should().BeOfType<ConnectionBar>().Which;

		Named<TextBlock>(bar, "PortLabel").Text.Should().Be(Resources.ConnectionPortLabel);
		var portList = Named<ComboBox>(bar, "PortList");
		portList.Width.Should().Be(240);
		portList.PlaceholderText.Should().Be(Resources.ConnectionPortPlaceholder);
		portList.IsEnabled.Should().BeTrue("the link is idle");

		Named<Button>(bar, "RefreshPorts").Content.Should().Be(Resources.ConnectionRefresh);
		Named<Button>(bar, "Connect").Content.Should().Be(Resources.ConnectionConnect);
		Named<Button>(bar, "Disconnect").Content.Should().Be(Resources.ConnectionDisconnect);
		Named<Button>(bar, "Connect").IsEffectivelyEnabled.Should().BeFalse("no port is selected");
		Named<Button>(bar, "Disconnect").IsEffectivelyEnabled.Should().BeFalse("the link is idle");
	}

	[AvaloniaFact]
	public async Task TheVerboseLoggingItemFollowsTheCommand()
	{
		Realise();
		var item = Named<MenuItem>(Named<AppMenuBar>(_window, "MenuBar"), "ViewVerboseLogging");
		item.IsChecked.Should().BeFalse();

		await _viewModel.ToggleVerboseLoggingCommand.Execute();
		Dispatcher.UIThread.RunJobs();

		item.IsChecked.Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task ExitClosesTheWindow()
	{
		var closed = false;
		_window.Closed += (_, _) => closed = true;
		Realise();

		await _viewModel.ExitCommand.Execute();
		Dispatcher.UIThread.RunJobs();

		closed.Should().BeTrue();
	}

	public void Dispose()
	{
		_window.Close();
		_viewModel.Dispose();
		_spectrum.Dispose();
		_spectrumSource.Dispose();
		_connectionBar.Dispose();
		_statusBar.Dispose();
		_serialLink.Dispose();
		_store.Dispose();
		_messagePanel.Dispose();
	}

	private static T Named<T>(Control root, string name)
		where T : Control
	{
		return root.FindControl<T>(name) ?? throw new InvalidOperationException($"No control named {name}.");
	}

	private void Realise()
	{
		_window.Show();
		Dispatcher.UIThread.RunJobs();
	}
}
