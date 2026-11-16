using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

using AwesomeAssertions;

using SemiSpec.Tests.Unit.Core.Store;
using SemiSpec.UI.Chart;
using SemiSpec.UI.Link;
using SemiSpec.UI.Localization;
using SemiSpec.UI.Messages;
using SemiSpec.UI.Trend;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Trend;

/// <summary>The normalization switch, Off, x100 and x1000, in the realised trend view.</summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendNormalizeSwitchViewTests : TrendChartViewTestBase
{
	private MirroredRadioButton Off => View.FindControl<MirroredRadioButton>("NormalizeOff")!;

	private MirroredRadioButton Small => View.FindControl<MirroredRadioButton>("NormalizeSmall")!;

	private MirroredRadioButton Large => View.FindControl<MirroredRadioButton>("NormalizeLarge")!;

	private Border Segments => View.FindControl<Border>("NormalizeSegments")!;

	private Grid Band => View.FindControl<Grid>("StateLineBand")!;

	[AvaloniaFact]
	public void TheSwitchFitsTheTopMarginEndsAtTheDataAreasRightEdgeAndStartsOff()
	{
		ShowTrend();
		DriveOneFrame();

		View.FindControl<TextBlock>("NormalizeLabel")!.Text.Should().Be(Resources.TrendNormalizeLabel);
		new[] { Off, Small, Large }.Select(segment => segment.Content).Should().Equal(
			Resources.TrendNormalizeOff, Resources.TrendNormalizeSmall, Resources.TrendNormalizeLarge);
		Off.IsChecked.Should().BeTrue();
		Segments.Bounds.Height.Should().BeLessThanOrEqualTo(
			Band.Bounds.Height, $"the {ChartStateLine.TopMargin} px margin holds the switch");
		var segmentsRight = Segments.TranslatePoint(new Point(Segments.Bounds.Width, 0), PlotControl)!.Value.X;
		segmentsRight.Should().BeApproximately(DataRect.Right, 1.0, "the switch ends at the data area's right edge");
		var smallRight = Small.TranslatePoint(new Point(Small.Bounds.Width, 0), PlotControl)!.Value.X;
		var largeLeft = Large.TranslatePoint(default, PlotControl)!.Value.X;
		largeLeft.Should().BeApproximately(smallRight, 0.5, "the segments are joined");
	}

	[AvaloniaFact]
	public void AnAcceptedClickChecksTheButton()
	{
		ShowTrend();
		DriveOneFrame();
		LetAvailabilityRun();

		Click(Small);

		Small.IsChecked.Should().BeTrue();
		Large.IsChecked.Should().BeFalse();
		Off.IsChecked.Should().BeFalse();
		Trend.StateText.Should().EndWith(Resources.FormatTrendStateNormalizedFormat(100));
	}

	[AvaloniaFact]
	public void AClickTheCacheRefusesLeavesTheButtonReleased()
	{
		ShowTrend();
		Add(StoreSamples.Dark(At(1800)));
		DriveOneFrame();
		LetAvailabilityRun();
		Small.IsEffectivelyEnabled.Should().BeTrue("a dark frame still covers the wavelength");

		Click(Small);

		Small.IsChecked.Should().BeFalse();
		Off.IsChecked.Should().BeTrue();
		MessagePanel.NewestEntry!.View.Severity.Should().Be(MessageSeverity.Warning);
	}

	[AvaloniaFact]
	public void AClickOnADisabledButtonDoesNotReachThePlot()
	{
		ShowTrend();
		LinkStatuses.OnNext(new LinkStatus(LinkState.NotResponding, "COM3", null, 0, null));
		DriveOneFrame();
		LetAvailabilityRun();
		Small.IsEffectivelyEnabled.Should().BeFalse();

		Click(Small);

		Switching.ShownMoment.Should().BeNull("a press on the greyed button is no click on the trend");
		SpectrumCalls.Should().BeEmpty();
		Small.IsChecked.Should().BeFalse();
	}

	[AvaloniaFact]
	public void AClickOnOffSwitchesTheNormalizationOff()
	{
		ShowTrend();
		DriveOneFrame();
		LetAvailabilityRun();
		Click(Large);

		Click(Off);

		Off.IsChecked.Should().BeTrue();
		Large.IsChecked.Should().BeFalse();
		Cache.Normalization.IsOn.Should().BeFalse();
	}

	private void LetAvailabilityRun()
	{
		Scheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks);
		Dispatcher.UIThread.RunJobs();
	}

	private void Click(Control button)
	{
		var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), Window)!.Value;
		Window.MouseDown(centre, MouseButton.Left);
		Window.MouseUp(centre, MouseButton.Left);
		Dispatcher.UIThread.RunJobs();
	}
}
