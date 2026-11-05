using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;

using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Messages;

using Xunit;

using MainWindowView = SpectrumVisualizer.UI.MainWindow.MainWindow;

namespace SpectrumVisualizer.Tests.Unit.UI.MainWindow;

/// <summary>
/// The old-firmware remedies take one line at the minimum width, so their entry stays within the panel's three-line cap.
/// </summary>
[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowTextFitMessageTests : MainWindowTextFitTestBase
{
	[AvaloniaTheory]
	[InlineData("en")]
	[InlineData("ru")]
	public void TheOldFirmwareRemediesFitOneLineAtTheMinimumWidth(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		var window = Realise();
		window.Width = MainWindowView.MinimumWidth;
		Dispatcher.UIThread.RunJobs();

		foreach (var error in new IError[] { new BoardNotRespondingError(PortName), new UnrecognisedDataError(PortName) })
		{
			MessagePanel.ReportFailure(error, NullLogger.Instance);
			var entry = RealisedEntry(window, 0);
			var remedy = EntryText(entry, "EntryRemedy");
			var time = EntryText(entry, "EntryTime");

			// The headless stub sizes the time cell, so the slot trades the stub's time width for the real one.
			var slot = remedy.Bounds.Width
				+ time.DesiredSize.Width
				- time.Margin.Left
				- time.Margin.Right
				- RenderedText.Width(time.Text!, time.FontFamily, time.FontSize);
			var width = RenderedText.Width(remedy.Text!, remedy.FontFamily, remedy.FontSize);

			TestContext.Current.TestOutputHelper?.WriteLine(
				$"{culture}: {error.GetType().Name} remedy {width:0.#} px in a {slot:0.#} px slot, "
				+ $"window {window.Bounds.Width} px");
			window.Bounds.Width.Should().Be(MainWindowView.MinimumWidth);
			remedy.Text.Should().Be(ErrorMessageMapper.Map(error).Remedy);
			width.Should().BeLessThanOrEqualTo(slot, "the remedy of {0} takes one line", error.GetType().Name);
		}
	}
}
