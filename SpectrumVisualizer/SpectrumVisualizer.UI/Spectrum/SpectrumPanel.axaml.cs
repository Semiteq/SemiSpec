using Avalonia.Controls;

namespace SpectrumVisualizer.UI.Spectrum;

public partial class SpectrumPanel : UserControl
{
	/// <summary>Border, padding, label column, gap, field column, padding and slack; MainWindowTextFitTests measures it.</summary>
	public const double PaneWidth = 368;

	public SpectrumPanel()
	{
		InitializeComponent();
	}
}
