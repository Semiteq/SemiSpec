using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

using AwesomeAssertions;

using ScottPlot.Plottables;

using SemiSpec.UI.Localization;
using SemiSpec.UI.Spectrum;

using Xunit;

using BitmapCache = Avalonia.Media.BitmapCache;
using PlotColor = ScottPlot.Color;

namespace SemiSpec.Tests.Unit.UI.Spectrum;

/// <summary>The realised view: cache layout, frame pacing, empty state, palette and data context.</summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Spectrum")]
[Trait("Category", "Unit")]
public sealed class SpectrumChartViewTests : SpectrumChartViewTestBase
{
	[AvaloniaFact]
	public void ThePlotIsCachedThroughItsOnlyAncestor()
	{
		Realise();

		var layer = View.FindControl<Decorator>("PlotLayer");

		layer.Should().NotBeNull();
		layer!.CacheMode.Should().BeOfType<BitmapCache>();
		layer.Child.Should().BeSameAs(PlotControl);
		PlotControl.CacheMode.Should().BeNull();
	}

	[AvaloniaFact]
	public void ABurstOfRedrawRequestsJoinsOneFrame()
	{
		Realise();
		DriveOneFrame();
		var framesBefore = View.ServedFrameCount;

		for (var request = 0; request < 10; request++)
		{
			Chart.Show(request % 2 == 0 ? SpectrumSamples.Atp8000Peak : SpectrumSamples.Atp2000Peak);
		}

		DriveOneFrame();

		View.ServedFrameCount.Should().Be(framesBefore + 1, "the requests before one frame join it");

		DriveOneFrame();

		View.ServedFrameCount.Should().Be(framesBefore + 1, "a frame nothing requested serves nothing");
	}

	[AvaloniaFact]
	public void ARequestOnADetachedViewServesNothing()
	{
		var detached = new SpectrumChartView { DataContext = Chart };
		var host = new Window { Width = WindowWidth, Height = WindowHeight };
		try
		{
			host.Show();
			Chart.Show(SpectrumSamples.Atp8000Peak);
			DriveOneFrame();

			detached.ServedFrameCount.Should().Be(0, "a view outside a window requests no frame");

			host.Content = detached;
			DriveOneFrame();
			var framesAfterAttaching = detached.ServedFrameCount;
			Chart.Show(SpectrumSamples.Atp2000Peak);
			DriveOneFrame();

			framesAfterAttaching.Should().Be(1, "the view requests its first frame when it loads");
			detached.ServedFrameCount.Should().Be(2, "the request on the detached view left no frame pending");
		}
		finally
		{
			host.Close();
		}
	}

	[AvaloniaFact]
	public void TheEmptyStateShowsUntilTheFirstSpectrum()
	{
		Realise();
		var message = View.FindControl<TextBlock>("EmptyStateMessage")!;

		message.IsVisible.Should().BeTrue();
		message.Text.Should().Be(Resources.SpectrumEmpty);

		Chart.Show(SpectrumSamples.Atp8000Peak);
		Dispatcher.UIThread.RunJobs();

		message.IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public void ALoadedViewRepaintsThePlotWhenTheVariantChanges()
	{
		var application = Application.Current!;
		var previous = application.RequestedThemeVariant;
		try
		{
			application.RequestedThemeVariant = ThemeVariant.Light;
			Realise();
			var light = Chart.Plot.FigureBackground.Color;

			application.RequestedThemeVariant = ThemeVariant.Dark;
			Dispatcher.UIThread.RunJobs();

			light.Should().Be(Expected("AppPanelBackgroundBrush", ThemeVariant.Light));
			Chart.Plot.FigureBackground.Color.Should().Be(Expected("AppPanelBackgroundBrush", ThemeVariant.Dark));
			CentreLine().Color.Should().Be(Expected("AppMarkerLineBrush", ThemeVariant.Dark));
		}
		finally
		{
			application.RequestedThemeVariant = previous;
		}
	}

	[AvaloniaFact]
	public void AnUnloadedViewIgnoresAVariantChange()
	{
		var application = Application.Current!;
		var previous = application.RequestedThemeVariant;
		try
		{
			application.RequestedThemeVariant = ThemeVariant.Light;
			Realise();
			Window.Content = null;
			Dispatcher.UIThread.RunJobs();

			application.RequestedThemeVariant = ThemeVariant.Dark;
			Dispatcher.UIThread.RunJobs();

			Chart.Plot.FigureBackground.Color.Should().Be(Expected("AppPanelBackgroundBrush", ThemeVariant.Light));
		}
		finally
		{
			application.RequestedThemeVariant = previous;
		}
	}

	[AvaloniaFact]
	public void ANullDataContextAndBackKeepsThePlottables()
	{
		Realise();
		var plottables = Chart.Plot.GetPlottables().ToList();

		View.DataContext = null;
		View.DataContext = Chart;
		Dispatcher.UIThread.RunJobs();

		PlotControl.Plot.Should().BeSameAs(Chart.Plot);
		Chart.Plot.GetPlottables().Should().Equal(plottables);
	}

	[AvaloniaFact]
	public void ADataContextSwitchEndsTheOldRedrawSubscription()
	{
		using var other = new SpectrumChartViewModel();
		Realise();

		View.DataContext = other;
		DriveOneFrame();
		var framesBefore = View.ServedFrameCount;
		Chart.Show(SpectrumSamples.Atp8000Peak);
		DriveOneFrame();

		PlotControl.Plot.Should().BeSameAs(other.Plot);
		View.ServedFrameCount.Should().Be(framesBefore, "the old view model's requests no longer reach the view");
	}

	[AvaloniaFact]
	public void DisposingTheViewModelClearsThePendingFrame()
	{
		var disposed = new SpectrumChartViewModel();
		View.DataContext = disposed;
		Realise();
		DriveOneFrame();
		var framesBefore = View.ServedFrameCount;

		disposed.Show(SpectrumSamples.Atp8000Peak);
		disposed.Dispose();
		DriveOneFrame();

		View.ServedFrameCount.Should().Be(framesBefore, "the completed requests left no frame to serve");
	}

	private static PlotColor Expected(string key, ThemeVariant variant)
	{
		Application.Current!.TryGetResource(key, variant, out var value).Should().BeTrue();
		var brush = (ISolidColorBrush)value!;
		var alpha = (byte)Math.Round(brush.Color.A * brush.Opacity);
		return new PlotColor(brush.Color.R, brush.Color.G, brush.Color.B, alpha);
	}

	private VerticalLine CentreLine()
	{
		return Chart.Plot.GetPlottables().OfType<VerticalLine>().Single();
	}
}
