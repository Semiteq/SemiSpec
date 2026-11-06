using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using SemiSpec.UI.Localization;
using SemiSpec.UI.Messages;
using SemiSpec.UI.Spectrum;
using SemiSpec.UI.Trend;

using Xunit;

using static SemiSpec.Tests.Unit.UI.Spectrum.SpectrumSamples;

using MainWindowView = SemiSpec.UI.MainWindow.MainWindow;

namespace SemiSpec.Tests.Unit.UI.MainWindow;

/// <summary>
/// The content area with the trend: its rows, the side pane across them and the trend section's form.
/// </summary>
[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowTrendLayoutTests : MainWindowLayoutTestBase
{
	private const double OverviewHeight = 37;

	private static readonly string[] _trendParts = ["TrendHeader", "TimeWindowLabel", "TimeWindowPreset", "FollowLive"];

	[AvaloniaFact]
	public void TheContentAreaSplitsTheChartRowsThreeToTwoAboveTheirFloorAndEndsWithTheOverview()
	{
		var content = Named<Grid>(Realise(), "ContentArea");

		content.RowDefinitions.Select(row => row.Height)
			.Should().Equal(
				new GridLength(3, GridUnitType.Star), new GridLength(2, GridUnitType.Star), GridLength.Auto);
		content.RowDefinitions.Select(row => row.MinHeight)
			.Should().Equal(MainWindowView.SpectrumRowMinHeight, MainWindowView.TrendRowMinHeight, 0);
	}

	[AvaloniaFact]
	public void TheTrendStandsUnderTheSpectrumTheOverviewUnderTheTrendAndTheSidePaneSpansAllThree()
	{
		var window = Realise();
		var spectrumPane = Named<Border>(window, "SpectrumPane");
		var trendPane = Named<Border>(window, "TrendPane");
		var overviewPane = Named<Border>(window, "OverviewPane");
		var signalPane = Named<Border>(window, "SignalPane");
		Control[] chartPanes = [spectrumPane, trendPane, overviewPane];

		chartPanes.Select(Grid.GetRow).Should().Equal(0, 1, 2);
		chartPanes.Select(Grid.GetColumn).Should().OnlyContain(column => column == 0);
		(Grid.GetRow(signalPane), Grid.GetRowSpan(signalPane), Grid.GetColumn(signalPane)).Should().Be((0, 3, 1));

		trendPane.Bounds.Top.Should().Be(spectrumPane.Bounds.Bottom);
		overviewPane.Bounds.Top.Should().Be(trendPane.Bounds.Bottom);
		overviewPane.Bounds.Height.Should().Be(OverviewHeight, "the 36 px band and its 1 px top border");
		new[] { trendPane, overviewPane }.Select(pane => pane.BorderThickness)
			.Should().OnlyContain(border => border == new Thickness(0, 1, 0, 0));
		signalPane.Bounds.Top.Should().Be(spectrumPane.Bounds.Top);
		signalPane.Bounds.Bottom.Should().Be(overviewPane.Bounds.Bottom);
		signalPane.Bounds.Width.Should().Be(SpectrumPanel.PaneWidth);

		trendPane.Child.Should().BeOfType<TrendChartView>().Which.DataContext.Should().BeSameAs(Trend.Trend);
		overviewPane.Child.Should().BeOfType<OverviewView>()
			.Which.DataContext.Should().BeSameAs(Trend.Trend.Overview);
	}

	[AvaloniaFact]
	public void TheTrendSectionShowsThePresetsAndFollowLive()
	{
		var pane = Named<Border>(Realise(), "SignalPane");

		Named<TextBlock>(pane, "TrendHeader").Text.Should().Be(Resources.TrendTitle);
		Named<TextBlock>(pane, "TimeWindowLabel").Text.Should().Be(Resources.TrendTimeWindow);
		var presets = Named<ComboBox>(pane, "TimeWindowPreset");
		presets.Width.Should().Be(120, "a short code from the width table");
		presets.Items.OfType<TrendPreset>().Select(preset => preset.Label)
			.Should().Equal(TrendPresets.All.Select(preset => preset.Label));
		presets.SelectedItem.Should().BeOfType<TrendPreset>().Which.Width.Should().Be(TimeSpan.FromMinutes(10));
		var followLive = Named<Button>(pane, "FollowLive");
		followLive.Content.Should().Be(Resources.TrendFollowLive);
		followLive.IsEffectivelyEnabled.Should().BeFalse("the trend follows live");
	}

	[AvaloniaFact]
	public void TheTrendSectionSharesTheFormGridOfTheSignalPane()
	{
		var pane = Named<Border>(Realise(), "SignalPane");

		var form = (Grid)pane.GetVisualDescendants().OfType<TrendPanel>().Single().Content!;
		var formOrigin = form.TranslatePoint(default, pane)!.Value;
		var scroll = Named<ScrollViewer>(pane, "SidePaneScroll");

		double Left(string name)
		{
			return Named<Control>(pane, name).TranslatePoint(default, pane)!.Value.X;
		}

		double Top(string name)
		{
			var part = Named<Control>(pane, name);
			return part.TranslatePoint(default, pane)!.Value.Y;
		}

		double Bottom(string name)
		{
			return Top(name) + Named<Control>(pane, name).Bounds.Height;
		}

		// The arranged slot is the grid cell, so a centred text's rounding inside its row does not count as a gap.
		Rect Cell(string name)
		{
			return LayoutInformation.GetPreviousArrangeBounds(Named<Control>(pane, name))!.Value;
		}

		var rightInset = pane.Bounds.Width - scroll.TranslatePoint(default, pane)!.Value.X - scroll.Bounds.Width;
		TestContext.Current.TestOutputHelper?.WriteLine(
			$"trend form at {formOrigin}, right inset {rightInset}, label column {Cell("TimeWindowLabel").Width}, "
			+ $"header {Top("TrendHeader") - Bottom("BackToLive")} above and "
			+ $"{Cell("TimeWindowLabel").Top - Cell("TrendHeader").Bottom} below, "
			+ $"field {Left("TimeWindowPreset") - formOrigin.X - Cell("TimeWindowLabel").Right} after the labels, "
			+ $"rows {Top("FollowLive") - Bottom("TimeWindowPreset")} apart");
		formOrigin.X.Should().Be(PaneBorder + PanePadding, "the left border and the pane padding");
		rightInset.Should().Be(PanePadding, "equal padding on both sides");
		Left("TimeWindowLabel").Should().Be(Left("DeviceLabel"), "one label edge");
		Left("TimeWindowPreset").Should().Be(formOrigin.X + Cell("TimeWindowLabel").Right + LabelToField);
		new[] { Left("TimeWindowPreset"), Left("FollowLive") }
			.Should().AllSatisfy(left => left.Should().Be(Left("ResetScale"), "one field edge across both sections"));
		(Top("TrendHeader") - Bottom("BackToLive")).Should().Be(SectionHeaderAbove);
		(Cell("TimeWindowLabel").Top - Cell("TrendHeader").Bottom).Should().Be(RowSpacing, "8 below a section header");
		(Top("FollowLive") - Bottom("TimeWindowPreset")).Should().Be(RowSpacing);
	}

	[AvaloniaFact]
	public void AtALargeWidthOnlyTheChartsAndTheOverviewGrowAndTheSidePaneKeepsItsContent()
	{
		var window = Realise();
		var pane = Named<Border>(window, "SignalPane");
		string[] chartPanes = ["SpectrumPane", "TrendPane", "OverviewPane"];
		var openingWidths = chartPanes.Select(name => Named<Border>(window, name).Bounds.Width).ToList();
		var openingOffsets = TrendOffsets(pane);

		window.Width = LargeWidth;
		Dispatcher.UIThread.RunJobs();

		pane.Bounds.Width.Should().Be(SpectrumPanel.PaneWidth);
		chartPanes.Select(name => Named<Border>(window, name).Bounds.Width).Should().Equal(
			openingWidths.Select(width => width + LargeWidth - MainWindowView.OpeningWidth),
			"the charts and the overview take every added pixel");
		TrendOffsets(pane).Should().Equal(openingOffsets, "the trend section stays left-aligned inside the pane");
	}

	[AvaloniaFact]
	public void EveryTrendStateKeepsTheWindowAndThePaneBoundsAndTheMessagePanelScrollsThePane()
	{
		var window = Realise();
		var shapes = new List<(string State, TrendShape Shape)> { ("no data", ShapeOf(window)) };
		void Record(string state)
		{
			Trend.Sample();
			Dispatcher.UIThread.RunJobs();
			window.UpdateLayout();
			shapes.Add((state, ShapeOf(window)));
		}

		Trend.AddSpectraEndingNow(TimeSpan.FromMinutes(20));
		Record("live, following");
		Trend.Trend.Chart.PanBy(TimeSpan.FromMinutes(-2));
		Record("live, not following");
		SwitchingSource.ShowMoment(Atp8000Peak.Measurement, ReceivedAt);
		Record("historical");
		SwitchingSource.ReturnToLive();
		Record("back to live");
		var opening = shapes[0].Shape;

		MessagePanel.ReportFailure(new TrendHistoryError(), NullLogger.Instance);
		Dispatcher.UIThread.RunJobs();
		window.UpdateLayout();

		Trend.Trend.Chart.HasPoints.Should().BeTrue();
		Trend.Trend.Chart.IsFollowing.Should().BeFalse();
		shapes.Select(shape => shape.Shape).Should().AllBeEquivalentTo(opening);
		var failed = ShapeOf(window);
		var messageHeight = Named<Border>(window, "MessagePanel").Bounds.Height;
		var scroll = Named<ScrollViewer>(window, "SidePaneScroll");
		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{window.Bounds.Size} with the message panel ({messageHeight} px): "
			+ $"side pane {failed.Pane.Height} px high, "
			+ $"scroll extent {scroll.Extent.Height} px in a {scroll.Viewport.Height} px viewport");
		failed.Window.Should().Be(opening.Window, "a failed re-read never changes the window size");
		failed.Pane.Should().Be(opening.Pane.WithHeight(opening.Pane.Height - messageHeight));
		new[] { failed.Spectrum, failed.Trend, failed.Overview }.Select(bounds => bounds.Width)
			.Should().Equal(opening.Spectrum.Width, opening.Trend.Width, opening.Overview.Width);
		scroll.ScrollBarMaximum.Y.Should().Be(
			Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height),
			"every row of the side pane stays reachable by scrolling");
	}

	[AvaloniaFact]
	public void TheHistoricalLineShowsOverTheSpectrumChartOnlyWhileAMomentIsShown()
	{
		var window = Realise();
		var line = Named<TextBlock>(Named<Border>(window, "SpectrumPane"), "HistoricalLine");
		line.IsEffectivelyVisible.Should().BeFalse("the live spectrum is shown");

		SwitchingSource.ShowMoment(Atp8000Peak.Measurement, ReceivedAt);
		Dispatcher.UIThread.RunJobs();

		var localTime = ReceivedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
		line.IsEffectivelyVisible.Should().BeTrue();
		line.Text.Should().Be(Resources.FormatSpectrumHistoricalFormat(localTime));

		SwitchingSource.ReturnToLive();
		Dispatcher.UIThread.RunJobs();

		line.IsEffectivelyVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public void TheSidePaneScrollsVerticallyOnlyAndAtTheChartFloorsStillHoldsEveryPart()
	{
		var window = Realise();

		window.Height = MainWindowView.MinimumHeight;
		Dispatcher.UIThread.RunJobs();

		var pane = Named<Border>(window, "SignalPane");
		var scroll = Named<ScrollViewer>(pane, "SidePaneScroll");
		scroll.VerticalScrollBarVisibility.Should().Be(ScrollBarVisibility.Auto);
		scroll.HorizontalScrollBarVisibility.Should().Be(ScrollBarVisibility.Disabled);
		pane.Bounds.Width.Should().Be(SpectrumPanel.PaneWidth);
		pane.Bounds.Height.Should().BeGreaterThanOrEqualTo(
			MainWindowView.SpectrumRowMinHeight + MainWindowView.TrendRowMinHeight + OverviewHeight,
			"the content area keeps both chart floors and the overview");
		scroll.Extent.Height.Should().BeLessThanOrEqualTo(scroll.Viewport.Height, "every part shows without scrolling");
	}

	private static List<Point> TrendOffsets(Border pane)
	{
		return [.. _trendParts.Select(name => Named<Control>(pane, name).TranslatePoint(default, pane)!.Value)];
	}

	private static TrendShape ShapeOf(Window window)
	{
		return new TrendShape(
			window.Bounds.Size,
			Named<Border>(window, "SignalPane").Bounds,
			Named<Border>(window, "SpectrumPane").Bounds,
			Named<Border>(window, "TrendPane").Bounds,
			Named<Border>(window, "OverviewPane").Bounds);
	}

	private sealed record TrendShape(Size Window, Rect Pane, Rect Spectrum, Rect Trend, Rect Overview);
}
