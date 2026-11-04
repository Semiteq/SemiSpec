using System.Globalization;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;

using ReactiveUI.Avalonia;

using SpectrumVisualizer.Tests.Unit.UI.Link;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Logging;
using SpectrumVisualizer.UI.MainWindow;
using SpectrumVisualizer.UI.Messages;

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
	private readonly ConnectionBarViewModel _connectionBar;
	private readonly AppStatusBarViewModel _statusBar;
	private readonly MainWindowViewModel _viewModel;
	private readonly MainWindowView _window;

	public MainWindowViewTests()
	{
		var portProvider = new FakeSerialPortProvider();
		_serialLink = new SerialLink(portProvider, ImmediateScheduler.Instance, NullLogger<SerialLink>.Instance);
		_connectionBar = new ConnectionBarViewModel(portProvider, _serialLink, AvaloniaScheduler.Instance);
		_statusBar = new AppStatusBarViewModel(
			_serialLink,
			_messagePanel,
			AvaloniaScheduler.Instance,
			NullLogger<AppStatusBarViewModel>.Instance);
		_viewModel = new MainWindowViewModel(
			_messagePanel,
			_connectionBar,
			_statusBar,
			new LogLevelControl(),
			NullLogger<MainWindowViewModel>.Instance);
		_window = new MainWindowView { DataContext = _viewModel };
	}

	[AvaloniaFact]
	public void TheRealisedWindowShowsTheMenuThePlaceholderAndTheStatusBarWithTheMessagePanelHidden()
	{
		Realise();

		var menuBar = Named<AppMenuBar>(_window, "MenuBar");
		menuBar.IsEffectivelyVisible.Should().BeTrue();
		Named<Menu>(menuBar, "MainMenu").Items.OfType<MenuItem>().Select(item => item.Name)
			.Should().Equal("FileMenu", "ViewMenu");

		var placeholder = Named<TextBlock>(_window, "ContentPlaceholder");
		placeholder.IsEffectivelyVisible.Should().BeTrue();
		placeholder.Text.Should().Be(Resources.ContentPlaceholder);

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

		string[] segments = ["LinkText", "PortText", "Atp2000Text", "Atp8000Text", "LostSpectraText"];
		segments.Select(name => Named<TextBlock>(bar, name))
			.Should().OnlyContain(segment => segment.IsEffectivelyVisible && segment.Bounds.Width > 0);
		segments.Select(name => Named<TextBlock>(bar, name).Text).Should().Equal(
			Resources.StatusNotConnected,
			Resources.StatusNoValue,
			Resources.FormatStatusSpectrometerFormat("ATP-2000", Resources.StatusSpectrometerNoData),
			Resources.FormatStatusSpectrometerFormat("ATP-8000", Resources.StatusSpectrometerNoData),
			Resources.FormatStatusLostSpectraFormat(Resources.StatusNoValue));
	}

	[AvaloniaFact]
	public void TheRowsStandTopToBottomAsMenuConnectionRowContentMessagePanelAndStatusBar()
	{
		Realise();

		new Control[]
			{
				Named<AppMenuBar>(_window, "MenuBar"),
				Named<Border>(_window, "ConnectionRow"),
				Named<Border>(_window, "ContentArea"),
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
	public void AReportedFailureOpensTheMessagePanelRow()
	{
		Realise();

		_messagePanel.ReportFailure(new Error("port busy"), NullLogger.Instance);
		Dispatcher.UIThread.RunJobs();

		Named<Border>(_window, "MessagePanel").IsVisible.Should().BeTrue();
	}

	[AvaloniaFact]
	public void ARepeatedFailureShowsOneEntryWithItsTimeTitleRepeatCountAndRemedy()
	{
		Realise();
		var error = new Error("port busy");

		_messagePanel.ReportFailure(error, NullLogger.Instance);
		_messagePanel.ReportFailure(error, NullLogger.Instance);
		var entry = RealisedEntry(0);

		var view = ErrorMessageMapper.Map(error);
		EntryText(entry, "EntryTitle").Text.Should().Be(view.Title);
		EntryText(entry, "EntryDetail").Text.Should().Be(view.Detail);
		EntryText(entry, "EntryRemedy").Text.Should().Be(view.Remedy);
		EntryText(entry, "EntryRemedy").IsVisible.Should().BeTrue();
		EntryText(entry, "EntryTime").Text.Should().Be(
			string.Format(CultureInfo.CurrentCulture, "{0:HH:mm:ss}", _messagePanel.Entries[0].LastSeen));

		var repeat = EntryText(entry, "EntryRepeat");
		repeat.IsVisible.Should().BeTrue();
		repeat.Text.Should().Be(string.Format(CultureInfo.CurrentCulture, Resources.MessagePanelRepeatFormat, 2));
	}

	[AvaloniaFact]
	public void AFirstFailureHidesTheRepeatLineAndAnEmptyRemedyTakesNoLine()
	{
		Realise();

		_messagePanel.Report(new MessageView("title", "detail", string.Empty, MessageSeverity.Error));
		var entry = RealisedEntry(0);

		EntryText(entry, "EntryRepeat").IsVisible.Should().BeFalse();
		EntryText(entry, "EntryRemedy").IsVisible.Should().BeFalse();
	}

	[AvaloniaTheory]
	[InlineData(MessageSeverity.Info, "severity-info")]
	[InlineData(MessageSeverity.Warning, "severity-warning")]
	[InlineData(MessageSeverity.Error, "severity-error")]
	public void TheEntryDotCarriesTheClassOfItsSeverityOnly(MessageSeverity severity, string expected)
	{
		Realise();

		_messagePanel.Report(new MessageView("title", "detail", "remedy", severity));
		var dot = RealisedEntry(0)
			.GetVisualDescendants()
			.OfType<Ellipse>()
			.Single(item => item.Name == "EntrySeverity");

		dot.Classes.Where(name => name.StartsWith("severity-", StringComparison.Ordinal))
			.Should().Equal(expected);
	}

	[AvaloniaFact]
	public void ClearEmptiesTheListAndHidesTheMessagePanelRow()
	{
		Realise();
		_messagePanel.ReportFailure(new Error("port busy"), NullLogger.Instance);
		Dispatcher.UIThread.RunJobs();

		Named<Button>(PanelView(), "ClearMessages").Command!.Execute(null);
		Dispatcher.UIThread.RunJobs();

		_messagePanel.Entries.Should().BeEmpty();
		Named<Border>(_window, "MessagePanel").IsVisible.Should().BeFalse();
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
		_connectionBar.Dispose();
		_statusBar.Dispose();
		_serialLink.Dispose();
		_messagePanel.Dispose();
	}

	private static T Named<T>(Control root, string name)
		where T : Control
	{
		return root.FindControl<T>(name) ?? throw new InvalidOperationException($"No control named {name}.");
	}

	private static TextBlock EntryText(Control entry, string name)
	{
		return entry.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == name);
	}

	private MessagePanelView PanelView()
	{
		return _window.GetVisualDescendants().OfType<MessagePanelView>().Single();
	}

	private void Realise()
	{
		_window.Show();
		Dispatcher.UIThread.RunJobs();
	}

	/// <summary>The realised row of an entry; the virtualizing panel creates it only on a layout pass.</summary>
	private Control RealisedEntry(int index)
	{
		Dispatcher.UIThread.RunJobs();
		_window.UpdateLayout();

		return Named<ItemsControl>(PanelView(), "MessageEntries").ContainerFromIndex(index)
			?? throw new InvalidOperationException($"No realised entry at {index}.");
	}
}
