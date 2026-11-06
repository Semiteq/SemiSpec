using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

using AwesomeAssertions;

using ScottPlot.Interactivity.UserActionResponses;

using SpectrumVisualizer.Core.Protocol;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Spectrum;

/// <summary>ScottPlot's input responses on the realised view, and the wheel and drag that freeze the scale.</summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Spectrum")]
[Trait("Category", "Unit")]
public sealed class SpectrumChartViewInputTests : SpectrumChartViewTestBase
{
	[AvaloniaFact]
	public void OnlyPanZoomAndWheelResponsesRemain()
	{
		var responses = PlotControl.UserInputProcessor.UserActionResponses;

		responses.Should().NotContain(response => response is DoubleClickBenchmark
			|| response is SingleClickAutoscale
			|| response is SingleClickContextMenu
			|| response is KeyboardAutoscale
			|| response is KeyboardPanAndZoom);
		responses.Should().ContainSingle(response => response is MouseDragPan);
		responses.Should().ContainSingle(response => response is MouseDragZoom);
		responses.Should().ContainSingle(response => response is MouseDragZoomRectangle);
		responses.Should().ContainSingle(response => response is MouseWheelZoom);
	}

	[AvaloniaFact]
	public void AWheelStepFreezesTheScale()
	{
		var centre = ShowSpectrum();

		Window.MouseWheel(centre, new Vector(0.0, 1.0));
		Dispatcher.UIThread.RunJobs();

		Chart.IsFollowing.Should().BeFalse();
	}

	[AvaloniaFact]
	public void APressAndReleaseWithoutAMoveKeepsFollowing()
	{
		var centre = ShowSpectrum();
		Window.MouseMove(centre);

		Window.MouseDown(centre, MouseButton.Left);
		Window.MouseUp(centre, MouseButton.Left);
		Dispatcher.UIThread.RunJobs();

		Chart.IsFollowing.Should().BeTrue();
	}

	[AvaloniaTheory]
	[InlineData(0.0, true)]
	[InlineData(2.0, true)]
	[InlineData(6.0, false)]
	[InlineData(40.0, false)]
	public void AHeldMoveFreezesTheScaleFromFivePixels(double distance, bool keepsFollowing)
	{
		var centre = ShowSpectrum();
		var moved = new Point(centre.X + distance, centre.Y);
		Window.MouseMove(centre);

		Window.MouseDown(centre, MouseButton.Left);
		// The headless window does not track the held button, so the move carries it as the platform would.
		Window.MouseMove(moved, RawInputModifiers.LeftMouseButton);
		var followingWhileHeld = Chart.IsFollowing;
		Window.MouseUp(moved, MouseButton.Left);
		Dispatcher.UIThread.RunJobs();

		followingWhileHeld.Should().Be(keepsFollowing, "ScottPlot pans only from 5 px away from the press");
		Chart.IsFollowing.Should().Be(keepsFollowing);
	}

	[AvaloniaFact]
	public void AWheelOnTheEmptyPlotLeavesTheFirstSpectrumFollowing()
	{
		Realise();
		var centre = PlotControl.TranslatePoint(
				new Point(PlotControl.Bounds.Width / 2, PlotControl.Bounds.Height / 2),
				Window)
			?? throw new InvalidOperationException("The plot control is not in the window.");

		Window.MouseWheel(centre, new Vector(0.0, 1.0));
		Dispatcher.UIThread.RunJobs();
		Chart.Show(SpectrumSamples.Atp8000Peak);

		Chart.IsFollowing.Should().BeTrue();
		Chart.ShouldSpanTheDeviceRange(Spectrometer.Atp8000);
	}
}
