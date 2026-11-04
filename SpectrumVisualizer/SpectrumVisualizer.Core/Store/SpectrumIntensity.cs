using SpectrumVisualizer.Core.Calibration;
using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.Core.Store;

/// <summary>The window intensity of one spectrum: the mean of the pixels around a wavelength.</summary>
public static class SpectrumIntensity
{
	/// <summary>True when the wavelength lies inside the device's firmware table range.</summary>
	public static bool Covers(Spectrometer device, ushort nm)
	{
		var calibration = SpectrometerCalibration.For(device);
		return calibration.FirstNm <= nm && nm <= calibration.LastNm;
	}

	public static double Mean(ReadOnlySpan<ushort> pixels, PixelWindow window)
	{
		long sum = 0;
		for (var pixel = window.First; pixel <= window.Last; pixel++)
		{
			sum += pixels[pixel];
		}

		return (double)sum / (window.Last - window.First + 1);
	}

	/// <summary>The mean of the measurement's own window, or null when its device does not cover its wavelength.</summary>
	public static double? OfOwnWindow(SpectrumMeasurement measurement)
	{
		if (!Covers(measurement.Device, measurement.WavelengthNm))
		{
			return null;
		}

		var window = SpectrometerCalibration.For(measurement.Device).Window(measurement.WavelengthNm, measurement.Delta);
		return Mean(measurement.Pixels, window);
	}
}
