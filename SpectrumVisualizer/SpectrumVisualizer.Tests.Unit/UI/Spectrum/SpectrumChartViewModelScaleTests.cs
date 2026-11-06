using Avalonia;
using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using ScottPlot.Plottables;

using SpectrumVisualizer.Core.Calibration;
using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Spectrum;
using SpectrumVisualizer.UI.Spectrum;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.UI.Spectrum.SpectrumSamples;

namespace SpectrumVisualizer.Tests.Unit.UI.Spectrum;

/// <summary>The follow and frozen scale: manual scale, Reset scale, Stick to zero, toggles, dark frames.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Spectrum")]
[Trait("Category", "Unit")]
public sealed class SpectrumChartViewModelScaleTests : IDisposable
{
	private const double Tolerance = 1e-9;
	private const double LogStuckTop = 4.5705429398818973;

	private readonly SpectrumChartViewModel _chart = new();

	public void Dispose()
	{
		_chart.Dispose();
	}

	[AvaloniaFact]
	public void ASpectrumWithoutAManualScaleKeepsFollowing()
	{
		_chart.Show(Atp8000Peak);
		_chart.Show(Atp2000Peak);

		_chart.IsFollowing.Should().BeTrue();
		_chart.ShouldSpanTheDeviceRange(Spectrometer.Atp2000);
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
	public void AManualScaleBeforeTheFirstSpectrumDoesNothing()
	{
		_chart.TakeManualScale();

		_chart.IsFollowing.Should().BeTrue();
		_chart.Show(Atp8000Peak);
		_chart.ShouldSpanTheDeviceRange(Spectrometer.Atp8000);
	}

	[AvaloniaTheory]
	[InlineData(0.0, 0.0, true, true)]
	[InlineData(4.9, 0.0, true, true)]
	[InlineData(5.0, 0.0, true, false)]
	[InlineData(0.0, -5.0, true, false)]
	[InlineData(40.0, 40.0, false, true)]
	public void AHeldMoveFreezesTheScaleFromFivePixelsAlongEitherAxis(
		double dx,
		double dy,
		bool isButtonHeld,
		bool keepsFollowing)
	{
		_chart.Show(Atp2000Peak);
		var press = new Point(100.0, 100.0);

		_chart.OnHeldMove(press, new Point(press.X + dx, press.Y + dy), isButtonHeld);

		_chart.IsFollowing.Should().Be(keepsFollowing);
	}

	[AvaloniaFact]
	public void ResetScaleReturnsToFollowing()
	{
		_chart.Show(Atp2000Peak);
		_chart.Plot.Axes.SetLimits(500, 600, 0, 100);
		_chart.TakeManualScale();

		_chart.ResetScale();

		_chart.IsFollowing.Should().BeTrue();
		_chart.ShouldSpanTheDeviceRange(Spectrometer.Atp2000);
		_chart.Plot.Axes.GetLimits().Top.Should().BeApproximately(PeakTop + 1500, Tolerance);
	}

	[AvaloniaTheory]
	[InlineData(false, PeakTop * 1.2)]
	[InlineData(true, LogStuckTop)]
	public void StickToZeroSetsTheBottomToZeroAndFreezes(bool logarithmic, double expectedTop)
	{
		_chart.SetDisplay(new SpectrumDisplay(IsInverted: false, logarithmic));
		_chart.Show(Atp2000Peak);

		_chart.StickToZero();

		_chart.IsFollowing.Should().BeFalse();
		var limits = _chart.Plot.Axes.GetLimits();
		limits.Bottom.Should().Be(0);
		limits.Top.Should().BeApproximately(expectedTop, Tolerance);
		_chart.ShouldSpanTheDeviceRange(Spectrometer.Atp2000);
	}

	[AvaloniaTheory]
	[InlineData(true, false)]
	[InlineData(false, true)]
	public void TogglingLogOrInversionReturnsToFollowing(bool inverted, bool logarithmic)
	{
		_chart.Show(Atp2000Peak);
		_chart.StickToZero();

		_chart.SetDisplay(new SpectrumDisplay(inverted, logarithmic));

		_chart.IsFollowing.Should().BeTrue();
		_chart.ShouldSpanTheDeviceRange(Spectrometer.Atp2000);
	}

	[AvaloniaTheory]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(true, true)]
	public void ToggleWhileASpectrumIsShownTakesTheFollowingLimitsOfTheNewMode(bool inverted, bool logarithmic)
	{
		var spectrum = Atp2000Peak;
		var display = new SpectrumDisplay(inverted, logarithmic);
		_chart.Show(spectrum);

		_chart.SetDisplay(display);

		ShouldFollow(spectrum, display);
		var line = _chart.Plot.GetPlottables<SignalXY>().Single(plottable => plottable.IsVisible).GetAxisLimits();
		var top = inverted ? PeakTop - Floor : PeakTop;
		line.Top.Should().BeApproximately(
			logarithmic ? Math.Log10(top) : top,
			Tolerance,
			"the line is in decades on the log axis");
	}

	[AvaloniaFact]
	public void ADarkFrameAfterALogToggleKeepsTheLogLimitsOfThePeak()
	{
		var peak = Atp2000Peak;
		var display = new SpectrumDisplay(IsInverted: false, IsLogarithmic: true);
		_chart.Show(peak);
		_chart.SetDisplay(display);

		_chart.Show(Dark(Spectrometer.Atp2000));

		ShouldFollow(peak, display);
	}

	[AvaloniaTheory]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public void AToggleDuringADarkFrameTakesTheLastNonDarkLimitsInTheNewMode(bool inverted, bool logarithmic)
	{
		var peak = Atp2000Peak;
		var display = new SpectrumDisplay(inverted, logarithmic);
		_chart.Show(peak);
		_chart.Show(Dark(Spectrometer.Atp2000));

		_chart.SetDisplay(display);

		ShouldFollow(peak, display);
	}

	[AvaloniaTheory]
	[InlineData(false, PeakTop * 1.2)]
	[InlineData(true, LogStuckTop)]
	public void StickToZeroOnADarkFrameUsesTheLastNonDarkMaximum(bool logarithmic, double expectedTop)
	{
		_chart.SetDisplay(new SpectrumDisplay(IsInverted: false, logarithmic));
		_chart.Show(Atp2000Peak);
		_chart.Show(Dark(Spectrometer.Atp2000));

		_chart.StickToZero();

		var limits = _chart.Plot.Axes.GetLimits();
		limits.Bottom.Should().Be(0);
		limits.Top.Should().BeApproximately(expectedTop, Tolerance);
		_chart.ShouldSpanTheDeviceRange(Spectrometer.Atp2000);
	}

	[AvaloniaTheory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(true, true)]
	public void ADarkFrameKeepsTheLastNonDarkYLimitsOfTheSameDeviceOnEveryDisplay(bool inverted, bool logarithmic)
	{
		_chart.SetDisplay(new SpectrumDisplay(inverted, logarithmic));
		_chart.Show(Atp8000Peak);
		var kept = _chart.Plot.Axes.GetLimits();

		_chart.Show(Dark(Spectrometer.Atp8000));

		var limits = _chart.Plot.Axes.GetLimits();
		limits.Bottom.Should().Be(kept.Bottom);
		limits.Top.Should().Be(kept.Top);
		_chart.ShouldSpanTheDeviceRange(Spectrometer.Atp8000);
	}

	[AvaloniaFact]
	public void ADarkFirstFrameAfterADeviceSwitchTakesTheFlatRange()
	{
		_chart.Show(Atp8000Peak);

		_chart.Show(Dark(Spectrometer.Atp2000));

		var limits = _chart.Plot.Axes.GetLimits();
		limits.Bottom.Should().Be(-1.0, "the other device's spectrum is no reference");
		limits.Top.Should().Be(1.0);
		_chart.ShouldSpanTheDeviceRange(Spectrometer.Atp2000);
	}

	[AvaloniaFact]
	public void ADarkFrameAfterTheOtherDeviceKeepsTheLimitsOfItsOwnDevicesLastPeak()
	{
		_chart.Show(Atp8000Peak);
		var kept = _chart.Plot.Axes.GetLimits();
		_chart.Show(Atp2000Peak);

		_chart.Show(Dark(Spectrometer.Atp8000));

		var limits = _chart.Plot.Axes.GetLimits();
		limits.Bottom.Should().Be(kept.Bottom);
		limits.Top.Should().Be(kept.Top);
		_chart.ShouldSpanTheDeviceRange(Spectrometer.Atp8000);
	}

	/// <summary>The scale follows and its Y limits are the spectrum's following limits in the given display.</summary>
	private void ShouldFollow(DisplayedSpectrum spectrum, SpectrumDisplay display)
	{
		var pixels = spectrum.Measurement.Pixels;
		var ys = new double[pixels.Length];
		SpectrumProjection.Fill(pixels, display, ys);
		var expected = SpectrumScale.Following([0.0, 1.0], ys, display);

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
