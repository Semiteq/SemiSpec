using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

using AwesomeAssertions;

using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.UI.Trend;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Trend;

/// <summary>The realised overview strip: the band, the window highlight, the hover line and the press navigation.</summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class OverviewViewTests : TrendChartTestBase
{
	private const int WindowWidth = 900;
	private const double StripHeight = 36.0;
	private const double MinimumHighlightWidth = 6.0;

	private readonly OverviewViewModel _overview;
	private readonly OverviewView _view;
	private readonly Window _window;

	public OverviewViewTests()
	{
		_overview = NewOverview();
		_view = new OverviewView { DataContext = _overview };
		_window = new Window { Width = WindowWidth, Height = 200, Content = _view };
	}

	private Canvas Strip => _view.FindControl<Canvas>("StripCanvas")!;

	private Border Highlight => _view.FindControl<Border>("WindowHighlight")!;

	private Border HoverLine => _view.FindControl<Border>("HoverLine")!;

	private OverviewBand BandLayer => _view.FindControl<OverviewBand>("BandLayer")!;

	[AvaloniaFact]
	public void TheStripIsThirtySixPixelsHighAndEmptyBeforeTheFirstSpectrum()
	{
		Realise();

		Strip.Bounds.Height.Should().Be(StripHeight);
		Strip.Bounds.Width.Should().Be(WindowWidth);
		Highlight.IsVisible.Should().BeFalse();
		BandLayer.Figures.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void TheBandSpansTheStripAndTheHighlightShowsTheWindow()
	{
		Realise();
		AddSpectra(0, 1800);
		Sample();
		Refold();

		BandLayer.Figures.Should().ContainSingle();
		BandLayer.Figures[0].Outline.Max(point => point.X).Should().BeApproximately(
			WindowWidth, (double)WindowWidth / OverviewViewModel.BandColumns, "the last column starts within a column of the end");
		BandLayer.Bounds.Size.Should().Be(Strip.Bounds.Size);
		Highlight.IsVisible.Should().BeTrue();
		Canvas.GetLeft(Highlight).Should().BeApproximately(WindowWidth * 2.0 / 3.0, 10.0);
		Highlight.Width.Should().BeApproximately(WindowWidth / 3.0, 10.0);
		Highlight.Height.Should().Be(StripHeight);
	}

	[AvaloniaFact]
	public void ANarrowWindowOverALongExtentKeepsSixPixelsInsideTheStrip()
	{
		Realise();
		AddFlat(0, 100);
		AddFlat(TimeSpan.FromHours(23).TotalSeconds, 200);
		Sample();
		Chart.SetWidth(TimeSpan.FromMinutes(1));
		Refold();

		Highlight.Width.Should().Be(MinimumHighlightWidth);
		(Canvas.GetLeft(Highlight) + Highlight.Width).Should().BeLessThanOrEqualTo(WindowWidth);
	}

	[AvaloniaFact]
	public void APressNavigatesTheTrendToThatTimeAndStopsFollowing()
	{
		Realise();
		AddSpectra(0, 1800);
		Sample();
		Refold();
		var middle = new Point(WindowWidth / 2.0, StripHeight / 2.0);
		var target = OverviewGeometry.TimeAtFraction(_overview.ExtentFirst, _overview.ExtentLast, 0.5);

		_window.MouseDown(InWindow(middle), MouseButton.Left);
		_window.MouseUp(InWindow(middle), MouseButton.Left);
		Dispatcher.UIThread.RunJobs();

		(Chart.From + (Chart.Width / 2)).Should().BeCloseTo(target, TimeSpan.FromMilliseconds(1));
		Chart.IsFollowing.Should().BeFalse();
	}

	[AvaloniaFact]
	public void ADragAcrossTheStripMovesTheTrendWindowWithThePointer()
	{
		Realise();
		AddSpectra(0, 1800);
		Sample();
		Refold();
		var quarter = new Point(WindowWidth / 4.0, StripHeight / 2.0);
		var half = new Point(WindowWidth / 2.0, StripHeight / 2.0);

		_window.MouseDown(InWindow(quarter), MouseButton.Left);
		var fromAtQuarter = Chart.From;
		_window.MouseMove(InWindow(half));
		_window.MouseUp(InWindow(half), MouseButton.Left);
		Dispatcher.UIThread.RunJobs();

		Chart.From.Should().BeAfter(fromAtQuarter);
		Chart.IsFollowing.Should().BeFalse();
	}

	[AvaloniaFact]
	public void TheHoverLineFollowsThePointerAndHidesOnExit()
	{
		Realise();
		AddSpectra(0, 60);
		Sample();
		Refold();
		var inside = new Point(300.0, StripHeight / 2.0);

		_window.MouseMove(InWindow(inside));

		HoverLine.IsVisible.Should().BeTrue();
		Canvas.GetLeft(HoverLine).Should().Be(inside.X);
		HoverLine.Height.Should().Be(StripHeight);

		_window.MouseMove(new Point(inside.X, 0.0));

		HoverLine.IsVisible.Should().BeFalse();
	}

	protected override void Dispose(bool disposing)
	{
		_window.Close();
		_overview.Dispose();
		base.Dispose(disposing);
	}

	private void Realise()
	{
		_window.Show();
		Dispatcher.UIThread.RunJobs();
	}

	/// <summary>Lets the band refold on its 2 s cadence and the strip lay the result out.</summary>
	private void Refold()
	{
		Scheduler.AdvanceBy(TimeSpan.FromSeconds(2).Ticks);
		Dispatcher.UIThread.RunJobs();
		AvaloniaHeadlessPlatform.ForceRenderTimerTick();
		Dispatcher.UIThread.RunJobs();
	}

	private Point InWindow(Point stripPoint)
	{
		return Strip.TranslatePoint(stripPoint, _window)
			?? throw new InvalidOperationException("The strip is not in the window.");
	}
}
