using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SpectrumVisualizer.UI.MainWindow;

public partial class MainWindow : Window
{
	/// <summary>
	/// Holds the connection row, the status bar and the signal pane plus the chart floor; MainWindowTextFitTests
	/// measures it.
	/// </summary>
	public const double MinimumWidth = 1256;

	public const double OpeningWidth = 1280;

	/// <summary>
	/// Holds the menu, the connection row, both chart floors, the overview, the message panel at its cap and the status
	/// bar inside a maximised 1366 x 768 client (689 px); MainWindowChartHeightTests measures it.
	/// </summary>
	public const double MinimumHeight = 688;

	/// <summary>The spectrum plot's 67 px of axis margins over a data area of at least 120 px.</summary>
	public const double SpectrumRowMinHeight = 192;

	/// <summary>The pane's 1 px border and the trend plot's 83 px of axis margins over a 120 px data area.</summary>
	public const double TrendRowMinHeight = 208;

	private IDisposable? _exitSubscription;

	public MainWindow()
	{
		InitializeComponent();
	}

	/// <summary>
	/// A screen smaller than the opening size gets a window inside its work area, never below the minimum.
	/// </summary>
	protected override void OnOpened(EventArgs e)
	{
		base.OnOpened(e);

		if (Screens.ScreenFromWindow(this) is { } screen)
		{
			FitInto(screen.WorkingArea.ToRect(screen.Scaling), screen.Scaling);
		}
	}

	protected override void OnLoaded(RoutedEventArgs e)
	{
		base.OnLoaded(e);

		if (DataContext is MainWindowViewModel viewModel)
		{
			_exitSubscription = viewModel.ExitRequests.Subscribe(_ => Close());
		}
	}

	protected override void OnUnloaded(RoutedEventArgs e)
	{
		_exitSubscription?.Dispose();
		_exitSubscription = null;

		base.OnUnloaded(e);
	}

	private void FitInto(Rect workArea, double scaling)
	{
		var frame = FrameSize ?? ClientSize;
		var frameWidth = frame.Width - ClientSize.Width;
		var frameHeight = frame.Height - ClientSize.Height;
		var width = Math.Max(MinWidth, Math.Min(Width, workArea.Width - frameWidth));
		var height = Math.Max(MinHeight, Math.Min(Height, workArea.Height - frameHeight));
		if (width.Equals(Width) && height.Equals(Height))
		{
			return;
		}

		Width = width;
		Height = height;
		var left = workArea.X + Math.Max(0.0, (workArea.Width - width - frameWidth) / 2);
		var top = workArea.Y + Math.Max(0.0, (workArea.Height - height - frameHeight) / 2);
		Position = PixelPoint.FromPoint(new Point(left, top), scaling);
	}
}
