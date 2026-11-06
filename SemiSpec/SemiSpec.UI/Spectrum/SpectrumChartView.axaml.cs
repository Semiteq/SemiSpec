using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using ScottPlot.Interactivity.UserActionResponses;

using SemiSpec.UI.Chart;

namespace SemiSpec.UI.Spectrum;

public partial class SpectrumChartView : UserControl
{
	private readonly ChartFrames _frames;
	private IDisposable? _redrawSubscription;
	private SpectrumChartViewModel? _viewModel;
	private Point? _pressPosition;

	public SpectrumChartView()
	{
		InitializeComponent();
		_frames = new ChartFrames(
			PlotControl,
			() => _viewModel is not null,
			colors => _viewModel?.ApplyPalette(colors),
			frameServed: () => { });
		DataContextChanged += OnDataContextChanged;

		// Only pan and zoom stay: autoscale and keyboard responses would move the limits behind the scale.
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
	internal int ServedFrameCount => _frames.ServedFrameCount;

	protected override void OnLoaded(RoutedEventArgs e)
	{
		base.OnLoaded(e);
		_frames.Attach();
	}

	protected override void OnUnloaded(RoutedEventArgs e)
	{
		_frames.Detach();
		base.OnUnloaded(e);
	}

	private void OnDataContextChanged(object? sender, EventArgs eventArgs)
	{
		_redrawSubscription?.Dispose();
		_redrawSubscription = null;
		_frames.ClearPending();
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

		_redrawSubscription = _viewModel.RedrawRequested.Subscribe(_ => _frames.Request(), _frames.ClearPending);
		_frames.Repaint();
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
