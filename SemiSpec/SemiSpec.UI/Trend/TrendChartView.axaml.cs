using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using ScottPlot;

using SemiSpec.UI.Chart;

using Cursor = Avalonia.Input.Cursor;

namespace SemiSpec.UI.Trend;

public partial class TrendChartView : UserControl
{
	private const double WheelNarrowFactor = 0.8;
	private const double WheelWidenFactor = 1.25;
	private const double WheelPanFraction = 0.1;
	private const double ClickSlopPixels = 4.0;

	private static readonly Cursor _handCursor = new(StandardCursorType.Hand);
	private static readonly Cursor _grabbingCursor = new(StandardCursorType.SizeAll);

	// ScottPlot's own constructor seeds the render events with an empty delegate; this restores that state when the
	// view drops the last handler.
	private static readonly EventHandler<RenderDetails> _noRenderFinishedHandler = (_, _) => { };

	private readonly CompositeDisposable _subscriptions = [];
	private readonly ChartFrames _frames;
	private readonly TrendCursorOverlay _overlay;
	private readonly AxisFlyout _intensityAxis;
	private readonly AxisFlyout _timeAxis;
	private TrendViewModel? _viewModel;
	private IDisposable? _windowClosing;
	private ChartDrag? _drag;
	private DateTimeOffset? _hoverTime;

	public TrendChartView()
	{
		InitializeComponent();
		_intensityAxis = new AxisFlyout(PlotControl, (Flyout)Resources["IntensityAxisFlyout"]!);
		_timeAxis = new AxisFlyout(PlotControl, (Flyout)Resources["TimeWindowFlyout"]!);
		_frames = new ChartFrames(
			PlotControl,
			() => _viewModel is not null,
			colors => _viewModel?.Chart.ApplyPalette(colors),
			frameServed: OnFrameServed);
		_overlay = new TrendCursorOverlay(CursorOverlay, CrosshairLine, ReadoutBox, ReadoutText);
		DataContextChanged += OnDataContextChanged;

		PlotControl.UserInputProcessor.Disable();
		// AvaPlot marks every wheel event handled in its class handler, which would keep OnPointerWheelChanged from
		// ever running.
		PlotControl.HandleMouseWheelEvent = false;
		PlotControl.Cursor = _handCursor;
		PlotControl.PointerWheelChanged += OnPointerWheelChanged;
		PlotControl.PointerPressed += OnPointerPressed;
		PlotControl.PointerMoved += OnPointerMoved;
		// AvaPlot's class handler releases the capture first, which would end the drag before a bubbling handler runs.
		PlotControl.AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
		PlotControl.PointerCaptureLost += OnPointerCaptureLost;
		PlotControl.PointerExited += OnPointerExited;
	}

	internal int ServedFrameCount => _frames.ServedFrameCount;

	protected override void OnLoaded(RoutedEventArgs e)
	{
		base.OnLoaded(e);
		_frames.Attach();
		if (TopLevel.GetTopLevel(this) is Window window)
		{
			_windowClosing = AxisFlyout.HideOnClosing(window, _intensityAxis, _timeAxis);
		}
	}

	protected override void OnUnloaded(RoutedEventArgs e)
	{
		_windowClosing?.Dispose();
		_windowClosing = null;
		_intensityAxis.Hide();
		_timeAxis.Hide();
		_frames.Detach();
		base.OnUnloaded(e);
	}

	private void OnDataContextChanged(object? sender, EventArgs eventArgs)
	{
		// Hidden while the old form is attached, so its pending fields commit as on unload.
		_intensityAxis.Hide();
		_timeAxis.Hide();
		_subscriptions.Clear();
		_frames.ClearPending();
		_hoverTime = null;
		_overlay.Hide();
		_viewModel = DataContext as TrendViewModel;

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

		var renderManager = chart.Plot.RenderManager;
		var reportDataAreaWidth = ReportDataAreaWidthTo(chart);
		renderManager.RenderFinished += reportDataAreaWidth;

		_subscriptions.Add(Disposable.Create(() => renderManager.RenderFinished =
			(renderManager.RenderFinished - reportDataAreaWidth) ?? _noRenderFinishedHandler));
		_subscriptions.Add(chart.RedrawRequested.Subscribe(_ => _frames.Request(), _frames.ClearPending));
		_subscriptions.Add(_intensityAxis.Attach(_viewModel.VerticalForm));
		_subscriptions.Add(_timeAxis.Attach(_viewModel.TimeWindow));
		_frames.Repaint();
	}

	/// <summary>Hands each new data area width of a render to the chart, which folds one column per pixel.</summary>
	private static EventHandler<RenderDetails> ReportDataAreaWidthTo(TrendChartViewModel chart)
	{
		float? lastPostedWidth = null;

		return (_, renderDetails) =>
		{
			var dataAreaWidth = renderDetails.DataRect.Width;
			if (dataAreaWidth == lastPostedWidth)
			{
				return;
			}

			lastPostedWidth = dataAreaWidth;
			Dispatcher.UIThread.Post(() => chart.SetDataAreaWidth(dataAreaWidth));
		};
	}

	private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs eventArgs)
	{
		if (_viewModel is null || !HasDataArea())
		{
			return;
		}

		var isShiftHeld = (eventArgs.KeyModifiers & KeyModifiers.Shift) != 0;
		var notch = isShiftHeld && eventArgs.Delta.Y == 0 ? Math.Sign(eventArgs.Delta.X) : Math.Sign(eventArgs.Delta.Y);
		if (notch == 0)
		{
			return;
		}

		var chart = _viewModel.Chart;
		if (isShiftHeld)
		{
			// Wheel down pans toward newer data.
			chart.PanBy(chart.Width * (WheelPanFraction * -notch));
		}
		else
		{
			chart.ZoomAt(notch > 0 ? WheelNarrowFactor : WheelWidenFactor, TimeAt(eventArgs.GetPosition(PlotControl)));
		}

		eventArgs.Handled = true;
	}

	/// <summary>
	/// A left press on the intensity axis opens its flyout at once. Any other left press starts a drag that pans; its
	/// release without a move opens the time window flyout on the time axis and shows the moment elsewhere.
	/// </summary>
	private void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
	{
		if (_viewModel is not { } viewModel
			|| _drag is not null
			|| !HasDataArea()
			|| !eventArgs.GetCurrentPoint(PlotControl).Properties.IsLeftButtonPressed)
		{
			return;
		}

		var position = eventArgs.GetPosition(PlotControl);
		if (ChartAxisRegion.TryCreateLeft(PlotControl.Plot)?.Contains(position) == true)
		{
			eventArgs.Handled = true;
			_overlay.Hide();
			_intensityAxis.Open();
			return;
		}

		var isOnTimeAxis = ChartAxisRegion.TryCreateBottom(PlotControl.Plot)?.Contains(position) == true;
		_drag = new ChartDrag(viewModel, eventArgs.Pointer, position, IsStarted: false, isOnTimeAxis);
		PlotControl.Cursor = _grabbingCursor;
		eventArgs.Pointer.Capture(PlotControl);
		_overlay.Hide();
	}

	private void OnPointerMoved(object? sender, PointerEventArgs eventArgs)
	{
		if (_viewModel is null)
		{
			return;
		}

		var current = eventArgs.GetPosition(PlotControl);

		if (_drag is { } drag)
		{
			if (eventArgs.Pointer != drag.Pointer
				|| (!drag.IsStarted && Point.Distance(drag.Origin, current) <= ClickSlopPixels))
			{
				return;
			}

			_drag = drag with { Origin = current, IsStarted = true };
			drag.ViewModel.Chart.PanBy(TimeAt(drag.Origin) - TimeAt(current));

			return;
		}

		if (!HasDataArea())
		{
			return;
		}

		_hoverTime = TimeAt(current);
		RepositionCursorOverlay();
	}

	private void OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
	{
		if (_drag is not { } drag
			|| eventArgs.Pointer != drag.Pointer
			|| eventArgs.InitialPressMouseButton != MouseButton.Left)
		{
			return;
		}

		// The view holds the capture here, so releasing it raises PointerCaptureLost, which ends the drag.
		eventArgs.Pointer.Capture(null);

		if (drag.IsStarted || drag.ViewModel != _viewModel)
		{
			return;
		}

		if (drag.IsOnTimeAxis)
		{
			_timeAxis.Open();
			return;
		}

		drag.ViewModel.ShowMoment(TimeAt(drag.Origin));
	}

	// Capture can be lost mid-drag without a PointerReleased (window deactivation, focus steal).
	private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs eventArgs)
	{
		if (_drag is not { } drag || eventArgs.Pointer != drag.Pointer)
		{
			return;
		}

		_drag = null;
		PlotControl.Cursor = _handCursor;
	}

	private void OnPointerExited(object? sender, PointerEventArgs eventArgs)
	{
		_hoverTime = null;
		_overlay.Hide();
	}

	private void OnFrameServed()
	{
		ChartStateLine.Place(StateLineBand, PlotControl.Plot);
		RepositionCursorOverlay();
	}

	private void RepositionCursorOverlay()
	{
		if (_viewModel is null || _drag is not null || _hoverTime is not { } hoverTime || !HasDataArea())
		{
			_overlay.Hide();
			return;
		}

		_overlay.ShowAt(PlotControl.Plot, hoverTime, _viewModel.Chart.ReadoutAt(hoverTime));
	}

	private bool HasDataArea()
	{
		return PlotControl.Plot.LastRender.DataRect.Width > 0f;
	}

	private DateTimeOffset TimeAt(Point position)
	{
		var x = PlotControl.Plot.GetCoordinates(new Pixel((float)position.X, (float)position.Y)).X;

		return LocalTimeAxis.FromAxis(x);
	}

	private readonly record struct ChartDrag(
		TrendViewModel ViewModel,
		IPointer Pointer,
		Point Origin,
		bool IsStarted,
		bool IsOnTimeAxis);
}
