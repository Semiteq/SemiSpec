using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using SkiaSharp;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Spectrum;
using SpectrumVisualizer.UI.Trend;

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

	// The widest key a frame can carry: ATP-8000's last wavelength and the widest three-digit delta.
	private const ushort WidestWavelength = 2461;
	private const byte WidestDelta = 255;

	private static readonly string[] _sidePaneLabels = [.. PaneLabels, "TimeWindowLabel"];
	private static readonly string[] _sidePaneControls = [.. PaneControls, "FollowLive"];
	private static readonly string[] _sidePaneHeaders = [.. PaneHeaders, "TrendHeader"];

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
			+ $"window minimum {MainWindowView.MinimumWidth} px, opening {MainWindowView.OpeningWidth} px");
		var faces = $"measured in {string.Join(", ", RenderedText.InstalledFamilies(family))} and Inter";
		window.MinWidth.Should().Be(MainWindowView.MinimumWidth);
		window.Bounds.Width.Should().Be(MainWindowView.OpeningWidth);
		MainWindowView.MinimumWidth.Should().BeLessThanOrEqualTo(
			MainWindowView.OpeningWidth,
			"both rows show whole at the opening width, since they fit the minimum");
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

	[AvaloniaTheory]
	[InlineData("en")]
	[InlineData("ru")]
	public void TheTrendYLabelAtTheWidestKeyFitsThePlotAtItsFloor(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		var window = Realise();
		window.Height = MainWindowView.MinimumHeight;
		ReportFailures(5);
		window.UpdateLayout();
		var trendPane = Named<Border>(window, "TrendPane");
		var plot = Trend.Trend.Chart.Plot;
		var label = plot.Axes.Left.Label;

		var plotHeight = trendPane.Bounds.Height - trendPane.BorderThickness.Top;
		plot.RenderInMemory((int)trendPane.Bounds.Width, (int)plotHeight);
		Dispatcher.UIThread.RunJobs();

		var dataRect = plot.RenderManager.LastRender.Layout.DataRect;
		var text = Resources.FormatTrendAxisIntensityFormat(WidestWavelength, WidestDelta);
		var length = ScottPlotTextWidth(text, label.FontName, label.FontSize, label.Bold);
		var oneLine = ScottPlotTextWidth(text.Replace('\n', ' '), label.FontName, label.FontSize, label.Bold);
		var room = dataRect.Height + (2 * dataRect.Top);
		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{culture}: \"{text.Replace('\n', '|')}\" {length:0.#} px in {label.FontName} {label.FontSize} px; "
			+ $"one line would take {oneLine:0.#} px; "
			+ $"trend row {trendPane.Bounds.Height} px, data area {dataRect.Height:0.#} px from {dataRect.Top:0.#}, "
			+ $"room {room:0.#} px");
		trendPane.Bounds.Height
			.Should().Be(MainWindowView.TrendRowMinHeight, "the message panel holds the row at its floor");
		length.Should().BeLessThanOrEqualTo(room, "the label centres on the data area and shows whole inside the plot");
	}

	[AvaloniaTheory]
	[InlineData("en")]
	[InlineData("ru")]
	public void TheHistoricalLineAndTheWidestReadoutFitTheChartsAtTheMinimumWidth(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		var window = Realise();
		window.Width = MainWindowView.MinimumWidth;
		Dispatcher.UIThread.RunJobs();
		var historical = Named<TextBlock>(Named<Border>(window, "SpectrumPane"), "HistoricalLine");
		var readoutBox = Named<Border>(Named<Border>(window, "TrendPane"), "ReadoutBox");
		var readoutText = Named<TextBlock>(readoutBox, "ReadoutText");
		var chartWidth = Named<Border>(window, "TrendPane").Bounds.Width;

		var historicalWidth = RenderedText.Width(
			Resources.FormatSpectrumHistoricalFormat(WidestUpdateTime[..8]),
			historical.FontFamily,
			historical.FontSize);
		var widestReadout = WidestReadoutLines()
			.Max(line => RenderedText.Width(line, readoutText.FontFamily, readoutText.FontSize));
		var readoutWidth = widestReadout + readoutBox.Padding.Left + readoutBox.Padding.Right
			+ readoutBox.BorderThickness.Left + readoutBox.BorderThickness.Right;

		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{culture}: historical line {historicalWidth:0.#} px, widest readout box {readoutWidth:0.#} px, "
			+ $"charts {chartWidth} px wide at the minimum");
		historical.TextWrapping.Should().Be(TextWrapping.NoWrap);
		readoutText.TextWrapping.Should().Be(TextWrapping.NoWrap);
		double.IsNaN(readoutBox.Width).Should().BeTrue("the readout box sizes to its content");
		historicalWidth.Should().BeLessThanOrEqualTo(chartWidth);
		readoutWidth.Should().BeLessThanOrEqualTo(chartWidth / 2, "the box fits on either side of a central hover line");
	}

	[AvaloniaFact]
	public void AtTheMinimumWidthTenMinutesOfTrendKeepFourLabelledTimeTicks()
	{
		Realise();
		Trend.AddSpectraEndingNow(TimeSpan.FromMinutes(10));
		Trend.Sample();
		Dispatcher.UIThread.RunJobs();
		var plot = Trend.Trend.Chart.Plot;

		List<string> LabelledTicksAt(double chartWidth)
		{
			plot.RenderInMemory((int)chartWidth, 300);
			Dispatcher.UIThread.RunJobs();
			var labels = plot.Axes.Bottom.TickGenerator.Ticks
				.Where(tick => tick.IsMajor && !string.IsNullOrWhiteSpace(tick.Label))
				.Select(tick => tick.Label.Replace('\n', ' '))
				.ToList();
			TestContext.Current.TestOutputHelper?.WriteLine(
				$"{chartWidth} px: {labels.Count} labelled ticks ({string.Join(", ", labels)})");
			return labels;
		}

		// The window never gives the charts less than its minimum width less the side pane; the count at the spectrum's
		// 400 px floor is printed for comparison only.
		LabelledTicksAt(ChartFloorWidth);
		var atMinimum = LabelledTicksAt(MainWindowView.MinimumWidth - SpectrumPanel.PaneWidth);
		(plot.Axes.Bottom.Max - plot.Axes.Bottom.Min).Should().BeApproximately(
			TimeSpan.FromMinutes(10).TotalDays,
			TimeSpan.FromSeconds(2).TotalDays,
			"the trend follows a 10 min window");
		atMinimum.Should().HaveCountGreaterThanOrEqualTo(MinimumLabelledTicks);
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

	/// <summary>The width ScottPlot gives the text's longest line in the axis label's face.</summary>
	private static double ScottPlotTextWidth(string text, string fontName, float fontSize, bool isBold)
	{
		using var typeface = SKFontManager.Default.MatchFamily(fontName, isBold ? SKFontStyle.Bold : SKFontStyle.Normal);
		using var font = new SKFont(typeface, fontSize);

		return text.Split('\n').Max(line => font.MeasureText(line));
	}

	private static IEnumerable<string> WidestReadoutLines()
	{
		var widestValue = WidestBoardValue.ToString("G6", CultureInfo.CurrentCulture);
		var widestCount = WidestLostCount.ToString(CultureInfo.CurrentCulture);

		return
		[
			WidestUpdateTime[..8],
			Resources.FormatTrendReadoutRangeFormat(widestValue, widestValue),
			Resources.FormatTrendReadoutCountFormat(widestCount),
			Resources.TrendReadoutNoData,
			Resources.FormatTrendReadoutLostFormat(widestCount),
			Resources.TrendReadoutNoSpectra,
			Resources.FormatTrendReadoutUncoveredFormat(ushort.MaxValue.ToString(CultureInfo.CurrentCulture)),
			Resources.FormatTrendReadoutFailedFormat(Spectrometer.Atp2000.DisplayName),
			Resources.FormatTrendReadoutFailedFormat(Spectrometer.Atp8000.DisplayName),
		];
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

	private void ReportFailures(int count)
	{
		for (var entry = 0; entry < count; entry++)
		{
			MessagePanel.Report(new MessageView($"title {entry}", "detail", "remedy", MessageSeverity.Error));
		}

		Dispatcher.UIThread.RunJobs();
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
		// Avalonia clamps DesiredSize to the space offered, so only an unbounded measure gives the whole chrome.
		row.Measure(Size.Infinity);
		var naturalWidth = row.DesiredSize.Width;
		var textWidths = texts.Sum(text => text.DesiredSize.Width - text.Margin.Left - text.Margin.Right);

		return naturalWidth - textWidths;
	}
}
