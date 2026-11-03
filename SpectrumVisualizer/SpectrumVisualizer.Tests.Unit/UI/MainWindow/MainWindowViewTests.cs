using System.Globalization;
using System.Reactive.Linq;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;

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
	private readonly MainWindowViewModel _viewModel;
	private readonly MainWindowView _window;

	public MainWindowViewTests()
	{
		_viewModel = new MainWindowViewModel(_messagePanel, new AppStatusBarViewModel(), new LogLevelControl());
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
		var statusBarContent = statusBar.Child.Should().BeOfType<AppStatusBar>().Which;
		Named<TextBlock>(statusBarContent, "ConnectionText").Text.Should().Be(Resources.StatusNotConnected);
	}

	[AvaloniaFact]
	public void TheRowsStandTopToBottomAsMenuContentMessagePanelAndStatusBar()
	{
		Realise();

		new Control[]
			{
				Named<AppMenuBar>(_window, "MenuBar"),
				Named<Border>(_window, "ContentArea"),
				Named<Border>(_window, "MessagePanel"),
				Named<Border>(_window, "StatusBar"),
			}
			.Select(Grid.GetRow)
			.Should().Equal(0, 1, 2, 3);
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

		_messagePanel.Report(new MessageView("title", "detail", string.Empty));
		var entry = RealisedEntry(0);

		EntryText(entry, "EntryRepeat").IsVisible.Should().BeFalse();
		EntryText(entry, "EntryRemedy").IsVisible.Should().BeFalse();
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
