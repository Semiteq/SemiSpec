using Avalonia;
using Avalonia.Controls;

namespace SemiSpec.UI.Spectrum;

public partial class SpectrumPanel : UserControl
{
	/// <summary>
	/// Border, padding, label column, gap, field column, padding and slack; MainWindowTextFitTests measures it.
	/// </summary>
	public const double PaneWidth = 312;

	public static readonly StyledProperty<bool> ShowQFactorProperty =
		AvaloniaProperty.Register<SpectrumPanel, bool>(nameof(ShowQFactor));

	public SpectrumPanel()
	{
		InitializeComponent();
	}

	/// <summary>Shows the Q factor row; the View menu's display choice sets it, hidden by default.</summary>
	public bool ShowQFactor
	{
		get => GetValue(ShowQFactorProperty);
		set => SetValue(ShowQFactorProperty, value);
	}
}
