using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using ScottPlot.Interactivity.UserActionResponses;

using Serilog;

using SpectrumVisualizer.UI.Chart;

namespace SpectrumVisualizer.UI.Spectrum;

public partial class SpectrumChartView : UserControl
{
	private IDisposable? _redrawSubscription;
	private SpectrumChartViewModel? _viewModel;
	private int _frameSequence;
	private int? _pendingFrame;

	public SpectrumChartView()
	{
		InitializeComponent();
		DataContextChanged += OnDataContextChanged;

		// A read-only viewer: ScottPlot's autoscale and keyboard responses would move the limits behind the scale state.
		PlotControl.UserInputProcessor.UserActionResponses.RemoveAll(response => response
			is DoubleClickBenchmark
			or SingleClickAutoscale
			or SingleClickContextMenu
			or KeyboardAutoscale
			or KeyboardPanAndZoom);

		// AvaPlot's class handler marks the wheel event handled, so both handlers run in the tunnel phase, before it.
		PlotControl.AddHandler(PointerWheelChangedEvent, OnPlotPointerWheelChanged, RoutingStrategies.Tunnel);
		PlotControl.AddHandler(PointerMovedEvent, OnPlotPointerMoved, RoutingStrategies.Tunnel);
	}

	/// <summary>Animation-frame callbacks that served a redraw request since the view was built.</summary>
	internal int ServedFrameCount { get; private set; }

	protected override void OnLoaded(RoutedEventArgs e)
	{
		base.OnLoaded(e);

		if (Application.Current is { } application)
		{
			application.ActualThemeVariantChanged += OnActualThemeVariantChanged;
		}

		RepaintChart();
	}

	protected override void OnUnloaded(RoutedEventArgs e)
	{
		ClearPendingFrame();

		if (Application.Current is { } application)
		{
			application.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
		}

		base.OnUnloaded(e);
	}

	private void OnActualThemeVariantChanged(object? sender, EventArgs eventArgs)
	{
		RepaintChart();
	}

	private void RepaintChart()
	{
		ApplyChartPalette();
		RequestFrame();
	}

	private void ApplyChartPalette()
	{
		if (Application.Current is not { } application)
		{
			return;
		}

		var variant = application.ActualThemeVariant;
		var plot = PlotControl.Plot;
		lock (plot.Sync)
		{
			ChartPalette.Apply(plot, application, variant);
		}

		_viewModel?.ApplyPalette(ChartPalette.Colors(application, variant));
	}

	private void OnDataContextChanged(object? sender, EventArgs eventArgs)
	{
		_redrawSubscription?.Dispose();
		_redrawSubscription = null;
		ClearPendingFrame();
		_viewModel = DataContext as SpectrumChartViewModel;

		if (_viewModel is null)
		{
			return;
		}

		PlotControl.Reset(_viewModel.Plot);
		_redrawSubscription = _viewModel.RedrawRequested
			.Subscribe(
				_ => RequestFrame(),
				ReportFailureOf(nameof(SpectrumChartViewModel.RedrawRequested)),
				ClearPendingFrame);
		RepaintChart();
	}

	private void RequestFrame()
	{
		if (_pendingFrame is not null || TopLevel.GetTopLevel(this) is not { } topLevel)
		{
			return;
		}

		var frame = ++_frameSequence;
		topLevel.RequestAnimationFrame(_ => ServeFrame(frame));
		_pendingFrame = frame;
	}

	private void ServeFrame(int frame)
	{
		if (_pendingFrame != frame)
		{
			return;
		}

		_pendingFrame = null;

		if (_viewModel is null || TopLevel.GetTopLevel(this) is null)
		{
			return;
		}

		PlotControl.InvalidateVisual();
		ServedFrameCount++;
	}

	private void ClearPendingFrame()
	{
		_pendingFrame = null;
	}

	private static Action<Exception> ReportFailureOf(string pipeline)
	{
		return failure => Log.Error(failure, "The spectrum chart view's {Pipeline} subscription ended on a failure", pipeline);
	}

	private void OnPlotPointerWheelChanged(object? sender, PointerWheelEventArgs eventArgs)
	{
		if (DataContext is SpectrumChartViewModel chart)
		{
			chart.TakeManualScale();
		}
	}

	private void OnPlotPointerMoved(object? sender, PointerEventArgs eventArgs)
	{
		if (DataContext is not SpectrumChartViewModel { IsFollowing: true } chart)
		{
			return;
		}

		var buttons = eventArgs.GetCurrentPoint(PlotControl).Properties;
		if (buttons.IsLeftButtonPressed || buttons.IsRightButtonPressed || buttons.IsMiddleButtonPressed)
		{
			chart.TakeManualScale();
		}
	}
}
