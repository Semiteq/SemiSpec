using SemiSpec.Core.Calibration;
using SemiSpec.Core.Protocol;

namespace SemiSpec.Tools.BoardEmulator;

/// <summary>A synthetic spectrum: a Gaussian peak at the wavelength over a flat floor.</summary>
public static class SpectrumGenerator
{
	private const ushort FloorLevel = 1000;
	private const ushort PeakHeight = 30000;

	public static SpectrumMeasurement Generate(Spectrometer device, ushort wavelengthNm, byte delta)
	{
		var calibration = SpectrometerCalibration.For(device);
		var sigmaNm = Math.Max((int)delta, 1) / 2.0;
		var pixels = new ushort[device.PixelCount];
		for (var pixel = 0; pixel < pixels.Length; pixel++)
		{
			var offsetNm = calibration.WavelengthOf(pixel) - wavelengthNm;
			var peak = PeakHeight * Math.Exp(-(offsetNm * offsetNm) / (2 * sigmaNm * sigmaNm));
			pixels[pixel] = (ushort)Math.Round(FloorLevel + peak);
		}

		return new SpectrumMeasurement(
			device,
			wavelengthNm,
			delta,
			amplitude: PeakHeight,
			snr: (float)PeakHeight / FloorLevel,
			q: 0f,
			pixels);
	}

	/// <summary>
	/// The board's dark measurement, sent as an ordinary spectrum: every pixel, amplitude and snr zero.
	/// </summary>
	public static SpectrumMeasurement Dark(Spectrometer device, ushort wavelengthNm, byte delta)
	{
		return new SpectrumMeasurement(
			device,
			wavelengthNm,
			delta,
			amplitude: 0f,
			snr: 0f,
			q: 0f,
			new ushort[device.PixelCount]);
	}
}
