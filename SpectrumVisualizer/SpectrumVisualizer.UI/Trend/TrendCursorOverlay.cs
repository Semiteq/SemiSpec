using Avalonia;
using Avalonia.Controls;

using ScottPlot;

using Line = Avalonia.Controls.Shapes.Line;
using PixelRect = ScottPlot.PixelRect;

namespace SpectrumVisualizer.UI.Trend;

/// <summary>The trend's hover line over the data area and the readout box beside it; runs on the UI thread.</summary>
internal sealed class TrendCursorOverlay(Canvas canvas, Line crosshairLine, Border readoutBox, TextBlock readoutText)
{
	/// <summary>
	/// Shows the line at the time with the readout; hides both outside the data area or without a readout.
	/// </summary>
	public void ShowAt(Plot plot, DateTimeOffset time, string? readout)
	{
		var cursorPixelX = plot.GetPixel(new Coordinates(LocalTimeAxis.ToAxis(time), 0.0)).X;
		if (ProjectLine(cursorPixelX, plot.LastRender.DataRect, plot.ScaleFactor) is not { } line || readout is null)
		{
			Hide();
			return;
		}

		crosshairLine.StartPoint = new Point(line.X, line.Top);
		crosshairLine.EndPoint = new Point(line.X, line.Bottom);
		crosshairLine.IsVisible = true;

		// A hidden control measures as empty, so the box shows before it measures its new text.
		readoutBox.IsVisible = true;
		readoutText.Text = readout;
		readoutBox.Measure(Size.Infinity);
		var readoutWidth = readoutBox.DesiredSize.Width;
		var fitsRightOfLine = line.X + readoutWidth <= canvas.Bounds.Width;
		Canvas.SetLeft(readoutBox, fitsRightOfLine ? line.X : Math.Max(0.0, line.X - readoutWidth));
		Canvas.SetTop(readoutBox, line.Top);
	}

	public void Hide()
	{
		crosshairLine.IsVisible = false;
		readoutBox.IsVisible = false;
	}

	/// <summary>The hover line in device-independent pixels; null when the cursor is outside the data area.</summary>
	private static OverlayLine? ProjectLine(double cursorPixelX, PixelRect dataRect, double renderScale)
	{
		if (renderScale <= 0.0 || cursorPixelX < dataRect.Left || cursorPixelX > dataRect.Right)
		{
			return null;
		}

		return new OverlayLine(cursorPixelX / renderScale, dataRect.Top / renderScale, dataRect.Bottom / renderScale);
	}

	private readonly record struct OverlayLine(double X, double Top, double Bottom);
}
