using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using ScottPlot;

using SpectrumVisualizer.UI.Chart;

using Cursor = Avalonia.Input.Cursor;
using PixelRect = ScottPlot.PixelRect;

namespace SpectrumVisualizer.UI.Trend;

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
	private TrendViewModel? _viewModel;
	private ChartDrag? _drag;
	private DateTimeOffset? _hoverTime;
	private int _frameSequence;
	private int? _pendingFrame;

	public TrendChartView()
	{
		InitializeComponent();
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
			_viewModel?.Chart.ApplyPalette(colors);
		}
	}

	private void OnDataContextChanged(object? sender, EventArgs eventArgs)
	{
		_subscriptions.Clear();
		ClearPendingFrame();
		_hoverTime = null;
		HideCursorOverlay();
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

		// RenderFinished is a plain delegate property, so the removal keeps the empty-delegate contract instead of
		// leaving null behind.
		_subscriptions.Add(Disposable.Create(() => renderManager.RenderFinished =
			(renderManager.RenderFinished - reportDataAreaWidth) ?? _noRenderFinishedHandler));
		_subscriptions.Add(chart.RedrawRequested.Subscribe(_ => RequestFrame(), ClearPendingFrame));
		RepaintChart();
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
		RepositionCursorOverlay();
		ServedFrameCount++;
	}

	private void ClearPendingFrame()
	{
		_pendingFrame = null;
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

	private void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
	{
		if (_viewModel is not { } viewModel
			|| _drag is not null
			|| !HasDataArea()
			|| !eventArgs.GetCurrentPoint(PlotControl).Properties.IsLeftButtonPressed)
		{
			return;
		}

		_drag = new ChartDrag(viewModel, eventArgs.Pointer, eventArgs.GetPosition(PlotControl), IsStarted: false);
		PlotControl.Cursor = _grabbingCursor;
		eventArgs.Pointer.Capture(PlotControl);
		HideCursorOverlay();
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

		drag.ViewModel.ShowMoment(TimeAt(drag.Origin));
	}

	// Capture can be lost mid-drag without a PointerReleased (window deactivation, focus steal); this mirrors the
	// release path's cleanup so the drag state and the grab cursor do not stay stuck.
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
		HideCursorOverlay();
	}

	private void RepositionCursorOverlay()
	{
		if (_viewModel is null || _drag is not null || _hoverTime is not { } hoverTime || !HasDataArea())
		{
			HideCursorOverlay();

			return;
		}

		var plot = PlotControl.Plot;
		var dataRect = plot.LastRender.DataRect;
		var cursorPixelX = plot.GetPixel(new Coordinates(LocalTimeAxis.ToAxis(hoverTime), 0.0)).X;
		var placement = ProjectOverlay(cursorPixelX, dataRect, plot.ScaleFactor);
		var readout = _viewModel.Chart.ReadoutAt(hoverTime);
		if (placement is not { } line || readout is null)
		{
			HideCursorOverlay();

			return;
		}

		CrosshairLine.StartPoint = new Point(line.X, line.Top);
		CrosshairLine.EndPoint = new Point(line.X, line.Bottom);
		CrosshairLine.IsVisible = true;

		// A hidden control measures as empty, so the box shows before it measures its new text.
		ReadoutBox.IsVisible = true;
		ReadoutText.Text = readout;
		ReadoutBox.Measure(Size.Infinity);
		var readoutWidth = ReadoutBox.DesiredSize.Width;
		var fitsRightOfLine = line.X + readoutWidth <= Bounds.Width;
		Canvas.SetLeft(ReadoutBox, fitsRightOfLine ? line.X : Math.Max(0.0, line.X - readoutWidth));
		Canvas.SetTop(ReadoutBox, line.Top);
	}

	/// <summary>The hover line in device-independent pixels; null when the cursor is outside the data area.</summary>
	private static OverlayLine? ProjectOverlay(double cursorPixelX, PixelRect dataRect, double renderScale)
	{
		if (renderScale <= 0.0 || cursorPixelX < dataRect.Left || cursorPixelX > dataRect.Right)
		{
			return null;
		}

		return new OverlayLine(cursorPixelX / renderScale, dataRect.Top / renderScale, dataRect.Bottom / renderScale);
	}

	private void HideCursorOverlay()
	{
		CrosshairLine.IsVisible = false;
		ReadoutBox.IsVisible = false;
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

	private readonly record struct ChartDrag(TrendViewModel ViewModel, IPointer Pointer, Point Origin, bool IsStarted);

	private readonly record struct OverlayLine(double X, double Top, double Bottom);
}
