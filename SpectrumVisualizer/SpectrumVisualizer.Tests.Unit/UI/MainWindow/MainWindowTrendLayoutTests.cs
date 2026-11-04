using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Spectrum;
using SpectrumVisualizer.UI.Trend;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.UI.Spectrum.SpectrumSamples;

namespace SpectrumVisualizer.Tests.Unit.UI.MainWindow;

/// <summary>The content area with the trend: its rows, the side pane across them and the trend section's form.</summary>
[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowTrendLayoutTests : MainWindowLayoutTestBase
{
	private const double ChartRowMinHeight = 260;
	private const double OverviewHeight = 37;

	[AvaloniaFact]
	public void TheContentAreaSplitsTheChartRowsThreeToTwoAboveTheirFloorAndEndsWithTheOverview()
	{
		var content = Named<Grid>(Realise(), "ContentArea");

		content.RowDefinitions.Select(row => row.Height)
			.Should().Equal(new GridLength(3, GridUnitType.Star), new GridLength(2, GridUnitType.Star), GridLength.Auto);
		content.RowDefinitions.Select(row => row.MinHeight).Should().Equal(ChartRowMinHeight, ChartRowMinHeight, 0);
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

		Left("TimeWindowLabel").Should().Be(Left("DeviceLabel"), "one label edge");
		new[] { Left("TimeWindowPreset"), Left("FollowLive") }
			.Should().AllSatisfy(left => left.Should().Be(Left("ResetScale"), "one field edge across both sections"));
		(Top("TrendHeader") - Bottom("BackToLive")).Should().Be(SectionHeaderAbove);
		(Top("TimeWindowPreset") - Bottom("TrendHeader")).Should().BeGreaterThanOrEqualTo(RowSpacing);
		(Top("FollowLive") - Bottom("TimeWindowPreset")).Should().Be(RowSpacing);
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

		window.Height = 480;
		Dispatcher.UIThread.RunJobs();

		var pane = Named<Border>(window, "SignalPane");
		var scroll = Named<ScrollViewer>(pane, "SidePaneScroll");
		scroll.VerticalScrollBarVisibility.Should().Be(ScrollBarVisibility.Auto);
		scroll.HorizontalScrollBarVisibility.Should().Be(ScrollBarVisibility.Disabled);
		pane.Bounds.Width.Should().Be(SpectrumPanel.PaneWidth);
		pane.Bounds.Height.Should().BeGreaterThanOrEqualTo(
			(2 * ChartRowMinHeight) + OverviewHeight,
			"the content area keeps both chart floors and the overview");
		scroll.Extent.Height.Should().BeLessThanOrEqualTo(scroll.Viewport.Height, "every part shows without scrolling");
	}
}
