using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

using AwesomeAssertions;

using ScottPlot.Avalonia;

using SpectrumVisualizer.UI.Spectrum;

namespace SpectrumVisualizer.Tests.Unit.UI.Spectrum;

/// <summary>A chart view over a fresh chart view model, inside a window that the test shows when it needs to.</summary>
public abstract class SpectrumChartViewTestBase : IDisposable
{
	protected const int WindowWidth = 900;
	protected const int WindowHeight = 600;

	protected SpectrumChartViewTestBase()
	{
		View = new SpectrumChartView { DataContext = Chart };
		Window = new Window { Width = WindowWidth, Height = WindowHeight, Content = View };
	}

	protected SpectrumChartViewModel Chart { get; } = new();

	protected SpectrumChartView View { get; }

	protected Window Window { get; }

	protected AvaPlot PlotControl => View.FindControl<AvaPlot>("PlotControl")!;

	public void Dispose()
	{
		Window.Close();
		Chart.Dispose();
		GC.SuppressFinalize(this);
	}

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

	/// <summary>Shows a spectrum on the realised view; returns the data area's centre in window coordinates.</summary>
	protected Point ShowSpectrum()
	{
		Realise();
		Chart.Show(SpectrumSamples.Atp8000Peak);
		DriveOneFrame();

		var plotControl = PlotControl;
		Chart.Plot.RenderInMemory((int)plotControl.Bounds.Width, (int)plotControl.Bounds.Height);
		var dataRect = Chart.Plot.RenderManager.LastRender.Layout.DataRect;
		dataRect.HasArea.Should().BeTrue("the pointer lands inside the data area");
		Chart.IsFollowing.Should().BeTrue();

		return plotControl.TranslatePoint(new Point(dataRect.HorizontalCenter, dataRect.VerticalCenter), Window)
			?? throw new InvalidOperationException("The plot control is not in the window.");
	}
}
