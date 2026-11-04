using AwesomeAssertions;

using SpectrumVisualizer.Core.Calibration;
using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.UI.Spectrum;

namespace SpectrumVisualizer.Tests.Unit.UI.Spectrum;

internal static class SpectrumChartAssertions
{
	/// <summary>The X limits run from the device's first to its last pixel wavelength.</summary>
	public static void ShouldSpanTheDeviceRange(this SpectrumChartViewModel chart, Spectrometer device)
	{
		var calibration = SpectrometerCalibration.For(device);
		var limits = chart.Plot.Axes.GetLimits();
		limits.Left.Should().Be(calibration.WavelengthOf(0));
		limits.Right.Should().Be(calibration.WavelengthOf(device.PixelCount - 1));
	}
}
