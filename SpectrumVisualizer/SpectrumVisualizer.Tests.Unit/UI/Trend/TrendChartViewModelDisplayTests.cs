using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using ScottPlot;
using ScottPlot.Plottables;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Tests.Unit.Core.Store;
using SpectrumVisualizer.UI.Chart;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Trend;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Store.StoreSamples;

namespace SpectrumVisualizer.Tests.Unit.UI.Trend;

[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendChartViewModelDisplayTests : TrendChartTestBase
{
	private const double Tolerance = 1e-9;

	[AvaloniaFact]
	public void TheXLimitsAreTheWindowInLocalTime()
	{
		AddSpectra(0, 60);
		Sample();

		var limits = Chart.Plot.Axes.GetLimits();
		limits.Left.Should().BeApproximately(LocalTimeAxis.ToAxis(Chart.From), Tolerance);
		limits.Right.Should().BeApproximately(LocalTimeAxis.ToAxis(Chart.To), Tolerance);
	}

	[AvaloniaFact]
	public void TheYAxisLabelNamesTheKey()
	{
		Chart.Plot.Axes.Left.Label.Text.Should().Be(Resources.TrendAxisIntensity);
		Chart.Plot.Axes.Bottom.Label.Text.Should().Be(Resources.TrendAxisTime);

		AddSpectra(0, 1);
		Sample();

		Chart.Plot.Axes.Left.Label.Text.Should().Be(Resources.FormatTrendAxisIntensityFormat(SampleNm, SampleDelta));
	}

	[AvaloniaFact]
	public void TheYLimitsPadTheVisibleEnvelope()
	{
		AddFlat(0, 100);
		AddFlat(1, 200);
		Sample();

		var limits = Chart.Plot.Axes.GetLimits();
		limits.Bottom.Should().BeApproximately(95.0, Tolerance);
		limits.Top.Should().BeApproximately(205.0, Tolerance);
	}

	[AvaloniaFact]
	public void AFlatVisibleRangeTakesPlusMinusOne()
	{
		AddFlat(0, 100);
		AddFlat(1, 100);
		Sample();

		var limits = Chart.Plot.Axes.GetLimits();
		limits.Bottom.Should().BeApproximately(99.0, Tolerance);
		limits.Top.Should().BeApproximately(101.0, Tolerance);
	}

	[AvaloniaFact]
	public void AWindowWithoutValuesKeepsTheYLimits()
	{
		AddFlat(0, 100);
		AddFlat(1, 200);
		Sample();
		var kept = Chart.Plot.Axes.GetLimits();

		AddFlat(1200, 500, Spectrometer.Atp2000);
		Sample();

		var limits = Chart.Plot.Axes.GetLimits();
		limits.Bottom.Should().Be(kept.Bottom);
		limits.Top.Should().Be(kept.Top);
	}

	[AvaloniaFact]
	public void TheShownMomentDrawsAMarker()
	{
		var frame = StoreSamples.Spectrum(At(5));
		var marker = Chart.Plot.GetPlottables<VerticalLine>().Single();
		marker.IsVisible.Should().BeFalse();

		Switching.ShowMoment(frame.Frame.Measurement!, frame.ReceivedAt);

		marker.IsVisible.Should().BeTrue();
		marker.X.Should().BeApproximately(LocalTimeAxis.ToAxis(At(5)), Tolerance);

		Switching.ReturnToLive();

		marker.IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public void AFailureColumnDrawsTheStrip()
	{
		AddSpectra(0, 30);
		AddFailedHeartbeat(30.5);
		AddSpectra(31, 30);
		Sample();
		Chart.ApplyPalette(Palette(strip: Colors.Red));
		Chart.SetDataAreaWidth(256);

		using var image = Chart.Plot.GetImage(800, 400);
		var pixels = image.GetArrayRGB();
		var dataRect = Chart.Plot.RenderManager.LastRender.Layout.DataRect;
		var failedX = (int)Math.Round(Chart.Plot.GetPixel(new Coordinates(LocalTimeAxis.ToAxis(At(30.5)), 0)).X);
		var cleanX = (int)Math.Round(Chart.Plot.GetPixel(new Coordinates(LocalTimeAxis.ToAxis(At(10)), 0)).X);
		var stripRow = (int)Math.Floor(dataRect.Bottom) - 2;

		var marked = Enumerable.Range(failedX - 1, 3).Any(column => StrokeProbe.IsRed(pixels, stripRow, column));
		marked.Should().BeTrue("the failed heartbeat's column is marked");
		StrokeProbe.IsRed(pixels, stripRow, cleanX).Should().BeFalse("a column without a failure stays clear");
	}

	[AvaloniaFact]
	public void TheReadoutReadsTheColumnUnderTheMoment()
	{
		Chart.ReadoutAt(At(0)).Should().BeNull();
		AddFlat(0, 100);
		Sample();

		var readout = Chart.ReadoutAt(At(0));

		readout.Should().NotBeNull();
		readout!.Split('\n').Should().Contain(Resources.FormatTrendReadoutCountFormat("1"));
		Chart.ReadoutAt(At(1)).Should().BeNull("the window ends at the newest point");
	}

	[AvaloniaFact]
	public void ApplyPaletteColoursTheEnvelopeAndTheMarker()
	{
		var colors = Palette(strip: Colors.Red);

		Chart.ApplyPalette(colors);

		Chart.Plot.GetPlottables<VerticalLine>().Single().Color.Should().Be(colors.MomentLine);
	}

	[AvaloniaFact]
	public void AWidthPostedAfterTheDisposeDoesNothing()
	{
		AddSpectra(0, 3);
		Sample();
		Chart.Dispose();

		var postedLate = () => Chart.SetDataAreaWidth(300);

		postedLate.Should().NotThrow();
	}

	[AvaloniaFact]
	public void EveryRefreshRequestsARedraw()
	{
		var redraws = 0;
		using var subscription = Chart.RedrawRequested.Subscribe(_ => redraws++);

		AddSpectra(0, 3);
		Sample();
		Chart.SetWidth(TimeSpan.FromHours(1));

		redraws.Should().Be(2);
	}

	private static ChartColors Palette(Color strip)
	{
		return new ChartColors(Colors.Blue, Colors.Blue, Colors.Green, Colors.Blue, Colors.Green, strip);
	}
}
