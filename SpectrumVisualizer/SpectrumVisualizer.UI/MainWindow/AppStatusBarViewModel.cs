using SpectrumVisualizer.UI.Localization;

namespace SpectrumVisualizer.UI.MainWindow;

/// <summary>What the status bar shows: the link state, fixed at not connected until a serial link exists.</summary>
public sealed class AppStatusBarViewModel
{
	public string ConnectionText => Resources.StatusNotConnected;
}
