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
}
