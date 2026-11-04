using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using ScottPlot.Interactivity.UserActionResponses;

using SpectrumVisualizer.UI.Chart;

namespace SpectrumVisualizer.UI.Spectrum;

public partial class SpectrumChartView : UserControl
{
	private IDisposable? _redrawSubscription;
	private SpectrumChartViewModel? _viewModel;
	private int _frameSequence;
	private int? _pendingFrame;
	private Point? _pressPosition;

	public SpectrumChartView()
	{
		InitializeComponent();
		DataContextChanged += OnDataContextChanged;

		// A read-only viewer: ScottPlot's autoscale and keyboard responses would move the limits behind the scale.
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
		_viewModel?.TakeManualScale();
	}

	private void OnPlotPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
	{
		_pressPosition = eventArgs.GetPosition(PlotControl);
	}

	private void OnPlotPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
	{
		_pressPosition = null;
	}

	private void OnPlotPointerMoved(object? sender, PointerEventArgs eventArgs)
	{
		if (_viewModel is null || _pressPosition is not { } press)
		{
			return;
		}

		var point = eventArgs.GetCurrentPoint(PlotControl);
		var buttons = point.Properties;
		var isButtonHeld = buttons.IsLeftButtonPressed || buttons.IsRightButtonPressed || buttons.IsMiddleButtonPressed;
		_viewModel.OnHeldMove(press, point.Position, isButtonHeld);
	}
}
