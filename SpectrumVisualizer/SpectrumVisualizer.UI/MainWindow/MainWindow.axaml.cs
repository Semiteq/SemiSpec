using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SpectrumVisualizer.UI.MainWindow;

public partial class MainWindow : Window
{
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
