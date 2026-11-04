using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

using AwesomeAssertions;

using ScottPlot.Avalonia;
using ScottPlot.Interactivity.UserActionResponses;
using ScottPlot.Plottables;

using SpectrumVisualizer.Core.Calibration;
using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Spectrum;

using Xunit;

using BitmapCache = Avalonia.Media.BitmapCache;
using PlotColor = ScottPlot.Color;

namespace SpectrumVisualizer.Tests.Unit.UI.Spectrum;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Spectrum")]
[Trait("Category", "Unit")]
public sealed class SpectrumChartViewTests : IDisposable
{
	private const int WindowWidth = 900;
	private const int WindowHeight = 600;

	private readonly SpectrumChartViewModel _chart = new();
	private readonly SpectrumChartView _view;
	private readonly Window _window;

	public SpectrumChartViewTests()
	{
		_view = new SpectrumChartView { DataContext = _chart };
		_window = new Window { Width = WindowWidth, Height = WindowHeight, Content = _view };
	}

	private AvaPlot PlotControl => _view.FindControl<AvaPlot>("PlotControl")!;

	public void Dispose()
	{
		_window.Close();
		_chart.Dispose();
	}

	[AvaloniaFact]
	public void ThePlotIsCachedThroughItsOnlyAncestor()
	{
		Realise();

		var layer = _view.FindControl<Decorator>("PlotLayer");

		layer.Should().NotBeNull();
		layer!.CacheMode.Should().BeOfType<BitmapCache>();
		layer.Child.Should().BeSameAs(PlotControl);
		PlotControl.CacheMode.Should().BeNull();
	}

	[AvaloniaFact]
	public void ABurstOfRedrawRequestsJoinsOneFrame()
	{
		Realise();
		DriveOneFrame();
		var framesBefore = _view.ServedFrameCount;

		for (var request = 0; request < 10; request++)
		{
			_chart.Show(request % 2 == 0 ? SpectrumSamples.Atp8000Peak : SpectrumSamples.Atp2000Peak);
		}

		DriveOneFrame();

		_view.ServedFrameCount.Should().Be(framesBefore + 1, "the requests before one frame join it");

		DriveOneFrame();

		_view.ServedFrameCount.Should().Be(framesBefore + 1, "a frame nothing requested serves nothing");
	}

	[AvaloniaFact]
	public void ARequestOnADetachedViewServesNothing()
	{
		var detached = new SpectrumChartView { DataContext = _chart };
		var host = new Window { Width = WindowWidth, Height = WindowHeight };
		try
		{
			host.Show();
			_chart.Show(SpectrumSamples.Atp8000Peak);
			DriveOneFrame();

			detached.ServedFrameCount.Should().Be(0, "a view outside a window requests no frame");

			host.Content = detached;
			DriveOneFrame();
			var framesAfterAttaching = detached.ServedFrameCount;
			_chart.Show(SpectrumSamples.Atp2000Peak);
			DriveOneFrame();

			framesAfterAttaching.Should().Be(1, "the view requests its first frame when it loads");
			detached.ServedFrameCount.Should().Be(2, "the request on the detached view left no frame pending");
		}
		finally
		{
			host.Close();
		}
	}

	[AvaloniaFact]
	public void TheEmptyStateShowsUntilTheFirstSpectrum()
	{
		Realise();
		var message = _view.FindControl<TextBlock>("EmptyStateMessage")!;

		message.IsVisible.Should().BeTrue();
		message.Text.Should().Be(Resources.SpectrumEmpty);

		_chart.Show(SpectrumSamples.Atp8000Peak);
		Dispatcher.UIThread.RunJobs();

		message.IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public void ALoadedViewRepaintsThePlotWhenTheVariantChanges()
	{
		var application = Application.Current!;
		var previous = application.RequestedThemeVariant;
		try
		{
			application.RequestedThemeVariant = ThemeVariant.Light;
			Realise();
			var light = _chart.Plot.FigureBackground.Color;

			application.RequestedThemeVariant = ThemeVariant.Dark;
			Dispatcher.UIThread.RunJobs();

			light.Should().Be(Expected("AppPanelBackgroundBrush", ThemeVariant.Light));
			_chart.Plot.FigureBackground.Color.Should().Be(Expected("AppPanelBackgroundBrush", ThemeVariant.Dark));
			CentreLine().Color.Should().Be(Expected("AppMarkerLineBrush", ThemeVariant.Dark));
		}
		finally
		{
			application.RequestedThemeVariant = previous;
		}
	}

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

		_window.MouseWheel(centre, new Vector(0.0, 1.0));
		Dispatcher.UIThread.RunJobs();

		_chart.IsFollowing.Should().BeFalse();
	}

	[AvaloniaFact]
	public void APressAndReleaseWithoutAMoveKeepsFollowing()
	{
		var centre = ShowSpectrum();
		_window.MouseMove(centre);

		_window.MouseDown(centre, MouseButton.Left);
		_window.MouseUp(centre, MouseButton.Left);
		Dispatcher.UIThread.RunJobs();

		_chart.IsFollowing.Should().BeTrue();
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
		_window.MouseMove(centre);

		_window.MouseDown(centre, MouseButton.Left);
		// The headless window does not track the held button, so the move carries it as the platform would.
		_window.MouseMove(moved, RawInputModifiers.LeftMouseButton);
		var followingWhileHeld = _chart.IsFollowing;
		_window.MouseUp(moved, MouseButton.Left);
		Dispatcher.UIThread.RunJobs();

		followingWhileHeld.Should().Be(keepsFollowing, "ScottPlot pans only from 5 px away from the press");
		_chart.IsFollowing.Should().Be(keepsFollowing);
	}

	[AvaloniaFact]
	public void AWheelOnTheEmptyPlotLeavesTheFirstSpectrumFollowing()
	{
		Realise();
		var centre = PlotControl.TranslatePoint(
				new Point(PlotControl.Bounds.Width / 2, PlotControl.Bounds.Height / 2),
				_window)
			?? throw new InvalidOperationException("The plot control is not in the window.");

		_window.MouseWheel(centre, new Vector(0.0, 1.0));
		Dispatcher.UIThread.RunJobs();
		_chart.Show(SpectrumSamples.Atp8000Peak);

		_chart.IsFollowing.Should().BeTrue();
		var calibration = SpectrometerCalibration.For(Spectrometer.Atp8000);
		var limits = _chart.Plot.Axes.GetLimits();
		limits.Left.Should().Be(calibration.WavelengthOf(0));
		limits.Right.Should().Be(calibration.WavelengthOf(Spectrometer.Atp8000.PixelCount - 1));
	}

	[AvaloniaFact]
	public void AnUnloadedViewIgnoresAVariantChange()
	{
		var application = Application.Current!;
		var previous = application.RequestedThemeVariant;
		try
		{
			application.RequestedThemeVariant = ThemeVariant.Light;
			Realise();
			_window.Content = null;
			Dispatcher.UIThread.RunJobs();

			application.RequestedThemeVariant = ThemeVariant.Dark;
			Dispatcher.UIThread.RunJobs();

			_chart.Plot.FigureBackground.Color.Should().Be(Expected("AppPanelBackgroundBrush", ThemeVariant.Light));
		}
		finally
		{
			application.RequestedThemeVariant = previous;
		}
	}

	[AvaloniaFact]
	public void ANullDataContextAndBackKeepsThePlottables()
	{
		Realise();
		var plottables = _chart.Plot.GetPlottables().ToList();

		_view.DataContext = null;
		_view.DataContext = _chart;
		Dispatcher.UIThread.RunJobs();

		PlotControl.Plot.Should().BeSameAs(_chart.Plot);
		_chart.Plot.GetPlottables().Should().Equal(plottables);
	}

	[AvaloniaFact]
	public void ADataContextSwitchEndsTheOldRedrawSubscription()
	{
		using var other = new SpectrumChartViewModel();
		Realise();

		_view.DataContext = other;
		DriveOneFrame();
		var framesBefore = _view.ServedFrameCount;
		_chart.Show(SpectrumSamples.Atp8000Peak);
		DriveOneFrame();

		PlotControl.Plot.Should().BeSameAs(other.Plot);
		_view.ServedFrameCount.Should().Be(framesBefore, "the old view model's requests no longer reach the view");
	}

	[AvaloniaFact]
	public void DisposingTheViewModelClearsThePendingFrame()
	{
		var disposed = new SpectrumChartViewModel();
		_view.DataContext = disposed;
		Realise();
		DriveOneFrame();
		var framesBefore = _view.ServedFrameCount;

		disposed.Show(SpectrumSamples.Atp8000Peak);
		disposed.Dispose();
		DriveOneFrame();

		_view.ServedFrameCount.Should().Be(framesBefore, "the completed requests left no frame to serve");
	}

	private static void DriveOneFrame()
	{
		Dispatcher.UIThread.RunJobs();
		AvaloniaHeadlessPlatform.ForceRenderTimerTick();
		Dispatcher.UIThread.RunJobs();
	}

	private static PlotColor Expected(string key, ThemeVariant variant)
	{
		Application.Current!.TryGetResource(key, variant, out var value).Should().BeTrue();
		var brush = (ISolidColorBrush)value!;
		var alpha = (byte)Math.Round(brush.Color.A * brush.Opacity);
		return new PlotColor(brush.Color.R, brush.Color.G, brush.Color.B, alpha);
	}

	private void Realise()
	{
		_window.Show();
		Dispatcher.UIThread.RunJobs();
	}

	private VerticalLine CentreLine()
	{
		return _chart.Plot.GetPlottables().OfType<VerticalLine>().Single();
	}

	/// <summary>Shows a spectrum on the realised view and returns the data area's centre in window coordinates.</summary>
	private Point ShowSpectrum()
	{
		Realise();
		_chart.Show(SpectrumSamples.Atp8000Peak);
		DriveOneFrame();

		var plotControl = PlotControl;
		_chart.Plot.RenderInMemory((int)plotControl.Bounds.Width, (int)plotControl.Bounds.Height);
		var dataRect = _chart.Plot.RenderManager.LastRender.Layout.DataRect;
		dataRect.HasArea.Should().BeTrue("the pointer lands inside the data area");
		_chart.IsFollowing.Should().BeTrue();

		return plotControl.TranslatePoint(new Point(dataRect.HorizontalCenter, dataRect.VerticalCenter), _window)
			?? throw new InvalidOperationException("The plot control is not in the window.");
	}
}
