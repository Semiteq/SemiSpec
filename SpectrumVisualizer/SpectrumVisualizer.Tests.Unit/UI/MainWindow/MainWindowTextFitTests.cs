using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Spectrum;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.UI.Spectrum.SpectrumSamples;

using MainWindowView = SpectrumVisualizer.UI.MainWindow.MainWindow;

namespace SpectrumVisualizer.Tests.Unit.UI.MainWindow;

/// <summary>
/// The connection row and the status bar texts fit on one line. The headless text stub gives no real width, so a
/// row's width is its realised chrome plus its texts measured in real faces (<see cref="RenderedText"/>).
/// </summary>
[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowTextFitTests : MainWindowLayoutTestBase
{
	// Windows numbers serial ports up to COM256.
	private const string WidestPortName = "COM256";

	// A day of ATP-8000 spectra at 1.3 per second is about 112 000, so six digits cover a day with every spectrum lost
	// or dropped.
	private const long WidestLostCount = 999_999;

	private const int MinimumLabelledTicks = 4;

	private const double NumberFieldWidth = 120;
	private const double FieldSlack = 24;

	// The widest G6 float: sign, six significant digits, a two-digit negative exponent.
	private const float WidestBoardValue = -1.23457E-38f;
	private const string WidestUpdateTime = "23:59:59.999";

	[AvaloniaTheory]
	[InlineData("en")]
	[InlineData("ru")]
	public void TheMinimumWidthHoldsTheConnectionRowAndTheLongestStatusTextsOnOneLine(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		var window = Realise();
		var connectionRow = Named<Border>(window, "ConnectionRow");
		var statusRow = Named<Border>(window, "StatusBar");

		var connectionTexts = Texts(connectionRow).Where(text => text.Name != "PlaceholderTextBlock").ToList();
		var statusTexts = Texts(statusRow).ToList();
		connectionTexts.Concat(statusTexts).Should().OnlyContain(
			text => text.TextWrapping == TextWrapping.NoWrap,
			"a label, a button or a status text never takes a second line");

		var family = statusTexts[0].FontFamily;
		var fontSize = statusTexts[0].FontSize;
		double Measure(string text)
		{
			return RenderedText.Width(text, family, fontSize);
		}

		var connectionWidth = ChromeWidth(connectionRow, connectionTexts)
			+ connectionTexts.Sum(text => Measure(text.Text!));
		var statusWidth = ChromeWidth(statusRow, statusTexts)
			+ LinkTexts().Max(Measure)
			+ Measure(WidestPortName)
			+ SpectrometerTexts("ATP-2000").Max(Measure)
			+ SpectrometerTexts("ATP-8000").Max(Measure)
			+ Measure(Resources.FormatStatusLostSpectraFormat(WidestLostCount.ToString(CultureInfo.CurrentCulture)))
			+ Measure(Resources.FormatStatusDroppedFramesFormat(WidestLostCount.ToString(CultureInfo.CurrentCulture)));

		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{culture}: faces {string.Join(", ", RenderedText.InstalledFamilies(family))} + Inter at {fontSize} px; "
			+ $"connection row {connectionWidth:0.#} px, status bar {statusWidth:0.#} px, "
			+ $"window minimum {MainWindowView.MinimumWidth} px");
		var faces = $"measured in {string.Join(", ", RenderedText.InstalledFamilies(family))} and Inter";
		window.MinWidth.Should().Be(MainWindowView.MinimumWidth);
		connectionWidth.Should().BeLessThanOrEqualTo(
			MainWindowView.MinimumWidth,
			$"the whole connection row shows, {faces}");
		statusWidth.Should().BeLessThanOrEqualTo(
			MainWindowView.MinimumWidth,
			$"every status segment shows whole, {faces}");
	}

	[AvaloniaTheory]
	[InlineData("en")]
	[InlineData("ru")]
	public void ThePortPlaceholderFitsThePortListOnOneLine(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		var window = Realise();
		var portList = Named<ComboBox>(window, "PortList");
		var placeholder = Texts(portList).Single(text => text.Name == "PlaceholderTextBlock");

		var slot = LayoutInformation.GetPreviousArrangeBounds(placeholder)!.Value.Width
			- placeholder.Margin.Left
			- placeholder.Margin.Right;
		var width = RenderedText.Width(
			Resources.ConnectionPortPlaceholder,
			placeholder.FontFamily,
			placeholder.FontSize);

		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{culture}: placeholder {width:0.#} px in a {slot:0.#} px slot");
		portList.Bounds.Width.Should().Be(240, "a choice field is 240 px wide");
		placeholder.Text.Should().Be(Resources.ConnectionPortPlaceholder);
		placeholder.TextWrapping.Should().Be(TextWrapping.NoWrap);
		width.Should().BeLessThanOrEqualTo(slot);
	}

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

		var labelColumn = PaneLabels.Max(name => Measure(Named<TextBlock>(pane, name).Text!));
		var valueColumn = WidestValues().Max(Measure);
		var controlWidths = PaneControls.Select(name => RealWidth(Named<ContentControl>(pane, name))).ToList();
		var controlColumn = controlWidths.Max();
		var fieldColumn = new[] { NumberFieldWidth, valueColumn, controlColumn }.Max();
		var headerWidth = PaneHeaders.Select(name => Named<TextBlock>(pane, name))
			.Max(header => RenderedText.Width(header.Text!, header.FontFamily, header.FontSize, header.FontWeight));
		var required = PaneBorder + PanePadding + labelColumn + LabelToField + fieldColumn + PanePadding + FieldSlack;

		var controls = string.Join(
			" / ",
			controlWidths.Select(width => width.ToString("0.#", CultureInfo.CurrentCulture)));
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

	[AvaloniaFact]
	public void AnAtp8000SpectrumOnTheChartFloorKeepsFourLabelledWavelengthTicks()
	{
		using var chart = new SpectrumChartViewModel();
		chart.Show(Atp8000Peak);

		chart.Plot.RenderInMemory(ChartFloorWidth, 300);

		var labelled = chart.Plot.Axes.Bottom.TickGenerator.Ticks
			.Where(tick => tick.IsMajor && !string.IsNullOrWhiteSpace(tick.Label))
			.Select(tick => tick.Label)
			.ToList();
		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{ChartFloorWidth} px: {labelled.Count} labelled ticks ({string.Join(", ", labelled)})");
		labelled.Should().HaveCountGreaterThanOrEqualTo(MinimumLabelledTicks);
	}

	[AvaloniaFact]
	public void TheMinimumWidthHoldsTheSignalPaneAndTheChartFloor()
	{
		var window = Realise();

		window.Width = 400;
		Dispatcher.UIThread.RunJobs();

		MainWindowView.MinimumWidth.Should()
			.BeGreaterThanOrEqualTo(RoundUpTo8(SpectrumPanel.PaneWidth + ChartFloorWidth));
		Named<Border>(window, "SpectrumPane").Bounds.Width.Should().BeGreaterThanOrEqualTo(ChartFloorWidth);
		Named<Border>(window, "SignalPane").Bounds.Width.Should().Be(SpectrumPanel.PaneWidth);
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

	private static double RoundUpTo8(double width)
	{
		return Math.Ceiling(width / 8) * 8;
	}

	private static IEnumerable<string> LinkTexts()
	{
		return
		[
			Resources.StatusNotConnected,
			Resources.StatusConnecting,
			Resources.StatusPortUnavailable,
			Resources.StatusWaiting,
			Resources.StatusReceiving,
			Resources.StatusNotResponding,
			Resources.StatusOtherDevice,
		];
	}

	private static IEnumerable<string> SpectrometerTexts(string device)
	{
		return new[]
			{
				Resources.StatusSpectrometerConnected,
				Resources.StatusSpectrometerNotConnected,
				Resources.StatusSpectrometerFailed,
				Resources.StatusSpectrometerNoData,
			}
			.Select(state => Resources.FormatStatusSpectrometerFormat(device, state));
	}

	/// <summary>
	/// The row's natural width less its texts' stub widths: padding, margins, spacing, borders, the fixed controls.
	/// </summary>
	private static double ChromeWidth(Border row, IReadOnlyCollection<TextBlock> texts)
	{
		// Avalonia clamps DesiredSize to the space offered, so only an unbounded measure gives the whole chrome. The
		// stub texts may overflow the opening width here; the real widths are judged against the minimum width.
		row.Measure(Size.Infinity);
		var naturalWidth = row.DesiredSize.Width;
		var textWidths = texts.Sum(text => text.DesiredSize.Width - text.Margin.Left - text.Margin.Right);

		return naturalWidth - textWidths;
	}
}
