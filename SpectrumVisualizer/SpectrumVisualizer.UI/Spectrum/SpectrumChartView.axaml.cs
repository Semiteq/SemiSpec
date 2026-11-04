using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using ScottPlot.Interactivity.UserActionResponses;

using SpectrumVisualizer.UI.Chart;

namespace SpectrumVisualizer.UI.Spectrum;

public partial class SpectrumChartView : UserControl
{
	// ScottPlot 5.1.59 MouseDragPan and MouseDragZoomRectangle ignore a held move shorter than this from the press.
	private const double DragThreshold = 5.0;

	private IDisposable? _redrawSubscription;
	private SpectrumChartViewModel? _viewModel;
	private int _frameSequence;
	private int? _pendingFrame;
	private Point? _pressPosition;

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

		// AvaPlot's class handler marks the wheel event handled, so these handlers run in the tunnel phase, before it.
		PlotControl.AddHandler(PointerWheelChangedEvent, OnPlotPointerWheelChanged, RoutingStrategies.Tunnel);
		PlotControl.AddHandler(PointerPressedEvent, OnPlotPointerPressed, RoutingStrategies.Tunnel);
		PlotControl.AddHandler(PointerMovedEvent, OnPlotPointerMoved, RoutingStrategies.Tunnel);
		PlotControl.AddHandler(PointerReleasedEvent, OnPlotPointerReleased, RoutingStrategies.Tunnel);
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
		var colors = ChartPalette.Colors(application, variant);

		// One hold of the lock, so no render sees new surfaces with old series colours; ApplyPalette re-enters it.
		lock (plot.Sync)
		{
			ChartPalette.Apply(plot, application, variant);
			_viewModel?.ApplyPalette(colors);
		}
	}

	private void OnDataContextChanged(object? sender, EventArgs eventArgs)
	{
		_redrawSubscription?.Dispose();
		_redrawSubscription = null;
		ClearPendingFrame();
		_viewModel = DataContext as SpectrumChartViewModel;

		// A null DataContext leaves the plot in place: AvaPlot.Reset disposes the plot it replaces, and that plot is
		// the view model's, so the same view model coming back finds its plottables.
		if (_viewModel is null)
		{
			return;
		}

		if (!ReferenceEquals(PlotControl.Plot, _viewModel.Plot))
		{
			PlotControl.Reset(_viewModel.Plot);
		}

		_redrawSubscription = _viewModel.RedrawRequested.Subscribe(_ => RequestFrame(), ClearPendingFrame);
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

	private void OnPlotPointerWheelChanged(object? sender, PointerWheelEventArgs eventArgs)
	{
		if (DataContext is SpectrumChartViewModel chart)
		{
			chart.TakeManualScale();
		}
	}

	private void OnPlotPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
	{
		_pressPosition = eventArgs.GetPosition(PlotControl);
	}

	private void OnPlotPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
	{
		_pressPosition = null;
	}

	/// <summary>A held move freezes the scale once it is far enough from the press for ScottPlot to pan or zoom.</summary>
	private void OnPlotPointerMoved(object? sender, PointerEventArgs eventArgs)
	{
		if (DataContext is not SpectrumChartViewModel { IsFollowing: true } chart || _pressPosition is not { } press)
		{
			return;
		}

		var point = eventArgs.GetCurrentPoint(PlotControl);
		var buttons = point.Properties;
		if (!buttons.IsLeftButtonPressed && !buttons.IsRightButtonPressed && !buttons.IsMiddleButtonPressed)
		{
			return;
		}

		var distance = Math.Max(Math.Abs(point.Position.X - press.X), Math.Abs(point.Position.Y - press.Y));
		if (distance >= DragThreshold)
		{
			chart.TakeManualScale();
		}
	}
}
