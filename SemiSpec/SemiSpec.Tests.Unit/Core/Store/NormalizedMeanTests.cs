using System.Runtime.InteropServices;

using AwesomeAssertions;

using SemiSpec.Core.Calibration;
using SemiSpec.Core.Protocol;
using SemiSpec.Core.Store;

using Xunit;

using static SemiSpec.Tests.Unit.Core.Store.StoreSamples;

namespace SemiSpec.Tests.Unit.Core.Store;

[Trait("Component", "Core")]
[Trait("Area", "Store")]
[Trait("Category", "Unit")]
public sealed class NormalizedMeanTests
{
	[Fact]
	public void EachPixelIsDividedByItsOwnReferenceBeforeTheMean()
	{
		var reference = new NormalizationReference(Spectrometer.Atp8000, [100, 200], Factor: 100);

		var value = QueryWindows.NormalizedMeanOf(Bytes(100, 100), reference);

		value.Should().BeApproximately(75.0, 1e-9, "(100/100 + 100/200) / 2 x 100, not 200/300 x 100 = 66.7");
	}

	[Fact]
	public void TheFactorScalesTheMean()
	{
		var reference = new NormalizationReference(Spectrometer.Atp8000, [50, 50], Factor: 1000);

		QueryWindows.NormalizedMeanOf(Bytes(50, 100), reference).Should().BeApproximately(1500.0, 1e-9);
	}

	[Fact]
	public void AZeroReferencePixelIsLeftOut()
	{
		var reference = new NormalizationReference(Spectrometer.Atp8000, [0, 100], Factor: 100);

		QueryWindows.NormalizedMeanOf(Bytes(500, 50), reference).Should().BeApproximately(50.0, 1e-9);
	}

	[Fact]
	public void AWindowOfAnotherLengthIsNaN()
	{
		var reference = new NormalizationReference(Spectrometer.Atp8000, [100, 100], Factor: 100);

		double.IsNaN(QueryWindows.NormalizedMeanOf(Bytes(100, 100, 100), reference)).Should().BeTrue();
	}

	[Fact]
	public void WithoutAReferenceTheIntensityIsTheWindowMean()
	{
		QueryWindows.IntensityOf(Bytes(100, 200), Spectrometer.Atp8000, reference: null).Should().Be(150.0);
	}

	[Fact]
	public void AnotherDeviceThanTheReferencesIsNaN()
	{
		var reference = new NormalizationReference(Spectrometer.Atp8000, [100, 100], Factor: 100);

		double.IsNaN(QueryWindows.IntensityOf(Bytes(100, 100), Spectrometer.Atp2000, reference)).Should().BeTrue();
	}

	[Fact]
	public void TheReferenceHoldsTheMeasurementsWindowPixels()
	{
		var windows = new QueryWindows(SampleNm, SampleDelta);
		var frame = StoreSamples.Spectrum(Start).Frame.Measurement!;
		var window = windows.For(Spectrometer.Atp8000);

		var reference = NormalizationReference.From(windows, frame, factor: 100).Value;

		reference.Device.Should().Be(Spectrometer.Atp8000);
		reference.Factor.Should().Be(100);
		reference.Pixels.Should().Equal(frame.Pixels[window.First..(window.Last + 1)]);
	}

	[Fact]
	public void ADarkWindowRefusesTheReference()
	{
		var windows = new QueryWindows(SampleNm, SampleDelta);

		var reference = NormalizationReference.From(windows, Dark(Start).Frame.Measurement!, factor: 100);

		reference.Errors.Should().ContainSingle().Which.Should().BeOfType<DarkReferenceError>();
	}

	[Fact]
	public void AnUncoveredWavelengthRefusesTheReference()
	{
		var windows = new QueryWindows(SampleNm, SampleDelta);
		var calibration = SpectrometerCalibration.For(Spectrometer.Atp2000);
		calibration.LastNm.Should().BeLessThan(SampleNm, "the sample wavelength lies outside the ATP-2000 table");

		var reference = NormalizationReference.From(
			windows, StoreSamples.Spectrum(Start, Spectrometer.Atp2000).Frame.Measurement!, factor: 100);

		reference.Errors.Should().ContainSingle().Which.Should().BeOfType<NormalizationUnavailableError>();
	}

	private static byte[] Bytes(params ushort[] pixels)
	{
		return MemoryMarshal.AsBytes(pixels.AsSpan()).ToArray();
	}
}
