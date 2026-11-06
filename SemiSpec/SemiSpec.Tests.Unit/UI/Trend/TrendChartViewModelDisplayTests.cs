using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using ScottPlot;
using ScottPlot.Plottables;

using SemiSpec.Core.Protocol;
using SemiSpec.Tests.Unit.Core.Store;
using SemiSpec.UI.Chart;
using SemiSpec.UI.Localization;
using SemiSpec.UI.Trend;

using Xunit;

using static SemiSpec.Tests.Unit.Core.Store.StoreSamples;

namespace SemiSpec.Tests.Unit.UI.Trend;

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

		AddSpectra(0, 1);
		Sample();

		Chart.Plot.Axes.Left.Label.Text.Should().Be(Resources.FormatTrendAxisIntensityFormat(SampleNm, SampleDelta));
		Chart.Plot.Axes.Bottom.Label.Text.Should().StartWith(
			Resources.FormatTrendAxisTimeFormat(string.Empty),
			"the date moves from the ticks into the title");
	}

	[AvaloniaFact]
	public void FromZeroRunsYFromZeroToTheMaximumPlusFivePercent()
	{
		AddFlat(0, 100);
		AddFlat(1, 200);
		Sample();

		var limits = Chart.Plot.Axes.GetLimits();
		limits.Bottom.Should().Be(0.0, "From zero is the trend's default");
		limits.Top.Should().BeApproximately(210.0, Tolerance);
	}

	[AvaloniaFact]
	public void TheYLimitsPadTheVisibleEnvelope()
	{
		Chart.SetFromZero(false);
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
		Chart.SetFromZero(false);
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

	[AvaloniaTheory]
	[InlineData(BoardState.Atp8000MeasurementFailed, false)]
	[InlineData(BoardState.Atp2000MeasurementFailed, true)]
	public void OnlyTheFailedBitOfTheMeasuringDeviceDrawsTheStrip(BoardState failedBit, bool marks)
	{
		for (var second = 0; second < 60; second++)
		{
			var spectrum = StoreSamples.Spectrum(At(second), Spectrometer.Atp2000, nm: 650, seq: NextSeq());
			Add(spectrum with { Frame = spectrum.Frame with { State = spectrum.Frame.State | failedBit } });
		}

		Sample();
		Chart.ApplyPalette(Palette(strip: Colors.Red));
		Chart.SetDataAreaWidth(256);

		using var image = Chart.Plot.GetImage(800, 400);
		var pixels = image.GetArrayRGB();
		var stripRow = (int)Math.Floor(Chart.Plot.RenderManager.LastRender.Layout.DataRect.Bottom) - 2;
		StrokeProbe.IsRed(pixels, stripRow, PixelColumnAt(30)).Should().Be(marks);
		Chart.ReadoutAt(At(30))!.Split('\n').Any(line => line.StartsWith(
			Resources.FormatTrendReadoutFailedFormat(string.Empty), StringComparison.Ordinal))
			.Should().Be(marks, "the readout names a failure only where the strip shows one");
	}

	[AvaloniaFact]
	public void SpectraFifteenSecondsApartWithHeartbeatsBetweenDrawAMarkEach()
	{
		const int Spectra = 20;
		for (var spectrum = 0; spectrum < Spectra; spectrum++)
		{
			var second = spectrum * 15.0;
			AddFlat(second, (ushort)(100 + (10 * spectrum)));
			for (var beat = 1; beat < 5; beat++)
			{
				AddHeartbeat(second + (3 * beat));
			}
		}

		Sample();
		Chart.ApplyPalette(Palette(strip: Colors.Blue, envelope: Colors.Red));
		Chart.SetDataAreaWidth(256);

		using var image = Chart.Plot.GetImage(800, 400);
		var pixels = image.GetArrayRGB();
		var dataRect = Chart.Plot.RenderManager.LastRender.Layout.DataRect;
		for (var spectrum = 0; spectrum < Spectra; spectrum++)
		{
			var x = PixelColumnAt(spectrum * 15.0);
			Enumerable.Range(x - 2, 5).Any(column => StrokeProbe.ColumnCarriesRed(pixels, dataRect, column))
				.Should().BeTrue($"the spectrum at {spectrum * 15} s stands alone between NoSpectra breaks");
		}

		StrokeProbe.ColumnCarriesRed(pixels, dataRect, PixelColumnAt(7.5))
			.Should().BeFalse("a NoSpectra break still cuts the line between two spectra");
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

	private static ChartColors Palette(Color strip, Color? envelope = null)
	{
		return new ChartColors(Colors.Blue, Colors.Blue, Colors.Green, envelope ?? Colors.Blue, Colors.Green, strip);
	}

	private int PixelColumnAt(double second)
	{
		return (int)Math.Round(Chart.Plot.GetPixel(new Coordinates(LocalTimeAxis.ToAxis(At(second)), 0)).X);
	}
}
