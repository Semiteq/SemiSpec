using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;

using AwesomeAssertions;

using SemiSpec.UI.Localization;
using SemiSpec.UI.Spectrum;
using SemiSpec.UI.Trend;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.MainWindow;

[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowTextFitFlyoutTests : MainWindowTextFitTestBase
{
	[AvaloniaTheory]
	[InlineData("en")]
	[InlineData("ru")]
	public void EveryFlyoutMessageFitsItsReservedRowAndEveryNoteOneLine(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		var window = Realise();
		var face = Named<TextBlock>(Named<Border>(window, "SpectrumPane"), "StateLine");
		var spectrum = window.GetVisualDescendants().OfType<SpectrumChartView>().Single();
		var trend = window.GetVisualDescendants().OfType<TrendChartView>().Single();
		string[] rangeMessages = [Resources.AxisScaleBoundInvalid, Resources.AxisScaleMinimumBelowMaximum];
		var clock = Resources.TimeWindowClockPattern;
		var flyouts = new (Control View, string Key, string Message, string Note, string[] Messages)[]
		{
			(spectrum, "IntensityAxisFlyout", "IntensityMessage", "IntensityNote",
				[.. rangeMessages, Resources.AxisScaleLogMinimumPositive]),
			(spectrum, "WavelengthAxisFlyout", "WavelengthMessage", "WavelengthNote", rangeMessages),
			(trend, "IntensityAxisFlyout", "TrendAxisMessage", "TrendAxisNote", rangeMessages),
			(trend, "TimeWindowFlyout", "TimeWindowMessage", "TimeWindowNote",
			[
				Resources.FormatTimeWindowStartUnreadableFormat(clock),
				Resources.FormatTimeWindowEndUnreadableFormat(clock),
				Resources.FormatTimeWindowWidthUnreadableFormat(clock),
				Resources.TimeWindowEndNotAfterStart,
			]),
		};
		var lineHeight = RenderedText.LineHeight(face.FontFamily, face.FontSize);

		foreach (var (view, key, messageName, noteName, messages) in flyouts)
		{
			var content = (Grid)((Flyout)view.Resources[key]!).Content!;
			var message = Nested(content, messageName);
			var note = Nested(content, noteName);
			double Measure(string text)
			{
				return RenderedText.Width(text, face.FontFamily, face.FontSize);
			}

			var lines = messages.Max(text => WrappedLines(text, content.Width, Measure));
			var noteWidth = Measure(note.Text!);
			TestContext.Current.TestOutputHelper?.WriteLine(
				$"{culture} {messageName}: {lines} lines of {lineHeight:0.#} px in {message.Height} px, "
				+ $"note {noteWidth:0.#} px in {content.Width} px");
			message.TextWrapping.Should().Be(TextWrapping.Wrap);
			(lines * lineHeight).Should().BeLessThanOrEqualTo(message.Height, $"{messageName} shows whole");
			noteWidth.Should().BeLessThanOrEqualTo(content.Width, $"{noteName} takes one line");
		}
	}

	private static TextBlock Nested(Grid content, string name)
	{
		return content.GetLogicalDescendants().OfType<TextBlock>().Single(text => text.Name == name);
	}

	/// <summary>The lines a word-wrapping text block breaks the text into at the width.</summary>
	private static int WrappedLines(string text, double width, Func<string, double> measure)
	{
		var lines = 1;
		var line = string.Empty;
		foreach (var word in text.Split(' '))
		{
			var candidate = line.Length == 0 ? word : $"{line} {word}";
			if (line.Length > 0 && measure(candidate) > width)
			{
				lines++;
				line = word;
			}
			else
			{
				line = candidate;
			}
		}

		return lines;
	}
}
