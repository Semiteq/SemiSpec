namespace SemiSpec.Tests.Unit.UI.MainWindow;

/// <summary>
/// The connection row and the status bar texts fit on one line. The headless text stub gives no real width, so a
/// row's width is its realised chrome plus its texts measured in real faces (<see cref="RenderedText"/>).
/// </summary>
public abstract class MainWindowTextFitTestBase : MainWindowLayoutTestBase
{
	// A day of ATP-8000 spectra at 1.3 per second is about 112 000, so six digits cover a day with every spectrum lost
	// or dropped.
	protected const long WidestLostCount = 999_999;

	// The widest G6 float: sign, six significant digits, a two-digit negative exponent.
	protected const float WidestBoardValue = -1.23457E-38f;
	protected const string WidestUpdateTime = "23:59:59.999";

	protected static double RoundUpTo8(double width)
	{
		return Math.Ceiling(width / 8) * 8;
	}
}
