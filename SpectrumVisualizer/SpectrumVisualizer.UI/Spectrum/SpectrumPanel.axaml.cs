using Avalonia.Controls;

namespace SpectrumVisualizer.UI.Spectrum;

public partial class SpectrumPanel : UserControl
{
	/// <summary>The signal pane's outer width: padding, label column, gap, field column, padding and slack.</summary>
	public const double PaneWidth = 352;

	public SpectrumPanel()
	{
		InitializeComponent();
	}
}
