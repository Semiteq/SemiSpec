using AwesomeAssertions;

using SpectrumVisualizer.Core.Calibration;
using SpectrumVisualizer.Core.Protocol;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Calibration;

[Trait("Component", "Core")]
[Trait("Area", "Calibration")]
[Trait("Category", "Unit")]
public sealed class SpectrometerCalibrationTests
{
	private static readonly SpectrometerCalibration _atp2000 = SpectrometerCalibration.For(Spectrometer.Atp2000);
	private static readonly SpectrometerCalibration _atp8000 = SpectrometerCalibration.For(Spectrometer.Atp8000);

	[Fact]
	public void Atp2000PixelsEqualTheFirmwareTable()
	{
		var pixels = Enumerable.Range(0, FirmwarePixelTables.Atp2000.Length)
			.Select(i => _atp2000.PixelFor((ushort)(FirmwarePixelTables.Atp2000FirstNm + i)));

		pixels.Should().Equal(FirmwarePixelTables.Atp2000.Select(p => (int)p));
	}

	[Fact]
	public void Atp8000PixelsAreWithinOnePixelOfTheFirmwareTable()
	{
		var deviations = Enumerable.Range(0, FirmwarePixelTables.Atp8000.Length)
			.Select(i => Math.Abs(
				_atp8000.PixelFor((ushort)(FirmwarePixelTables.Atp8000FirstNm + i)) - FirmwarePixelTables.Atp8000[i]));

		deviations.Should().OnlyContain(d => d <= 1);
	}

	[Fact]
	public void TheTableRangesMatchTheFirmwareTables()
	{
		_atp2000.FirstNm.Should().Be(FirmwarePixelTables.Atp2000FirstNm);
		_atp2000.LastNm.Should().Be((ushort)(FirmwarePixelTables.Atp2000FirstNm + FirmwarePixelTables.Atp2000.Length - 1));
		_atp8000.FirstNm.Should().Be(FirmwarePixelTables.Atp8000FirstNm);
		_atp8000.LastNm.Should().Be((ushort)(FirmwarePixelTables.Atp8000FirstNm + FirmwarePixelTables.Atp8000.Length - 1));
	}

	[Theory]
	[InlineData(Spectrometer.Atp2000, 0)]
	[InlineData(Spectrometer.Atp2000, 188)]
	[InlineData(Spectrometer.Atp8000, 0)]
	[InlineData(Spectrometer.Atp8000, 897)]
	public void AWavelengthBelowTheTableClampsToItsFirstEntry(Spectrometer device, ushort nm)
	{
		var calibration = SpectrometerCalibration.For(device);

		calibration.PixelFor(nm).Should().Be(calibration.PixelFor(calibration.FirstNm));
	}

	[Theory]
	[InlineData(Spectrometer.Atp2000, 1029)]
	[InlineData(Spectrometer.Atp2000, ushort.MaxValue)]
	[InlineData(Spectrometer.Atp8000, 2462)]
	[InlineData(Spectrometer.Atp8000, ushort.MaxValue)]
	public void AWavelengthAboveTheTableClampsToItsLastEntry(Spectrometer device, ushort nm)
	{
		var calibration = SpectrometerCalibration.For(device);

		calibration.PixelFor(nm).Should().Be(calibration.PixelFor(calibration.LastNm));
	}

	[Fact]
	public void TheWindowSpansThePixelsOfBothEdgesInclusive()
	{
		var window = _atp2000.Window(500, 10);

		window.Should().Be(new PixelWindow(_atp2000.PixelFor(490), _atp2000.PixelFor(510)));
	}

	[Fact]
	public void TheWindowClampsAtTheLowEnd()
	{
		_atp2000.Window(5, 10).Should().Be(new PixelWindow(_atp2000.PixelFor(189), _atp2000.PixelFor(189)));
	}

	[Fact]
	public void TheWindowClampsAtTheHighEnd()
	{
		_atp2000.Window(1028, 5).Last.Should().Be(Spectrometer.Atp2000.PixelCount - 1);
		_atp8000.Window(ushort.MaxValue, byte.MaxValue).Last.Should().Be(_atp8000.PixelFor(_atp8000.LastNm));
	}

	[Fact]
	public void Atp2000PixelZeroIsTheConstantTerm()
	{
		_atp2000.WavelengthOf(0).Should().Be(185.89856);
	}
}
