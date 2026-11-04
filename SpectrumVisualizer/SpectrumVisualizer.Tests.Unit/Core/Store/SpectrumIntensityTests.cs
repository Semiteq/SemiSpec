using AwesomeAssertions;

using SpectrumVisualizer.Core.Calibration;
using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Tests.Unit.Core.Protocol;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Store;

[Trait("Component", "Core")]
[Trait("Area", "Store")]
[Trait("Category", "Unit")]
public sealed class SpectrumIntensityTests
{
	[Theory]
	[InlineData(Spectrometer.Atp2000, 189, true)]
	[InlineData(Spectrometer.Atp2000, 1028, true)]
	[InlineData(Spectrometer.Atp2000, 188, false)]
	[InlineData(Spectrometer.Atp2000, 1029, false)]
	[InlineData(Spectrometer.Atp8000, 898, true)]
	[InlineData(Spectrometer.Atp8000, 2461, true)]
	[InlineData(Spectrometer.Atp8000, 897, false)]
	[InlineData(Spectrometer.Atp8000, 2462, false)]
	public void CoversFollowsTheFirmwareTableRange(Spectrometer device, ushort nm, bool expected)
	{
		SpectrumIntensity.Covers(device, nm).Should().Be(expected);
	}

	[Fact]
	public void MeanAveragesTheInclusiveWindow()
	{
		ushort[] pixels = [100, 1, 2, 3, 6, 100];

		SpectrumIntensity.Mean(pixels, new PixelWindow(1, 4)).Should().Be(3.0);
	}

	[Fact]
	public void ADarkSpectrumHasMeanZero()
	{
		var window = SpectrometerCalibration.For(Spectrometer.Atp2000).Window(650, 10);

		SpectrumIntensity.Mean(new ushort[Spectrometer.Atp2000.PixelCount], window).Should().Be(0.0);
	}

	[Fact]
	public void ADeltaOfZeroTakesOnePixel()
	{
		var calibration = SpectrometerCalibration.For(Spectrometer.Atp2000);
		var window = calibration.Window(650, 0);
		var pixels = ProtocolSamples.Ramp(Spectrometer.Atp2000);

		window.First.Should().Be(window.Last);
		SpectrumIntensity.Mean(pixels, window).Should().Be(pixels[calibration.PixelFor(650)]);
	}

	[Fact]
	public void TheOwnWindowMeanIsNullWhenTheDeviceDoesNotCoverItsWavelength()
	{
		var frame = StoreSamples.Spectrum(StoreSamples.Start, Spectrometer.Atp2000, nm: 1310);

		SpectrumIntensity.OfOwnWindow(frame.Frame.Measurement!).Should().BeNull();
	}
}
