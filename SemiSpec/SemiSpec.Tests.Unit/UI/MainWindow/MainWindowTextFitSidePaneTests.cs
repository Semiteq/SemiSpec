using System.Globalization;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;

using AwesomeAssertions;

using SemiSpec.Core.Protocol;
using SemiSpec.UI.Localization;
using SemiSpec.UI.Spectrum;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.MainWindow;

[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowTextFitSidePaneTests : MainWindowTextFitTestBase
{
	private const double NumberFieldWidth = 120;
	private const double FieldSlack = 24;

	[AvaloniaTheory]
	[InlineData("en")]
	[InlineData("ru")]
	public void TheReadoutPaneHoldsItsWidestLabelAndValueOnOneLineWithSlack(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		var window = Realise();
		var pane = Named<Border>(window, "SignalPane");
		Texts(pane).Should().OnlyContain(
			text => text.TextWrapping == TextWrapping.NoWrap,
			"a label or a value never takes a second line");

		var sample = Named<TextBlock>(pane, "DeviceLabel");
		PaneLabels.Concat(PaneValues).Select(name => Named<TextBlock>(pane, name)).Should().OnlyContain(
			text => text.FontFamily == sample.FontFamily && text.FontSize == sample.FontSize,
			"every label and value is measured in the first label's face");
		double Measure(string text)
		{
			return RenderedText.Width(text, sample.FontFamily, sample.FontSize);
		}

		var labelColumn = PaneLabels.Max(name => Measure(Named<TextBlock>(pane, name).Text!));
		var valueColumn = WidestValues().Max(Measure);
		var fieldColumn = Math.Max(NumberFieldWidth, valueColumn);
		var required = PaneBorder + PanePadding + labelColumn + LabelToField + fieldColumn + PanePadding + FieldSlack;

		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{culture}: labels {labelColumn:0.#} px, values {valueColumn:0.#} px, field column {fieldColumn:0.#} px; "
			+ $"pane needs {required:0.#} px, rounded {RoundUpTo8(required)} px, set {SpectrumPanel.PaneWidth} px");
		pane.Bounds.Width.Should().Be(SpectrumPanel.PaneWidth);
		SpectrumPanel.PaneWidth.Should().BeGreaterThanOrEqualTo(
			RoundUpTo8(required),
			"the pane is padding, label column, gap, field column, padding and slack");
	}

	private static IEnumerable<string> WidestValues()
	{
		return
		[
			Resources.StatusNoValue,
			Spectrometer.Atp2000.DisplayName,
			Spectrometer.Atp8000.DisplayName,
			Resources.FormatSignalWavelengthFormat(ushort.MaxValue),
			Resources.FormatSignalWindowFormat(byte.MaxValue),
			WidestBoardValue.ToString("G6", CultureInfo.CurrentCulture),
			WidestUpdateTime,
		];
	}
}
