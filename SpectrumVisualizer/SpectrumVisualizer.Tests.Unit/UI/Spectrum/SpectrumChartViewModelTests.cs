using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using ScottPlot.Plottables;
using ScottPlot.TickGenerators;

using SpectrumVisualizer.Core.Calibration;
using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.UI.Chart;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Spectrum;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.UI.Spectrum.SpectrumSamples;

using Color = ScottPlot.Color;

namespace SpectrumVisualizer.Tests.Unit.UI.Spectrum;

[Trait("Component", "UI")]
[Trait("Area", "Spectrum")]
[Trait("Category", "Unit")]
public sealed class SpectrumChartViewModelTests : IDisposable
{
	private const double Tolerance = 1e-9;

	private readonly SpectrumChartViewModel _chart = new();

	public void Dispose()
	{
		_chart.Dispose();
	}

	[AvaloniaFact]
	public void TheAxesCarryTheLocalisedLabels()
	{
		_chart.Plot.Axes.Bottom.Label.Text.Should().Be(Resources.SpectrumAxisWavelength);
		_chart.Plot.Axes.Left.Label.Text.Should().Be(Resources.SpectrumAxisIntensity);
	}

	[AvaloniaFact]
	public void TheFirstSpectrumSetsTheXLimitsToTheDeviceRange()
	{
		_chart.HasSpectrum.Should().BeFalse();

		_chart.Show(Atp8000Peak);

		_chart.HasSpectrum.Should().BeTrue();
		_chart.IsFollowing.Should().BeTrue();
		ShouldSpanTheDeviceRange(Spectrometer.Atp8000);
		var limits = _chart.Plot.Axes.GetLimits();
		limits.Bottom.Should().BeApproximately(Floor - 1500, Tolerance);
		limits.Top.Should().BeApproximately(PeakTop + 1500, Tolerance);
	}

	[AvaloniaFact]
	public void TheWindowBandSpansTheDeltaAroundTheWavelength()
	{
		_chart.Show(Peak(Spectrometer.Atp8000, 1550, 10));

		var band = Band();
		band.IsVisible.Should().BeTrue();
		band.X1.Should().Be(1540);
		band.X2.Should().Be(1560);
		CentreLine().IsVisible.Should().BeTrue();
		CentreLine().X.Should().Be(1550);
	}

	[AvaloniaFact]
	public void ADeltaOfZeroDrawsTheCentreLineOnly()
	{
		_chart.Show(Peak(Spectrometer.Atp2000, 700, 0));

		Band().IsVisible.Should().BeFalse();
		CentreLine().IsVisible.Should().BeTrue();
		CentreLine().X.Should().Be(700);
	}

	[AvaloniaFact]
	public void AnAtp2000FrameAfterAnAtp8000FrameShowsTheAtp2000LineOnly()
	{
		_chart.Show(Atp8000Peak);

		_chart.Show(Atp2000Peak);

		var visible = _chart.Plot.GetPlottables<SignalXY>().Where(line => line.IsVisible).ToList();
		visible.Should().ContainSingle();
		visible[0].GetAxisLimits().Left.Should().Be(SpectrometerCalibration.For(Spectrometer.Atp2000).WavelengthOf(0));
		ShouldSpanTheDeviceRange(Spectrometer.Atp2000);
	}

	[AvaloniaFact]
	public void ABandOutsideTheDeviceRangeIsHiddenAndTheXLimitsStayTheDeviceRange()
	{
		_chart.Show(Peak(Spectrometer.Atp8000, 400, 10));

		Band().IsVisible.Should().BeFalse();
		CentreLine().IsVisible.Should().BeFalse();
		ShouldSpanTheDeviceRange(Spectrometer.Atp8000);
	}

	[AvaloniaFact]
	public void ASpectrumWithoutAManualScaleKeepsFollowing()
	{
		_chart.Show(Atp8000Peak);
		_chart.Show(Atp2000Peak);

		_chart.IsFollowing.Should().BeTrue();
		ShouldSpanTheDeviceRange(Spectrometer.Atp2000);
	}

	[AvaloniaFact]
	public void AManualScaleFreezesTheLimitsAcrossLaterFrames()
	{
		_chart.Show(Atp2000Peak);
		_chart.Plot.Axes.SetLimits(500, 600, 0, 100);

		_chart.TakeManualScale();
		_chart.Show(Peak(Spectrometer.Atp2000, 400, 25));

		_chart.IsFollowing.Should().BeFalse();
		ShouldHaveLimits(500, 600, 0, 100);
	}

	[AvaloniaFact]
	public void AFrameFromTheOtherDeviceWhileFrozenKeepsTheFrozenLimits()
	{
		_chart.Show(Atp2000Peak);
		_chart.Plot.Axes.SetLimits(500, 600, 0, 100);
		_chart.TakeManualScale();

		_chart.Show(Atp8000Peak);

		ShouldHaveLimits(500, 600, 0, 100);
		_chart.Plot.GetPlottables<SignalXY>().Where(line => line.IsVisible).Should().ContainSingle()
			.Which.GetAxisLimits().Left.Should().Be(SpectrometerCalibration.For(Spectrometer.Atp8000).WavelengthOf(0));
	}

	[AvaloniaFact]
	public void ResetScaleReturnsToFollowing()
	{
		_chart.Show(Atp2000Peak);
		_chart.Plot.Axes.SetLimits(500, 600, 0, 100);
		_chart.TakeManualScale();

		_chart.ResetScale();

		_chart.IsFollowing.Should().BeTrue();
		ShouldSpanTheDeviceRange(Spectrometer.Atp2000);
		_chart.Plot.Axes.GetLimits().Top.Should().BeApproximately(PeakTop + 1500, Tolerance);
	}

	[AvaloniaTheory]
	[InlineData(false, PeakTop * 1.2)]
	[InlineData(true, 4.5705429398818973)]
	public void StickToZeroSetsTheBottomToZeroAndFreezes(bool logarithmic, double expectedTop)
	{
		_chart.SetDisplay(inverted: false, logarithmic);
		_chart.Show(Atp2000Peak);

		_chart.StickToZero();

		_chart.IsFollowing.Should().BeFalse();
		var limits = _chart.Plot.Axes.GetLimits();
		limits.Bottom.Should().Be(0);
		limits.Top.Should().BeApproximately(expectedTop, Tolerance);
		ShouldSpanTheDeviceRange(Spectrometer.Atp2000);
	}

	[AvaloniaTheory]
	[InlineData(true, false)]
	[InlineData(false, true)]
	public void TogglingLogOrInversionReturnsToFollowing(bool inverted, bool logarithmic)
	{
		_chart.Show(Atp2000Peak);
		_chart.StickToZero();

		_chart.SetDisplay(inverted, logarithmic);

		_chart.IsFollowing.Should().BeTrue();
		ShouldSpanTheDeviceRange(Spectrometer.Atp2000);
	}

	[AvaloniaTheory]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(true, true)]
	public void ToggleWhileASpectrumIsShownTakesTheFollowingLimitsOfTheNewMode(bool inverted, bool logarithmic)
	{
		var spectrum = Atp2000Peak;
		_chart.Show(spectrum);

		_chart.SetDisplay(inverted, logarithmic);

		ShouldFollow(spectrum, inverted, logarithmic);
		var line = _chart.Plot.GetPlottables<SignalXY>().Single(plottable => plottable.IsVisible).GetAxisLimits();
		var top = inverted ? PeakTop - Floor : PeakTop;
		line.Top.Should().BeApproximately(logarithmic ? Math.Log10(top) : top, Tolerance, "the line is in decades on the log axis");
	}

	[AvaloniaFact]
	public void ADarkFrameAfterALogToggleKeepsTheLogLimitsOfThePeak()
	{
		var peak = Atp2000Peak;
		_chart.Show(peak);
		_chart.SetDisplay(inverted: false, logarithmic: true);

		_chart.Show(Dark(Spectrometer.Atp2000));

		ShouldFollow(peak, inverted: false, logarithmic: true);
	}

	[AvaloniaTheory]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public void AToggleDuringADarkFrameTakesTheLastNonDarkLimitsInTheNewMode(bool inverted, bool logarithmic)
	{
		var peak = Atp2000Peak;
		_chart.Show(peak);
		_chart.Show(Dark(Spectrometer.Atp2000));

		_chart.SetDisplay(inverted, logarithmic);

		ShouldFollow(peak, inverted, logarithmic);
	}

	[AvaloniaTheory]
	[InlineData(false, PeakTop * 1.2)]
	[InlineData(true, 4.5705429398818973)]
	public void StickToZeroOnADarkFrameUsesTheLastNonDarkMaximum(bool logarithmic, double expectedTop)
	{
		_chart.SetDisplay(inverted: false, logarithmic);
		_chart.Show(Atp2000Peak);
		_chart.Show(Dark(Spectrometer.Atp8000));

		_chart.StickToZero();

		var limits = _chart.Plot.Axes.GetLimits();
		limits.Bottom.Should().Be(0);
		limits.Top.Should().BeApproximately(expectedTop, Tolerance);
		ShouldSpanTheDeviceRange(Spectrometer.Atp8000);
	}

	[AvaloniaFact]
	public void AManualScaleBeforeTheFirstSpectrumDoesNothing()
	{
		_chart.TakeManualScale();

		_chart.IsFollowing.Should().BeTrue();
		_chart.Show(Atp8000Peak);
		ShouldSpanTheDeviceRange(Spectrometer.Atp8000);
	}

	[AvaloniaFact]
	public void TheLogAxisCarriesALogTickGeneratorAndMinorGridlinesAndTheLinearAxisNeither()
	{
		_chart.SetDisplay(inverted: false, logarithmic: true);

		_chart.Plot.Axes.Left.TickGenerator.Should().BeOfType<LogTickGenerator>();
		_chart.Plot.Grid.YAxisStyle.MinorLineStyle.Width.Should().Be(1f);

		_chart.SetDisplay(inverted: false, logarithmic: false);

		_chart.Plot.Axes.Left.TickGenerator.Should().BeOfType<NumericAutomatic>();
		_chart.Plot.Grid.YAxisStyle.MinorLineStyle.Width.Should().Be(0f);
	}

	[AvaloniaFact]
	public void TheLogAxisRunsInDecades()
	{
		_chart.SetDisplay(inverted: false, logarithmic: true);

		_chart.Show(Atp2000Peak);

		var span = Math.Log10(PeakTop) - 3.0;
		var limits = _chart.Plot.Axes.GetLimits();
		limits.Bottom.Should().BeApproximately(3.0 - (span * 0.05), Tolerance);
		limits.Top.Should().BeApproximately(Math.Log10(PeakTop) + (span * 0.05), Tolerance);
	}

	[AvaloniaFact]
	public void ADarkFrameWhileFollowingKeepsTheLastNonDarkYLimits()
	{
		_chart.Show(Atp8000Peak);
		var kept = _chart.Plot.Axes.GetLimits();

		_chart.Show(Dark(Spectrometer.Atp2000));

		var limits = _chart.Plot.Axes.GetLimits();
		limits.Bottom.Should().Be(kept.Bottom);
		limits.Top.Should().Be(kept.Top);
		ShouldSpanTheDeviceRange(Spectrometer.Atp2000);
	}

	[AvaloniaTheory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(true, true)]
	public void ADarkFrameKeepsTheLastNonDarkYLimitsOnEveryDisplay(bool inverted, bool logarithmic)
	{
		_chart.SetDisplay(inverted, logarithmic);
		_chart.Show(Atp8000Peak);
		var kept = _chart.Plot.Axes.GetLimits();

		_chart.Show(Dark(Spectrometer.Atp2000));

		var limits = _chart.Plot.Axes.GetLimits();
		limits.Bottom.Should().Be(kept.Bottom);
		limits.Top.Should().Be(kept.Top);
		ShouldSpanTheDeviceRange(Spectrometer.Atp2000);
	}

	[AvaloniaFact]
	public void ShowNeverWritesTheFramesPixels()
	{
		var spectrum = Atp2000Peak;
		var copy = spectrum.Measurement.Pixels.ToArray();
		_chart.SetDisplay(inverted: true, logarithmic: true);

		_chart.Show(spectrum);

		spectrum.Measurement.Pixels.Should().Equal(copy);
	}

	[AvaloniaFact]
	public void EveryShowRequestsOneRedrawAndDisposeCompletesTheRequests()
	{
		var chart = new SpectrumChartViewModel();
		var requests = 0;
		var completed = false;
		using var subscription = chart.RedrawRequested.Subscribe(_ => requests++, () => completed = true);

		chart.Show(Atp2000Peak);
		chart.Show(Atp8000Peak);
		chart.Dispose();

		requests.Should().Be(2);
		completed.Should().BeTrue();
	}

	[AvaloniaFact]
	public void ApplyPaletteColoursTheLinesTheBandAndTheCentreLine()
	{
		var colors = new ChartColors(new Color(1, 2, 3), new Color(4, 5, 6, 64), new Color(7, 8, 9));

		_chart.ApplyPalette(colors);

		_chart.Plot.GetPlottables<SignalXY>().Should().AllSatisfy(line => line.Color.Should().Be(colors.Line));
		Band().FillStyle.Color.Should().Be(colors.BandFill);
		CentreLine().Color.Should().Be(colors.CentreLine);
	}

	[AvaloniaFact]
	public async Task ApplyPaletteRunsUnderThePlotLock()
	{
		var colors = new ChartColors(new Color(1, 2, 3), new Color(4, 5, 6, 64), new Color(7, 8, 9));
		using var held = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		var holder = Task.Run(() =>
		{
			lock (_chart.Plot.Sync)
			{
				held.Set();
				release.Wait(Waits.Limit);
			}
		});
		held.Wait(Waits.Limit).Should().BeTrue();

		var apply = Task.Run(() => _chart.ApplyPalette(colors));
		var first = await Task.WhenAny(apply, Task.Delay(TimeSpan.FromMilliseconds(200)));

		first.Should().NotBeSameAs(apply);
		release.Set();
		await apply.WaitAsync(Waits.Limit);
		await holder.WaitAsync(Waits.Limit);
		CentreLine().Color.Should().Be(colors.CentreLine);
	}

	private HorizontalSpan Band()
	{
		return _chart.Plot.GetPlottables<HorizontalSpan>().Single();
	}

	private VerticalLine CentreLine()
	{
		return _chart.Plot.GetPlottables<VerticalLine>().Single();
	}

	private void ShouldSpanTheDeviceRange(Spectrometer device)
	{
		var calibration = SpectrometerCalibration.For(device);
		var limits = _chart.Plot.Axes.GetLimits();
		limits.Left.Should().Be(calibration.WavelengthOf(0));
		limits.Right.Should().Be(calibration.WavelengthOf(device.PixelCount - 1));
	}

	/// <summary>The scale follows and its Y limits are the spectrum's following limits in the given mode.</summary>
	private void ShouldFollow(DisplayedSpectrum spectrum, bool inverted, bool logarithmic)
	{
		var pixels = spectrum.Measurement.Pixels;
		var ys = new double[pixels.Length];
		SpectrumProjection.Fill(pixels, inverted, logarithmic, ys);
		var expected = SpectrumScale.Following([0.0, 1.0], ys, logarithmic);

		_chart.IsFollowing.Should().BeTrue();
		var limits = _chart.Plot.Axes.GetLimits();
		limits.Bottom.Should().BeApproximately(expected.Y.Min, Tolerance);
		limits.Top.Should().BeApproximately(expected.Y.Max, Tolerance);
	}

	private void ShouldHaveLimits(double left, double right, double bottom, double top)
	{
		var limits = _chart.Plot.Axes.GetLimits();
		limits.Left.Should().Be(left);
		limits.Right.Should().Be(right);
		limits.Bottom.Should().Be(bottom);
		limits.Top.Should().Be(top);
	}
}
