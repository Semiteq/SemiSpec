using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

using AwesomeAssertions;

using ScottPlot;
using ScottPlot.Avalonia;

using SemiSpec.UI.Trend;

using PixelRect = ScottPlot.PixelRect;
using Point = Avalonia.Point;

namespace SemiSpec.Tests.Unit.UI.Trend;

/// <summary>
/// A trend chart view over the feed stand, inside a window that leaves a margin around the view, so the pointer can
/// leave the plot.
/// </summary>
public abstract class TrendChartViewTestBase : TrendFeedTestBase
{
	protected const int WindowWidth = 900;
	protected const int WindowHeight = 400;
	protected const double ViewMargin = 32;

	protected TrendChartViewTestBase()
	{
		Overview = NewOverview();
		Trend = new TrendViewModel(Chart, Overview, Feed);
		Feed.Start();
		View = new TrendChartView { DataContext = Trend };
		Window = new Window
		{
			Width = WindowWidth,
			Height = WindowHeight,
			Content = new Border { Padding = new Thickness(ViewMargin), Child = View }
		};
	}

	protected OverviewViewModel Overview { get; }

	protected TrendViewModel Trend { get; }

	protected TrendChartView View { get; }

	protected Window Window { get; }

	protected AvaPlot PlotControl => View.FindControl<AvaPlot>("PlotControl")!;

	protected PixelRect DataRect => Chart.Plot.RenderManager.LastRender.Layout.DataRect;

	/// <summary>The data area's centre in plot control coordinates.</summary>
	protected Point DataAreaCentre => new(DataRect.HorizontalCenter, DataRect.VerticalCenter);

	protected static void DriveOneFrame()
	{
		Dispatcher.UIThread.RunJobs();
		AvaloniaHeadlessPlatform.ForceRenderTimerTick();
		Dispatcher.UIThread.RunJobs();
	}

	protected void Realise()
	{
		Window.Show();
		Dispatcher.UIThread.RunJobs();
	}

	/// <summary>
	/// Thirty minutes of spectra on the realised view, rendered once so the pointer maths has a data area.
	/// </summary>
	protected void ShowTrend()
	{
		Realise();
		AddSpectra(0, 1800);
		Sample();
		Render();
		Chart.IsFollowing.Should().BeTrue();
	}

	/// <summary>Renders the plot at the control's size and lets the reported data area width reach the chart.</summary>
	protected void Render()
	{
		var plotControl = PlotControl;
		Chart.Plot.RenderInMemory((int)plotControl.Bounds.Width, (int)plotControl.Bounds.Height);
		DataRect.HasArea.Should().BeTrue("the pointer lands inside the data area");
		DriveOneFrame();
	}

	/// <summary>A point of the plot control in the window's client coordinates, where headless input lands.</summary>
	protected Point InWindow(Point plotPoint)
	{
		return PlotControl.TranslatePoint(plotPoint, Window)
			?? throw new InvalidOperationException("The plot control is not in the window.");
	}

	/// <summary>The time under a point of the plot control, as the view converts it.</summary>
	protected DateTimeOffset TimeAt(Point plotPoint)
	{
		var x = Chart.Plot.GetCoordinates(new Pixel((float)plotPoint.X, (float)plotPoint.Y)).X;
		return LocalTimeAxis.FromAxis(x);
	}

	protected override void Dispose(bool disposing)
	{
		Window.Close();
		Trend.Dispose();
		Overview.Dispose();
		base.Dispose(disposing);
	}
}
