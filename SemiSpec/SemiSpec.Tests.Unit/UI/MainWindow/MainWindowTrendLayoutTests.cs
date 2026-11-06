using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using SemiSpec.UI.Spectrum;
using SemiSpec.UI.Trend;

using Xunit;

using MainWindowView = SemiSpec.UI.MainWindow.MainWindow;

namespace SemiSpec.Tests.Unit.UI.MainWindow;

/// <summary>
/// The content area: the spectrum row with the readout beside it, then the trend and the overview across the window.
/// </summary>
[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowTrendLayoutTests : MainWindowLayoutTestBase
{
	private const double OverviewHeight = 37;

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
	public void TheTrendAndTheOverviewSpanTheWindowAndTheReadoutSitsBesideTheSpectrumOnly()
	{
		var window = Realise();
		var spectrumPane = Named<Border>(window, "SpectrumPane");
		var trendPane = Named<Border>(window, "TrendPane");
		var overviewPane = Named<Border>(window, "OverviewPane");
		var signalPane = Named<Border>(window, "SignalPane");
		Control[] chartPanes = [spectrumPane, trendPane, overviewPane];

		chartPanes.Select(Grid.GetRow).Should().Equal(0, 1, 2);
		chartPanes.Select(Grid.GetColumn).Should().OnlyContain(column => column == 0);
		chartPanes.Select(Grid.GetColumnSpan).Should().Equal(1, 2, 2);
		(Grid.GetRow(signalPane), Grid.GetRowSpan(signalPane), Grid.GetColumn(signalPane)).Should().Be((0, 1, 1));

		trendPane.Bounds.Top.Should().Be(spectrumPane.Bounds.Bottom);
		overviewPane.Bounds.Top.Should().Be(trendPane.Bounds.Bottom);
		overviewPane.Bounds.Height.Should().Be(OverviewHeight, "the 36 px band and its 1 px top border");
		new[] { trendPane, overviewPane }.Select(pane => pane.BorderThickness)
			.Should().OnlyContain(border => border == new Thickness(0, 1, 0, 0));
		signalPane.Bounds.Top.Should().Be(spectrumPane.Bounds.Top);
		signalPane.Bounds.Bottom.Should().Be(spectrumPane.Bounds.Bottom);
		trendPane.Bounds.Width.Should().Be(window.Bounds.Width);
		overviewPane.Bounds.Width.Should().Be(window.Bounds.Width);
		signalPane.Bounds.Width.Should().Be(SpectrumPanel.PaneWidth);

		trendPane.Child.Should().BeOfType<TrendChartView>().Which.DataContext.Should().BeSameAs(Trend.Trend);
		overviewPane.Child.Should().BeOfType<OverviewView>()
			.Which.DataContext.Should().BeSameAs(Trend.Trend.Overview);
	}
}
