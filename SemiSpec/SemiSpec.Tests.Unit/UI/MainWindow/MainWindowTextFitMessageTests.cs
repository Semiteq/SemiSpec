using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;

using SemiSpec.UI.Link;
using SemiSpec.UI.Messages;

using Xunit;

using MainWindowView = SemiSpec.UI.MainWindow.MainWindow;

namespace SemiSpec.Tests.Unit.UI.MainWindow;

[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowTextFitMessageTests : MainWindowTextFitTestBase
{
	[AvaloniaTheory]
	[InlineData("en")]
	[InlineData("ru")]
	public void TheOldFirmwareRemediesFitTwoLinesAtTheMinimumWidth(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		var window = Realise();
		window.Width = MainWindowView.MinimumWidth;
		Dispatcher.UIThread.RunJobs();
		window.Bounds.Width.Should().Be(MainWindowView.MinimumWidth);

		foreach (var error in new IError[] { new BoardNotRespondingError(PortName), new UnrecognisedDataError(PortName) })
		{
			MessagePanel.ReportFailure(error, NullLogger.Instance);
			var entry = NewestEntry(window);
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
			remedy.Text.Should().Be(ErrorMessageMapper.Map(error).Remedy);
			width.Should().BeLessThanOrEqualTo(2 * slot, "the remedy of {0} takes at most two lines", error.GetType().Name);
		}
	}
}
