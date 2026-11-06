using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using ScottPlot.Interactivity.UserActionResponses;

using SemiSpec.UI.Chart;

namespace SemiSpec.UI.Spectrum;

public partial class SpectrumChartView : UserControl
{
	private readonly ChartFrames _frames;
	private readonly AxisFlyout _intensityAxis;
	private readonly AxisFlyout _wavelengthAxis;
	private readonly CompositeDisposable _subscriptions = [];
	private SpectrumViewModel? _viewModel;
	private Point? _pressPosition;

	public SpectrumChartView()
	{
		InitializeComponent();
		_intensityAxis = new AxisFlyout(PlotControl, (Flyout)Resources["IntensityAxisFlyout"]!);
		_wavelengthAxis = new AxisFlyout(PlotControl, (Flyout)Resources["WavelengthAxisFlyout"]!);
		_frames = new ChartFrames(
			PlotControl,
			() => _viewModel is not null,
			colors => _viewModel?.Chart.ApplyPalette(colors),
			frameServed: PlaceStateLine);
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
		PlotControl.SizeChanged += (_, _) => Dispatcher.UIThread.Post(PlaceStateLine, DispatcherPriority.Background);
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
		_intensityAxis.Hide();
		_wavelengthAxis.Hide();
		_frames.Detach();
		base.OnUnloaded(e);
	}

	private void OnDataContextChanged(object? sender, EventArgs eventArgs)
	{
		_subscriptions.Clear();
		_intensityAxis.Hide();
		_wavelengthAxis.Hide();
		_frames.ClearPending();
		_viewModel = DataContext as SpectrumViewModel;

		// A null DataContext leaves the plot in place: AvaPlot.Reset disposes the plot it replaces, and that plot is
		// the view model's, so the same view model coming back finds its plottables.
		if (_viewModel is null)
		{
			return;
		}

		var chart = _viewModel.Chart;
		if (!ReferenceEquals(PlotControl.Plot, chart.Plot))
		{
			PlotControl.Reset(chart.Plot);
		}

		_subscriptions.Add(chart.RedrawRequested.Subscribe(_ => _frames.Request(), _frames.ClearPending));
		_subscriptions.Add(_intensityAxis.Attach(_viewModel.VerticalForm));
		_subscriptions.Add(_wavelengthAxis.Attach(_viewModel.HorizontalForm));
		_frames.Repaint();
	}

	private void PlaceStateLine()
	{
		ChartStateLine.Place(StateLineBand, PlotControl.Plot);
	}

	private void OnPlotPointerWheelChanged(object? sender, PointerWheelEventArgs eventArgs)
	{
		_viewModel?.Chart.TakeManualScale();
	}

	/// <summary>
	/// A left press on an axis band opens that axis's flyout and never reaches ScottPlot, so it neither pans nor
	/// zooms; every other press goes on to ScottPlot's pan and zoom.
	/// </summary>
	private void OnPlotPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
	{
		var position = eventArgs.GetPosition(PlotControl);
		if (_viewModel is not null && eventArgs.GetCurrentPoint(PlotControl).Properties.IsLeftButtonPressed)
		{
			var plot = PlotControl.Plot;
			var flyout = ChartAxisRegion.TryCreateLeft(plot)?.Contains(position) == true ? _intensityAxis
				: ChartAxisRegion.TryCreateBottom(plot)?.Contains(position) == true ? _wavelengthAxis
				: null;
			if (flyout is not null)
			{
				eventArgs.Handled = true;
				flyout.Open();
				return;
			}
		}

		_pressPosition = position;
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
		_viewModel.Chart.OnHeldMove(press, point.Position, isButtonHeld);
	}
}
