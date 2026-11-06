using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;

using AwesomeAssertions;

using SemiSpec.Core.Protocol;
using SemiSpec.UI.Localization;
using SemiSpec.UI.Spectrum;
using SemiSpec.UI.Trend;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.MainWindow;

[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowTextFitSidePaneTests : MainWindowTextFitTestBase
{
	private static readonly string[] _sidePaneLabels = [.. PaneLabels, "TimeWindowLabel"];
	private static readonly string[] _sidePaneControls = [.. PaneControls, "FollowLive"];
	private static readonly string[] _sidePaneHeaders = [.. PaneHeaders, "TrendHeader"];

	private const double NumberFieldWidth = 120;
	private const double FieldSlack = 24;

	[AvaloniaTheory]
	[InlineData("en")]
	[InlineData("ru")]
	public void TheSignalPaneHoldsItsWidestLabelValueAndControlOnOneLineWithSlack(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		var window = Realise();
		var pane = Named<Border>(window, "SignalPane");
		Texts(pane).Should().OnlyContain(
			text => text.TextWrapping == TextWrapping.NoWrap,
			"a label, a value, a checkbox or a button never takes a second line");

		var sample = Named<TextBlock>(pane, "DeviceLabel");
		PaneLabels.Concat(PaneValues).Select(name => Named<TextBlock>(pane, name)).Should().OnlyContain(
			text => text.FontFamily == sample.FontFamily && text.FontSize == sample.FontSize,
			"every label and value is measured in the first label's face");
		double Measure(string text)
		{
			return RenderedText.Width(text, sample.FontFamily, sample.FontSize);
		}

		var labelColumn = _sidePaneLabels.Max(name => Measure(Named<TextBlock>(pane, name).Text!));
		var valueColumn = WidestValues().Max(Measure);
		var controlWidths = _sidePaneControls.Select(name => RealWidth(Named<ContentControl>(pane, name))).ToList();
		var controlColumn = controlWidths.Max();
		var fieldColumn = new[] { NumberFieldWidth, valueColumn, controlColumn }.Max();
		var headerWidth = _sidePaneHeaders.Select(name => Named<TextBlock>(pane, name))
			.Max(header => RenderedText.Width(header.Text!, header.FontFamily, header.FontSize, header.FontWeight));
		var required = PaneBorder + PanePadding + labelColumn + LabelToField + fieldColumn + PanePadding + FieldSlack;

		var controls = string.Join(
			" / ",
			controlWidths.Select(width => width.ToString("0.#", CultureInfo.CurrentCulture)));
		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{culture}: \"{Resources.TrendTimeWindow}\" {Measure(Resources.TrendTimeWindow):0.#} px, "
			+ $"\"{Resources.ChartBackToLive}\" {RealWidth(Named<ContentControl>(pane, "BackToLive")):0.#} px, "
			+ $"\"{Resources.TrendFollowLive}\" {RealWidth(Named<ContentControl>(pane, "FollowLive")):0.#} px");
		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{culture}: labels {labelColumn:0.#} px, values {valueColumn:0.#} px, controls {controls} px, "
			+ $"field column {fieldColumn:0.#} px, headers {headerWidth:0.#} px; pane needs {required:0.#} px, "
			+ $"rounded {RoundUpTo8(required)} px, set {SpectrumPanel.PaneWidth} px");
		pane.Bounds.Width.Should().Be(SpectrumPanel.PaneWidth);
		SpectrumPanel.PaneWidth.Should().BeGreaterThanOrEqualTo(
			RoundUpTo8(required),
			"the pane is padding, label column, gap, field column, padding and slack");
		headerWidth.Should().BeLessThanOrEqualTo(
			labelColumn + LabelToField + fieldColumn,
			"a header spans both columns");
	}

	[AvaloniaTheory]
	[InlineData("en")]
	[InlineData("ru")]
	public void TheWidestPresetLabelFitsThePresetBoxOnOneLine(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		var presets = Named<ComboBox>(Realise(), "TimeWindowPreset");
		var selected = presets.SelectedItem as TrendPreset
			?? throw new InvalidOperationException("The preset box shows no preset.");
		var shown = Texts(presets).Single(text => text.Text == selected.Label);
		var presenter = shown.FindAncestorOfType<ContentPresenter>()
			?? throw new InvalidOperationException("The preset box shows no content presenter.");

		var slot = presenter.Bounds.Width - presenter.Padding.Left - presenter.Padding.Right;
		double Measure(TrendPreset preset)
		{
			return RenderedText.Width(preset.Label, shown.FontFamily, shown.FontSize);
		}

		var widestPreset = TrendPresets.All.MaxBy(Measure)!;
		var widest = Measure(widestPreset);

		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{culture}: widest preset \"{widestPreset.Label}\" {widest:0.#} px in a {slot:0.#} px slot of the "
			+ $"{presets.Bounds.Width} px box");
		presets.Bounds.Width.Should().Be(NumberFieldWidth, "a short code from the width table");
		shown.TextWrapping.Should().Be(TextWrapping.NoWrap);
		widest.Should().BeLessThanOrEqualTo(slot);
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

	/// <summary>A checkbox or a button in real faces: its realised chrome plus its text in its own face.</summary>
	private static double RealWidth(ContentControl control)
	{
		control.Measure(Size.Infinity);
		var text = Texts(control).Single();
		var chrome = control.DesiredSize.Width - text.DesiredSize.Width;

		return chrome + RenderedText.Width(text.Text!, text.FontFamily, text.FontSize, text.FontWeight);
	}
}
