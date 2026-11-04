using AwesomeAssertions;

using SpectrumVisualizer.Core.Calibration;
using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Tools.BoardEmulator;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Tools;

[Trait("Component", "Tools")]
[Trait("Area", "Emulator")]
[Trait("Category", "Unit")]
public sealed class SpectrumGeneratorTests
{
	[Theory]
	[InlineData(Spectrometer.Atp2000, 2048)]
	[InlineData(Spectrometer.Atp8000, 512)]
	public void TheSpectrumHasThePixelCountOfItsDevice(Spectrometer device, int pixelCount)
	{
		var measurement = SpectrumGenerator.Generate(device, EmulatorOptions.DefaultWavelength(device), 10);

		measurement.Device.Should().Be(device);
		measurement.Pixels.Should().HaveCount(pixelCount);
	}

	[Theory]
	[InlineData(Spectrometer.Atp2000, 650, 10)]
	[InlineData(Spectrometer.Atp2000, 300, 1)]
	[InlineData(Spectrometer.Atp8000, 1550, 10)]
	[InlineData(Spectrometer.Atp8000, 2000, 1)]
	public void ThePeakSitsAtTheCalibratedPixelOfTheWavelength(Spectrometer device, ushort nm, byte delta)
	{
		var measurement = SpectrumGenerator.Generate(device, nm, delta);

		var pixels = measurement.Pixels;
		var peakPixel = Array.IndexOf(pixels, pixels.Max());
		peakPixel.Should().Be(SpectrometerCalibration.For(device).PixelFor(nm));
	}

	[Fact]
	public void PixelsFarFromThePeakSitOnTheFloor()
	{
		var measurement = SpectrumGenerator.Generate(Spectrometer.Atp2000, 650, 10);

		measurement.Pixels[0].Should().Be(1000);
		measurement.Pixels[^1].Should().Be(1000);
	}

	[Fact]
	public void TheMeasurementCarriesTheWavelengthAndDelta()
	{
		var measurement = SpectrumGenerator.Generate(Spectrometer.Atp8000, 1310, 7);

		measurement.WavelengthNm.Should().Be(1310);
		measurement.Delta.Should().Be(7);
		measurement.Amplitude.Should().Be(30000);
	}
}
