using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

using ReactiveUI;

using SpectrumVisualizer.Core.Trend;

namespace SpectrumVisualizer.UI.Trend;

public partial class OverviewView : UserControl
{
	// Floors a sub-pixel window fraction so the highlight stays visible.
	private const double MinimumHighlightWidth = 6.0;

	private readonly CompositeDisposable _subscriptions = [];
	private bool _isDragging;

	public OverviewView()
	{
		InitializeComponent();

		StripCanvas.PointerPressed += OnPointerPressed;
		StripCanvas.PointerMoved += OnPointerMoved;
		StripCanvas.PointerReleased += OnPointerReleased;
		StripCanvas.PointerCaptureLost += OnPointerCaptureLost;
		StripCanvas.PointerExited += OnPointerExited;
		StripCanvas.GetObservable(BoundsProperty).Subscribe(_ => UpdateStrip());

		DataContextChanged += OnDataContextChanged;
	}

	private void OnDataContextChanged(object? sender, EventArgs eventArgs)
	{
		_subscriptions.Clear();
		UpdateStrip();

		if (DataContext is not OverviewViewModel viewModel)
		{
			return;
		}

		_subscriptions.Add(viewModel
			.WhenAnyValue(
				model => model.HasExtent,
				model => model.ExtentFirst,
				model => model.ExtentLast,
				model => model.Band)
			.Subscribe(_ => LayoutBand()));
		_subscriptions.Add(viewModel
			.WhenAnyValue(
				model => model.WindowStartFraction,
				model => model.WindowWidthFraction,
				model => model.HasExtent)
			.Subscribe(_ => PlaceHighlight()));
	}

	private void UpdateStrip()
	{
		LayoutBand();
		PlaceHighlight();
		HoverLine.IsVisible = false;
	}

	private void LayoutBand()
	{
		var (width, height) = (StripCanvas.Bounds.Width, StripCanvas.Bounds.Height);
		BandLayer.Width = width;
		BandLayer.Height = height;
		BandLayer.Figures = DataContext is OverviewViewModel { HasExtent: true, Band: { } band } viewModel
			? OverviewGeometry.BandFigures(band, viewModel.ExtentFirst, viewModel.ExtentLast, width, height)
			: [];
	}

	private void PlaceHighlight()
	{
		if (DataContext is not OverviewViewModel { HasExtent: true } viewModel)
		{
			WindowHighlight.IsVisible = false;
			return;
		}

		var (left, width) = OverviewGeometry.HighlightSpan(
			viewModel.WindowStartFraction,
			viewModel.WindowWidthFraction,
			StripCanvas.Bounds.Width,
			MinimumHighlightWidth);
		WindowHighlight.IsVisible = true;
		Canvas.SetLeft(WindowHighlight, left);
		WindowHighlight.Width = width;
		WindowHighlight.Height = StripCanvas.Bounds.Height;
	}

	private void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
	{
		if (!eventArgs.GetCurrentPoint(StripCanvas).Properties.IsLeftButtonPressed)
		{
			return;
		}

		_isDragging = true;
		eventArgs.Pointer.Capture(StripCanvas);
		NavigateToPointer(eventArgs);
		eventArgs.Handled = true;
	}

	private void OnPointerMoved(object? sender, PointerEventArgs eventArgs)
	{
		if (_isDragging)
		{
			NavigateToPointer(eventArgs);
		}

		PlaceHover(eventArgs.GetPosition(StripCanvas));
	}

	private void OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
	{
		if (!_isDragging)
		{
			return;
		}

		eventArgs.Pointer.Capture(null);
		_isDragging = false;
	}

	// Capture can be lost mid-drag without a PointerReleased (window deactivation).
	private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs eventArgs)
	{
		_isDragging = false;
	}

	private void OnPointerExited(object? sender, PointerEventArgs eventArgs)
	{
		if (!_isDragging)
		{
			HoverLine.IsVisible = false;
		}
	}

	private void PlaceHover(Point position)
	{
		var stripBounds = new Rect(StripCanvas.Bounds.Size);
		if (DataContext is not OverviewViewModel { HasExtent: true } || !stripBounds.Contains(position))
		{
			HoverLine.IsVisible = false;
			return;
		}

		HoverLine.IsVisible = true;
		Canvas.SetLeft(HoverLine, OverviewGeometry.SpanLeftWithin(position.X, HoverLine.Width, stripBounds.Width));
		HoverLine.Height = stripBounds.Height;
	}

	private void NavigateToPointer(PointerEventArgs eventArgs)
	{
		var width = StripCanvas.Bounds.Width;
		if (DataContext is OverviewViewModel viewModel && width > 0.0)
		{
			viewModel.NavigateToFraction(eventArgs.GetPosition(StripCanvas).X / width);
		}
	}
}
