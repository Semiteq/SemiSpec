using Avalonia;
using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using ScottPlot.Plottables;

using SemiSpec.Core.Calibration;
using SemiSpec.Core.Protocol;
using SemiSpec.Core.Spectrum;

using Xunit;

using static SemiSpec.Tests.Unit.UI.Spectrum.SpectrumSamples;

namespace SemiSpec.Tests.Unit.UI.Spectrum;

/// <summary>The follow and frozen scale: manual scale, Reset scale, Stick to zero, toggles.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Spectrum")]
[Trait("Category", "Unit")]
public sealed class SpectrumChartViewModelManualScaleTests : SpectrumChartViewModelScaleTestBase
{
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
}
