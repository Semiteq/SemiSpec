using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Spectrum;

using Xunit;

using static SemiSpec.Tests.Unit.UI.Spectrum.SpectrumSamples;

namespace SemiSpec.Tests.Unit.UI.Spectrum;

/// <summary>The scale while a historical spectrum is shown and on the way back to live.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Spectrum")]
[Trait("Category", "Unit")]
public sealed class SpectrumChartViewModelHistoricalScaleTests : SpectrumChartViewModelScaleTestBase
{
	[AvaloniaFact]
	public void AHistoricalSpectrumDoesNotWriteLastNonDark()
	{
		var livePeak = Atp2000Peak;
		_chart.Show(livePeak);
		_chart.Show(Historical(TallPeak(Spectrometer.Atp2000)));

		_chart.Show(Dark(Spectrometer.Atp2000));

		ShouldFollow(livePeak, default);
	}

	[AvaloniaFact]
	public void AToggleWhileAHistoricalSpectrumIsShownDoesNotWriteLastNonDark()
	{
		var livePeak = Atp2000Peak;
		var display = new SpectrumDisplay(IsInverted: true, IsLogarithmic: false);
		_chart.Show(livePeak);
		_chart.Show(Historical(TallPeak(Spectrometer.Atp2000)));
		_chart.SetDisplay(display);

		_chart.Show(Dark(Spectrometer.Atp2000));

		ShouldFollow(livePeak, display);
	}

	[AvaloniaFact]
	public void AHistoricalDarkSpectrumUsesTheLiveReferenceOfItsDevice()
	{
		var livePeak = Atp2000Peak;
		_chart.Show(livePeak);

		_chart.Show(Historical(Dark(Spectrometer.Atp2000)));

		ShouldFollow(livePeak, default);
	}

	[AvaloniaFact]
	public void AHistoricalSpectrumOfTheFrozenDeviceKeepsTheFrozenLimits()
	{
		_chart.Show(Atp2000Peak);
		_chart.Plot.Axes.SetLimits(500, 600, 0, 100);
		_chart.TakeManualScale();

		_chart.Show(Historical(TallPeak(Spectrometer.Atp2000)));
		_chart.Show(Atp2000Peak);

		_chart.IsFollowing.Should().BeFalse("live returns on the device the scale was frozen on");
		ShouldHaveLimits(500, 600, 0, 100);
	}

	[AvaloniaFact]
	public void AHistoricalSpectrumOfTheOtherDeviceReturnsToFollowing()
	{
		_chart.Show(Atp2000Peak);
		_chart.Plot.Axes.SetLimits(500, 600, 0, 100);
		_chart.TakeManualScale();

		_chart.Show(Historical(Atp8000Peak));

		_chart.IsFollowing.Should().BeTrue();
		_chart.ShouldSpanTheDeviceRange(Spectrometer.Atp8000);
	}

	[AvaloniaFact]
	public void BackToLiveOnTheOtherDeviceReturnsToFollowing()
	{
		_chart.Show(Atp2000Peak);
		_chart.Plot.Axes.SetLimits(500, 600, 0, 100);
		_chart.TakeManualScale();
		_chart.Show(Historical(Atp2000Peak));

		_chart.Show(Atp8000Peak);

		_chart.IsFollowing.Should().BeTrue();
		_chart.ShouldSpanTheDeviceRange(Spectrometer.Atp8000);
	}
}
