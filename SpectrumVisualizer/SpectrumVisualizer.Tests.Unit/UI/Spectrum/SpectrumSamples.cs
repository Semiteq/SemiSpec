using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.UI.Spectrum;

namespace SpectrumVisualizer.Tests.Unit.UI.Spectrum;

/// <summary>Displayed spectra like the emulator's: a peak of 30000 over a floor of 1000, or a dark frame.</summary>
internal static class SpectrumSamples
{
	public const ushort Floor = 1000;
	public const ushort PeakTop = 31000;

	public static readonly DateTimeOffset ReceivedAt = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

	public static DisplayedSpectrum Atp2000Peak => Peak(Spectrometer.Atp2000, 400, 25);

	public static DisplayedSpectrum Atp8000Peak => Peak(Spectrometer.Atp8000, 1550, 10);

	/// <summary>A floor of 1000 with one pixel of 31000 in the middle of the array.</summary>
	public static DisplayedSpectrum Peak(Spectrometer device, ushort wavelengthNm, byte delta)
	{
		var pixels = new ushort[device.PixelCount];
		Array.Fill(pixels, Floor);
		pixels[pixels.Length / 2] = PeakTop;
		return Of(device, wavelengthNm, delta, pixels, amplitude: 30000f, snr: 30f);
	}

	/// <summary>Every pixel, the amplitude and the SNR zero, as the board sends a dark frame.</summary>
	public static DisplayedSpectrum Dark(Spectrometer device)
	{
		return Of(device, 400, 25, new ushort[device.PixelCount], amplitude: 0f, snr: 0f);
	}

	private static DisplayedSpectrum Of(
		Spectrometer device,
		ushort wavelengthNm,
		byte delta,
		ushort[] pixels,
		float amplitude,
		float snr)
	{
		var measurement = new SpectrumMeasurement(device, wavelengthNm, delta, amplitude, snr, 0f, pixels);
		return new DisplayedSpectrum(measurement, ReceivedAt);
	}
}
