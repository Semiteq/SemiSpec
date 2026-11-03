using System.Reactive.Linq;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

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

/// <summary>The realised window: which rows stand and what each of them shows.</summary>
[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowViewTests : IDisposable
{
	private readonly MessagePanelViewModel _messagePanel = new();
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
	public void AReportedFailureOpensTheMessagePanelRow()
	{
		Realise();

		_messagePanel.ReportFailure(Result.Fail("port busy"), NullLogger.Instance);
		Dispatcher.UIThread.RunJobs();

		Named<Border>(_window, "MessagePanel").IsVisible.Should().BeTrue();
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

	private void Realise()
	{
		_window.Show();
		Dispatcher.UIThread.RunJobs();
	}
}
