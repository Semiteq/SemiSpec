using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using ScottPlot.Plottables;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Spectrum;

using Xunit;

using static SemiSpec.Tests.Unit.UI.Spectrum.SpectrumSamples;

namespace SemiSpec.Tests.Unit.UI.Spectrum;

/// <summary>The following limits after a display toggle and on dark frames.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Spectrum")]
[Trait("Category", "Unit")]
public sealed class SpectrumChartViewModelDarkFrameTests : SpectrumChartViewModelScaleTestBase
{
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
	[InlineData(false)]
	[InlineData(true)]
	public void FromZeroOnADarkFrameUsesTheLastNonDarkMaximum(bool logarithmic)
	{
		_chart.SetDisplay(new SpectrumDisplay(IsInverted: false, logarithmic));
		_chart.Show(Atp2000Peak);
		_chart.Show(Dark(Spectrometer.Atp2000));

		_chart.SetFromZero(true);

		var limits = _chart.Plot.Axes.GetLimits();
		limits.Bottom.Should().Be(0);
		var top = logarithmic ? Math.Log10(PeakTop) : PeakTop;
		limits.Top.Should().BeApproximately(top * 1.05, Tolerance);
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
}
