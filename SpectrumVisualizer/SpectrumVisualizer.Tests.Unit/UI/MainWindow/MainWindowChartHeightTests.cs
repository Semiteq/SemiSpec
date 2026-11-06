using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using ScottPlot;

using SpectrumVisualizer.UI.Messages;

using Xunit;

using MainWindowView = SpectrumVisualizer.UI.MainWindow.MainWindow;

namespace SpectrumVisualizer.Tests.Unit.UI.MainWindow;

/// <summary>
/// The window's rows at the common screen sizes, with and without the message panel: nothing runs past the window,
/// each chart keeps its data floor and the charts split 3 : 2 above their floors. The status bar's text height is
/// corrected from the headless stub to the tallest real face.
/// </summary>
[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowChartHeightTests : MainWindowLayoutTestBase
{
	/// <summary>A maximised 1366 x 768 window loses a 31 px title bar and a 48 px taskbar.</summary>
	private const double MaximisedClientHeightAt768 = 689;

	private const double DataFloor = 120;
	private const double OverviewHeight = 37;

	// Five three-line entries overflow the panel's scroll cap, so the panel stands at its tallest.
	private const int EntriesPastTheCap = 5;

	[AvaloniaTheory]
	[InlineData(1280, 800, false)]
	[InlineData(1280, 800, true)]
	[InlineData(1366, MaximisedClientHeightAt768, false)]
	[InlineData(1366, MaximisedClientHeightAt768, true)]
	[InlineData(1280, 1024, false)]
	[InlineData(1280, 1024, true)]
	[InlineData(1280, MainWindowView.MinimumHeight, true)]
	public void EveryRowStaysInsideTheWindowAndEachChartKeepsItsDataFloor(
		double width,
		double clientHeight,
		bool withMessages)
	{
		var window = Realise();
		if (withMessages)
		{
			ReportEntriesPastTheCap();
		}

		window.Width = width;
		window.Height = clientHeight;
		Dispatcher.UIThread.RunJobs();
		window.UpdateLayout();
		var rows = Measure(window);
		var spectrumData = DataHeight(Spectrum.Chart.Plot, Named<Border>(window, "SpectrumPane"));
		var trendData = DataHeight(Trend.Trend.Chart.Plot, Named<Border>(window, "TrendPane"));
		Dispatcher.UIThread.RunJobs();

		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{width} x {clientHeight}, messages {withMessages}: menu {rows.Menu}, connection {rows.Connection}, "
			+ $"content {rows.Content}, spectrum row {rows.Spectrum} (data {spectrumData:0.#}), trend row {rows.Trend} "
			+ $"(data {trendData:0.#}), overview {rows.Overview}, messages {rows.Messages}, status {rows.Status:0.#} "
			+ $"(stub {rows.StubStatus})");
		window.Bounds.Height.Should().Be(clientHeight);
		rows.Overview.Should().Be(OverviewHeight);
		rows.ChartsAndOverview.Should().BeLessThanOrEqualTo(rows.Content, "the content area never overflows its row");
		(rows.Content - (rows.Status - rows.StubStatus)).Should().BeGreaterThanOrEqualTo(
			MainWindowView.SpectrumRowMinHeight + MainWindowView.TrendRowMinHeight + rows.Overview,
			"the taller real status bar takes its height from the chart rows, never past their floors");
		rows.Spectrum.Should().BeGreaterThanOrEqualTo(MainWindowView.SpectrumRowMinHeight);
		rows.Trend.Should().BeGreaterThanOrEqualTo(MainWindowView.TrendRowMinHeight);
		spectrumData.Should().BeGreaterThanOrEqualTo(DataFloor);
		trendData.Should().BeGreaterThanOrEqualTo(DataFloor);
		if (rows.Trend > MainWindowView.TrendRowMinHeight && rows.Spectrum > MainWindowView.SpectrumRowMinHeight)
		{
			(rows.Spectrum / rows.Trend)
				.Should().BeApproximately(1.5, 0.01, "the charts split 3 : 2 above their floors");
		}
	}

	[AvaloniaFact]
	public void TheMinimumHeightHoldsEveryRowWithTheMessagePanelAtItsCapAndFitsA768Screen()
	{
		var window = Realise();
		ReportEntriesPastTheCap();

		window.Height = 400;
		Dispatcher.UIThread.RunJobs();
		window.UpdateLayout();
		var rows = Measure(window);

		var required = rows.Menu + rows.Connection + MainWindowView.SpectrumRowMinHeight
			+ MainWindowView.TrendRowMinHeight + rows.Overview + rows.Messages + rows.Status;
		TestContext.Current.TestOutputHelper?.WriteLine(
			$"rows need {required:0.#} px (status {rows.Status:0.#} in real faces), "
			+ $"minimum {MainWindowView.MinimumHeight} px");
		window.MinHeight.Should().Be(MainWindowView.MinimumHeight);
		window.Bounds.Height.Should().Be(MainWindowView.MinimumHeight);
		required.Should().BeLessThanOrEqualTo(MainWindowView.MinimumHeight);
		MainWindowView.MinimumHeight.Should().BeLessThanOrEqualTo(MaximisedClientHeightAt768);
	}

	/// <summary>The plot's data area height when it renders at its pane's size, less the pane's border.</summary>
	private static double DataHeight(Plot plot, Border pane)
	{
		var height = pane.Bounds.Height - pane.BorderThickness.Top;
		plot.RenderInMemory((int)pane.Bounds.Width, (int)height);

		return plot.RenderManager.LastRender.Layout.DataRect.Height;
	}

	private static Rows Measure(Window window)
	{
		var status = Named<Border>(window, "StatusBar");
		var statusText = Named<TextBlock>(status, "LinkText");
		var stubLine = statusText.Bounds.Height;
		var realStatus = status.Bounds.Height - stubLine
			+ RenderedText.LineHeight(statusText.FontFamily, statusText.FontSize);
		var messages = Named<Border>(window, "MessagePanel");
		var overview = Named<Border>(window, "OverviewPane");

		return new Rows(
			Named<Control>(window, "MenuBar").Bounds.Height,
			Named<Border>(window, "ConnectionRow").Bounds.Height,
			Named<Grid>(window, "ContentArea").Bounds.Height,
			Named<Border>(window, "SpectrumPane").Bounds.Height,
			Named<Border>(window, "TrendPane").Bounds.Height,
			overview.Bounds.Height,
			overview.Bounds.Bottom,
			messages.IsVisible ? messages.Bounds.Height : 0,
			realStatus,
			status.Bounds.Height);
	}

	private void ReportEntriesPastTheCap()
	{
		for (var entry = 0; entry < EntriesPastTheCap; entry++)
		{
			MessagePanel.Report(new MessageView($"title {entry}", "detail", "remedy", MessageSeverity.Error));
		}

		Dispatcher.UIThread.RunJobs();
	}

	private sealed record Rows(
		double Menu,
		double Connection,
		double Content,
		double Spectrum,
		double Trend,
		double Overview,
		double ChartsAndOverview,
		double Messages,
		double Status,
		double StubStatus);
}
