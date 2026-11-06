using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AwesomeAssertions;

using SpectrumVisualizer.UI.Localization;

using Xunit;

using BitmapCache = Avalonia.Media.BitmapCache;

namespace SpectrumVisualizer.Tests.Unit.UI.Trend;

/// <summary>
/// The realised trend view: cache layout, frame pacing, empty state, and pointer input through Avalonia.
/// </summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendChartViewTests : TrendChartViewTestBase
{
	private const double DragPixels = 40.0;
	private const double SubSlopPixels = 3.0;

	private static readonly TimeSpan _tenMinutes = TimeSpan.FromMinutes(10);

	private Line CrosshairLine => View.FindControl<Line>("CrosshairLine")!;

	private Border ReadoutBox => View.FindControl<Border>("ReadoutBox")!;

	private TextBlock ReadoutText => View.FindControl<TextBlock>("ReadoutText")!;

	[AvaloniaFact]
	public void ThePlotIsCachedThroughItsOnlyAncestor()
	{
		Realise();

		var layer = View.FindControl<Decorator>("PlotLayer");

		layer.Should().NotBeNull();
		layer!.CacheMode.Should().BeOfType<BitmapCache>();
		layer.Child.Should().BeSameAs(PlotControl);
		PlotControl.CacheMode.Should().BeNull();
		PlotControl.Plot.Should().BeSameAs(Chart.Plot);
	}

	[AvaloniaFact]
	public void ABurstOfRedrawRequestsJoinsOneFrame()
	{
		ShowTrend();
		DriveOneFrame();
		var framesBefore = View.ServedFrameCount;

		for (var request = 0; request < 10; request++)
		{
			Chart.SetWidth(request % 2 == 0 ? TimeSpan.FromMinutes(1) : _tenMinutes);
		}

		DriveOneFrame();

		View.ServedFrameCount.Should().Be(framesBefore + 1, "the requests before one frame join it");

		DriveOneFrame();

		View.ServedFrameCount.Should().Be(framesBefore + 1, "a frame nothing requested serves nothing");
	}

	[AvaloniaFact]
	public void TheInputProcessorIsDisabledAndTheWheelUnhandled()
	{
		ShowTrend();
		var centre = DataAreaCentre;
		var anchor = TimeAt(centre);

		PlotControl.UserInputProcessor.IsEnabled.Should().BeFalse();
		PlotControl.HandleMouseWheelEvent.Should().BeFalse();

		Window.MouseWheel(InWindow(centre), new Vector(0.0, 1.0));
		Dispatcher.UIThread.RunJobs();

		Chart.Width.Should().BeLessThan(_tenMinutes, "a wheel notch up zooms in");
		TimeAt(centre).Should().BeCloseTo(anchor, TimeSpan.FromSeconds(1), "the time under the cursor stays under it");
	}

	[AvaloniaFact]
	public void AShiftWheelUpPansATenthIntoThePast()
	{
		ShowTrend();
		var fromBefore = Chart.From;

		Window.MouseWheel(InWindow(DataAreaCentre), new Vector(0.0, 1.0), RawInputModifiers.Shift);

		Chart.From.Should().Be(fromBefore - (_tenMinutes / 10));
		Chart.Width.Should().Be(_tenMinutes);
		Chart.IsFollowing.Should().BeFalse();
	}

	[AvaloniaFact]
	public void ADragPastFourPixelsPansAndStopsFollowing()
	{
		ShowTrend();
		var press = DataAreaCentre;
		var release = new Point(press.X + DragPixels, press.Y);
		var expectedShift = TimeAt(release) - TimeAt(press);
		var fromBefore = Chart.From;

		Window.MouseDown(InWindow(press), MouseButton.Left);
		Window.MouseMove(InWindow(new Point(press.X + SubSlopPixels, press.Y)));
		Chart.From.Should().Be(fromBefore, "a move inside the slop pans nothing");
		Window.MouseMove(InWindow(release));
		Window.MouseUp(InWindow(release), MouseButton.Left);
		Dispatcher.UIThread.RunJobs();

		Chart.From.Should().BeCloseTo(fromBefore - expectedShift, TimeSpan.FromMilliseconds(1));
		Chart.Width.Should().Be(_tenMinutes);
		Chart.IsFollowing.Should().BeFalse();
		Switching.ShownMoment.Should().BeNull("a drag is no click");
	}

	[AvaloniaFact]
	public void AClickWithinFourPixelsShowsTheMoment()
	{
		ShowTrend();
		Chart.SetWidth(TimeSpan.FromMinutes(1));
		Render();
		var press = DataAreaCentre;
		var moment = TimeAt(press);
		var fromBefore = Chart.From;

		Window.MouseDown(InWindow(press), MouseButton.Left);
		Window.MouseMove(InWindow(new Point(press.X + SubSlopPixels, press.Y)));
		Window.MouseUp(InWindow(new Point(press.X + SubSlopPixels, press.Y)), MouseButton.Left);
		Dispatcher.UIThread.RunJobs();

		Switching.ShownMoment.Should().NotBeNull();
		Switching.ShownMoment!.Value.Should().BeOnOrBefore(moment);
		Switching.ShownMoment.Value.Should().BeAfter(moment - Second, "the latest spectrum at or before the press");
		Chart.From.Should().Be(fromBefore);
		Chart.IsFollowing.Should().BeTrue();
		SpectrumCalls.Should().BeEmpty("the moment lies inside the recent spectra");
	}

	[AvaloniaFact]
	public void ALostCaptureEndsTheDragAndTheReleaseShowsNothing()
	{
		ShowTrend();
		var press = DataAreaCentre;
		IPointer? pressed = null;
		Window.AddHandler(
			InputElement.PointerPressedEvent,
			(_, eventArgs) => pressed = eventArgs.Pointer,
			RoutingStrategies.Bubble,
			handledEventsToo: true);
		var fromBefore = Chart.From;

		Window.MouseDown(InWindow(press), MouseButton.Left);
		pressed!.Capture(null);
		Window.MouseMove(InWindow(new Point(press.X + DragPixels, press.Y)));
		Window.MouseUp(InWindow(new Point(press.X + DragPixels, press.Y)), MouseButton.Left);
		Dispatcher.UIThread.RunJobs();

		Chart.From.Should().Be(fromBefore, "no drag remains after the capture is lost");
		Switching.ShownMoment.Should().BeNull();
		CrosshairLine.IsVisible.Should().BeTrue("the move hovers instead");
	}

	[AvaloniaFact]
	public void TheHoverOverlayFollowsThePointerAndHidesOnExit()
	{
		ShowTrend();
		var hover = new Point(DataRect.Left + (DataRect.Width * 0.25), DataRect.VerticalCenter);

		Window.MouseMove(InWindow(hover));

		CrosshairLine.IsVisible.Should().BeTrue();
		CrosshairLine.StartPoint.X.Should().BeApproximately(hover.X, 1.0);
		CrosshairLine.StartPoint.Y.Should().BeApproximately(DataRect.Top, 1.0);
		CrosshairLine.EndPoint.Y.Should().BeApproximately(DataRect.Bottom, 1.0);
		ReadoutBox.IsVisible.Should().BeTrue();
		Canvas.GetLeft(ReadoutBox).Should().BeApproximately(
			CrosshairLine.StartPoint.X, 0.5, "away from the right edge the box starts at the hover line");
		ReadoutText.Text.Should().Be(Chart.ReadoutAt(TimeAt(hover)));
		ReadoutText.Text.Should().NotBeNullOrEmpty();

		Window.MouseMove(new Point(ViewMargin / 2, ViewMargin / 2));

		CrosshairLine.IsVisible.Should().BeFalse();
		ReadoutBox.IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public void AtTheRightEdgeTheReadoutFlipsLeftOfTheLineAndStaysInsideTheView()
	{
		ShowTrend();
		var hover = new Point(DataRect.Right - 1, DataRect.VerticalCenter);

		Window.MouseMove(InWindow(hover));

		ReadoutBox.IsVisible.Should().BeTrue();
		var left = Canvas.GetLeft(ReadoutBox);
		var width = ReadoutBox.DesiredSize.Width;
		width.Should().BeGreaterThan(0);
		(left + width).Should().BeApproximately(CrosshairLine.StartPoint.X, 0.5, "the box ends at the hover line");
		left.Should().BeGreaterThanOrEqualTo(0);
		(left + width).Should().BeLessThanOrEqualTo(View.Bounds.Width, "the box never runs over the side pane");
	}

	[AvaloniaFact]
	public void TheHoverOverlayHidesOutsideTheDataArea()
	{
		ShowTrend();

		Window.MouseMove(InWindow(new Point(DataRect.Left / 2, DataRect.VerticalCenter)));

		CrosshairLine.IsVisible.Should().BeFalse();
		ReadoutBox.IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public void TheHoverOverlayHidesWhileDragging()
	{
		ShowTrend();
		var press = DataAreaCentre;
		var dragged = new Point(press.X + DragPixels, press.Y);
		Window.MouseMove(InWindow(press));
		CrosshairLine.IsVisible.Should().BeTrue();

		Window.MouseDown(InWindow(press), MouseButton.Left);
		Window.MouseMove(InWindow(dragged));
		DriveOneFrame();

		CrosshairLine.IsVisible.Should().BeFalse();
		ReadoutBox.IsVisible.Should().BeFalse();

		Window.MouseUp(InWindow(dragged), MouseButton.Left);
		Window.MouseMove(InWindow(new Point(dragged.X + 1, dragged.Y)));

		CrosshairLine.IsVisible.Should().BeTrue("the overlay returns with the first move after the release");
	}

	[AvaloniaFact]
	public void TheEmptyStateShowsUntilTheFirstPoint()
	{
		Realise();
		var message = View.FindControl<TextBlock>("EmptyStateMessage")!;

		message.IsVisible.Should().BeTrue();
		message.Text.Should().Be(Resources.TrendEmpty);

		AddSpectra(0, 1);
		Sample();
		Dispatcher.UIThread.RunJobs();

		message.IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public void ADataContextSwitchEndsTheOldRedrawSubscription()
	{
		Realise();
		View.DataContext = null;
		DriveOneFrame();
		var framesBefore = View.ServedFrameCount;

		Chart.SetWidth(TimeSpan.FromMinutes(1));
		DriveOneFrame();

		View.ServedFrameCount.Should().Be(framesBefore, "the dropped view model's requests no longer reach the view");
		PlotControl.Plot.Should().BeSameAs(Chart.Plot, "a null data context leaves the plot in place");
	}
}
