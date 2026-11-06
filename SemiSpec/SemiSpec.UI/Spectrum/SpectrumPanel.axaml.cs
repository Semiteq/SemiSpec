using Avalonia.Controls;

namespace SemiSpec.UI.Spectrum;

public partial class SpectrumPanel : UserControl
{
	/// <summary>
	/// Border, padding, label column, gap, field column, padding and slack; MainWindowTextFitTests measures it.
	/// </summary>
	public const double PaneWidth = 392;

	public SpectrumPanel()
	{
		InitializeComponent();
	}
}
