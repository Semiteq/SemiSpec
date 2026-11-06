using Avalonia;
using Avalonia.Controls;

using ScottPlot.Avalonia;

namespace SpectrumVisualizer.UI.Chart;

/// <summary>
/// A chart view's redraws: requests join into one animation frame, and the app's theme change repaints the plot and
/// the series with the theme's palette; frameServed runs after each served frame, all on the UI thread.
/// </summary>
internal sealed class ChartFrames(
	AvaPlot plotControl, Func<bool> isBound, Action<ChartColors> recolorSeries, Action frameServed)
{
	private int _frameSequence;
	private int? _pendingFrame;

	/// <summary>Animation-frame callbacks that served a redraw request since the view was built.</summary>
	public int ServedFrameCount { get; private set; }

	/// <summary>Called from the view's OnLoaded: follows the app's theme and repaints at once.</summary>
	public void Attach()
	{
		if (Application.Current is { } application)
		{
			application.ActualThemeVariantChanged += OnActualThemeVariantChanged;
		}

		Repaint();
	}

	/// <summary>Called from the view's OnUnloaded: drops the pending frame and stops following the theme.</summary>
	public void Detach()
	{
		ClearPending();

		if (Application.Current is { } application)
		{
			application.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
		}
	}

	public void Repaint()
	{
		ApplyPalette();
		Request();
	}

	/// <summary>
	/// Asks for one animation frame; requests before it is served join it, and a detached view asks none.
	/// </summary>
	public void Request()
	{
		if (_pendingFrame is not null || TopLevel.GetTopLevel(plotControl) is not { } topLevel)
		{
			return;
		}

		var frame = ++_frameSequence;
		topLevel.RequestAnimationFrame(_ => Serve(frame));
		_pendingFrame = frame;
	}

	public void ClearPending()
	{
		_pendingFrame = null;
	}

	private void Serve(int frame)
	{
		if (_pendingFrame != frame)
		{
			return;
		}

		_pendingFrame = null;

		if (!isBound() || TopLevel.GetTopLevel(plotControl) is null)
		{
			return;
		}

		plotControl.InvalidateVisual();
		frameServed();
		ServedFrameCount++;
	}

	private void OnActualThemeVariantChanged(object? sender, EventArgs eventArgs)
	{
		Repaint();
	}

	private void ApplyPalette()
	{
		if (Application.Current is not { } application)
		{
			return;
		}

		var variant = application.ActualThemeVariant;
		var plot = plotControl.Plot;
		var colors = ChartPalette.Colors(application, variant);

		// One hold of the lock, so no render sees new surfaces with old series colours; the series recolour re-enters
		// it.
		lock (plot.Sync)
		{
			ChartPalette.Apply(plot, application, variant);
			recolorSeries(colors);
		}
	}
}
