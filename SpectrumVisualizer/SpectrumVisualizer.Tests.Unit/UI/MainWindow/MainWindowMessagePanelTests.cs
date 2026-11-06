using System.Globalization;

using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;

using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Messages;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.MainWindow;

/// <summary>The message panel row in the shown window: opening on a report, the entry rows and Clear.</summary>
[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowMessagePanelTests : MainWindowLayoutTestBase
{
	[AvaloniaFact]
	public void AReportedFailureOpensTheMessagePanelRow()
	{
		var window = Realise();

		MessagePanel.ReportFailure(new Error("port busy"), NullLogger.Instance);
		Dispatcher.UIThread.RunJobs();

		Named<Border>(window, "MessagePanel").IsVisible.Should().BeTrue();
	}

	[AvaloniaFact]
	public void ARepeatedFailureShowsOneEntryWithItsTimeTitleRepeatCountAndRemedy()
	{
		var window = Realise();
		var error = new Error("port busy");

		MessagePanel.ReportFailure(error, NullLogger.Instance);
		MessagePanel.ReportFailure(error, NullLogger.Instance);
		var entry = RealisedEntry(window, 0);

		var view = ErrorMessageMapper.Map(error);
		EntryText(entry, "EntryTitle").Text.Should().Be(view.Title);
		EntryText(entry, "EntryDetail").Text.Should().Be(view.Detail);
		EntryText(entry, "EntryRemedy").Text.Should().Be(view.Remedy);
		EntryText(entry, "EntryRemedy").IsVisible.Should().BeTrue();
		EntryText(entry, "EntryTime").Text.Should().Be(
			string.Format(CultureInfo.CurrentCulture, "{0:HH:mm:ss}", MessagePanel.Entries[0].LastSeen));

		var repeat = EntryText(entry, "EntryRepeat");
		repeat.IsVisible.Should().BeTrue();
		repeat.Text.Should().Be(string.Format(CultureInfo.CurrentCulture, Resources.MessagePanelRepeatFormat, 2));
	}

	[AvaloniaFact]
	public void AFirstFailureHidesTheRepeatLineAndAnEmptyRemedyTakesNoLine()
	{
		var window = Realise();

		MessagePanel.Report(new MessageView("title", "detail", string.Empty, MessageSeverity.Error));
		var entry = RealisedEntry(window, 0);

		EntryText(entry, "EntryRepeat").IsVisible.Should().BeFalse();
		EntryText(entry, "EntryRemedy").IsVisible.Should().BeFalse();
	}

	[AvaloniaTheory]
	[InlineData(MessageSeverity.Info, "severity-info")]
	[InlineData(MessageSeverity.Warning, "severity-warning")]
	[InlineData(MessageSeverity.Error, "severity-error")]
	public void TheEntryDotCarriesTheClassOfItsSeverityOnly(MessageSeverity severity, string expected)
	{
		var window = Realise();

		MessagePanel.Report(new MessageView("title", "detail", "remedy", severity));
		var dot = RealisedEntry(window, 0)
			.GetVisualDescendants()
			.OfType<Ellipse>()
			.Single(item => item.Name == "EntrySeverity");

		dot.Classes.Where(name => name.StartsWith("severity-", StringComparison.Ordinal))
			.Should().Equal(expected);
	}

	[AvaloniaFact]
	public void ClearEmptiesTheListAndHidesTheMessagePanelRow()
	{
		var window = Realise();
		MessagePanel.ReportFailure(new Error("port busy"), NullLogger.Instance);
		Dispatcher.UIThread.RunJobs();

		Named<Button>(PanelView(window), "ClearMessages").Command!.Execute(null);
		Dispatcher.UIThread.RunJobs();

		MessagePanel.Entries.Should().BeEmpty();
		Named<Border>(window, "MessagePanel").IsVisible.Should().BeFalse();
	}
}
