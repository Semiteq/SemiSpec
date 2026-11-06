using System.Reactive.Concurrency;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using ReactiveUI.Avalonia;

using ScottPlot.Avalonia;

using SemiSpec.Tests.Unit.UI.Link;
using SemiSpec.UI.Link;
using SemiSpec.UI.Messages;
using SemiSpec.UI.Spectrum;

namespace SemiSpec.Tests.Unit.UI.Spectrum;

/// <summary>A chart view over a fresh chart view model, inside a window that the test shows when it needs to.</summary>
public abstract class SpectrumChartViewTestBase : IDisposable
{
	protected const int WindowWidth = 900;
	protected const int WindowHeight = 600;

	private readonly FakeDisplayedSpectrumSource _source = new();
	private readonly SwitchingSpectrumSource _switching;
	private readonly MessagePanelViewModel _messagePanel = new(TimeProvider.System);
	private readonly SerialLink _serialLink =
		new(new FakeSerialPortProvider(), ImmediateScheduler.Instance, NullLogger<SerialLink>.Instance);

	protected SpectrumChartViewTestBase()
	{
		_switching = new SwitchingSpectrumSource(_source);
		Spectrum = NewSpectrum();
		View = new SpectrumChartView { DataContext = Spectrum };
		Window = new Window { Width = WindowWidth, Height = WindowHeight, Content = View };
	}

	protected SpectrumViewModel Spectrum { get; }

	protected SpectrumChartViewModel Chart => Spectrum.Chart;

	protected SpectrumChartView View { get; }

	protected Window Window { get; }

	protected AvaPlot PlotControl => View.FindControl<AvaPlot>("PlotControl")!;

	public void Dispose()
	{
		Window.Close();
		Spectrum.Dispose();
		_switching.Dispose();
		_source.Dispose();
		_serialLink.Dispose();
		_messagePanel.Dispose();
		GC.SuppressFinalize(this);
	}

	/// <summary>Another spectrum view model over the same source, for a DataContext switch.</summary>
	protected SpectrumViewModel NewSpectrum()
	{
		return new SpectrumViewModel(
			_switching,
			_serialLink,
			_messagePanel,
			TimeProvider.System,
			AvaloniaScheduler.Instance,
			NullLogger<SpectrumViewModel>.Instance);
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
