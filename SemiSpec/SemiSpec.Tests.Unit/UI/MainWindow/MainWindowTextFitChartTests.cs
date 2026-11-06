using System.Globalization;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;

using AwesomeAssertions;

using SemiSpec.Core.Protocol;
using SemiSpec.UI.Localization;
using SemiSpec.UI.Messages;
using SemiSpec.UI.Spectrum;
using SemiSpec.UI.Trend;

using SkiaSharp;

using Xunit;

using static SemiSpec.Tests.Unit.UI.Spectrum.SpectrumSamples;

using MainWindowView = SemiSpec.UI.MainWindow.MainWindow;

namespace SemiSpec.Tests.Unit.UI.MainWindow;

[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowTextFitChartTests : MainWindowTextFitTestBase
{
	private const int MinimumLabelledTicks = 4;

	// The widest key a frame can carry: ATP-8000's last wavelength and the widest three-digit delta.
	private const ushort WidestWavelength = 2461;
	private const byte WidestDelta = 255;

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
	public void TheLongestStateLinesFitTheDataAreasAtTheMinimumWidth(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		var window = Realise();
		window.Width = MainWindowView.MinimumWidth;
		Dispatcher.UIThread.RunJobs();
		var spectrumPane = Named<Border>(window, "SpectrumPane");
		var trendPane = Named<Border>(window, "TrendPane");
		var stateLine = Named<TextBlock>(spectrumPane, "StateLine");
		var spectrumData = DataWidth(Spectrum.Chart.Plot, spectrumPane);
		var trendData = DataWidth(Trend.Trend.Chart.Plot, trendPane);

		var spectrumWidth = LongestSpectrumStateLines()
			.Max(line => RenderedText.Width(line, stateLine.FontFamily, stateLine.FontSize));
		var trendWidth = LongestTrendStateLines()
			.Max(line => RenderedText.Width(line, stateLine.FontFamily, stateLine.FontSize));

		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{culture}: spectrum state line {spectrumWidth:0.#} px over {spectrumData:0.#} px of data, "
			+ $"trend state line {trendWidth:0.#} px over {trendData:0.#} px");
		stateLine.TextWrapping.Should().Be(TextWrapping.NoWrap);
		spectrumWidth.Should().BeLessThanOrEqualTo(spectrumData, "the whole spectrum state shows over its data area");
		trendWidth.Should().BeLessThanOrEqualTo(trendData, "the whole trend state shows over its data area");
	}

	[AvaloniaTheory]
	[InlineData("en")]
	[InlineData("ru")]
	public void TheWidestReadoutFitsEitherSideOfTheTrendAtTheMinimumWidth(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		var window = Realise();
		window.Width = MainWindowView.MinimumWidth;
		Dispatcher.UIThread.RunJobs();
		var readoutBox = Named<Border>(Named<Border>(window, "TrendPane"), "ReadoutBox");
		var readoutText = Named<TextBlock>(readoutBox, "ReadoutText");
		var chartWidth = Named<Border>(window, "TrendPane").Bounds.Width;

		var widestReadout = WidestReadoutLines()
			.Max(line => RenderedText.Width(line, readoutText.FontFamily, readoutText.FontSize));
		var readoutWidth = widestReadout + readoutBox.Padding.Left + readoutBox.Padding.Right
			+ readoutBox.BorderThickness.Left + readoutBox.BorderThickness.Right;

		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{culture}: widest readout box {readoutWidth:0.#} px, trend {chartWidth} px wide at the minimum");
		readoutText.TextWrapping.Should().Be(TextWrapping.NoWrap);
		double.IsNaN(readoutBox.Width).Should().BeTrue("the readout box sizes to its content");
		readoutWidth.Should()
			.BeLessThanOrEqualTo(chartWidth / 2, "the box fits on either side of a central hover line");
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

	/// <summary>The data area's width when the plot renders at the pane's arranged size.</summary>
	private static double DataWidth(ScottPlot.Plot plot, Border pane)
	{
		plot.RenderInMemory((int)pane.Bounds.Width, (int)(pane.Bounds.Height - pane.BorderThickness.Top));
		Dispatcher.UIThread.RunJobs();

		return plot.RenderManager.LastRender.Layout.DataRect.Width;
	}

	/// <summary>A data-age text and every mode the spectrum's state line can carry at once.</summary>
	private static IEnumerable<string> LongestSpectrumStateLines()
	{
		var time = WidestUpdateTime[..8];
		string[] modes =
		[
			Resources.SpectrumStateFrozen,
			Resources.SpectrumStateLog,
			Resources.SpectrumStateInverted,
			Resources.SpectrumStateFromZero,
		];

		return new[] { Resources.FormatSpectrumHistoricalFormat(time), Resources.FormatSpectrumStaleFormat(time) }
			.Select(age => string.Join(Resources.ChartStateSeparator, modes.Prepend(age)));
	}

	/// <summary>The widest window widths with both trend states.</summary>
	private static IEnumerable<string> LongestTrendStateLines()
	{
		TimeSpan[] widths = [new(23, 59, 0), new(0, 59, 59), TimeSpan.FromHours(24)];

		return widths.Select(width => string.Join(
			Resources.ChartStateSeparator,
			TrendViewModel.WidthText(width),
			Resources.TrendStateNotFollowing,
			Resources.SpectrumStateFrozen));
	}

	/// <summary>The width ScottPlot gives the text's longest line in the axis label's face.</summary>
	private static double ScottPlotTextWidth(string text, string fontName, float fontSize, bool isBold)
	{
		using var typeface = SKFontManager.Default
			.MatchFamily(fontName, isBold ? SKFontStyle.Bold : SKFontStyle.Normal);
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

	private void ReportFailures(int count)
	{
		for (var entry = 0; entry < count; entry++)
		{
			MessagePanel.Report(new MessageView($"title {entry}", "detail", "remedy", MessageSeverity.Error));
		}

		Dispatcher.UIThread.RunJobs();
	}
}
