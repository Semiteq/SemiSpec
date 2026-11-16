using FluentResults;

using SemiSpec.Core.Protocol;

namespace SemiSpec.Core.Store;

/// <summary>
/// The window pixels of the spectrum fixed at a normalize press, its device, and the factor, 100 or 1000, the normalized
/// mean is scaled by; the pixel array is never written.
/// </summary>
public sealed record NormalizationReference(Spectrometer Device, ushort[] Pixels, int Factor)
{
	/// <summary>
	/// The measurement's window pixels at the windows; fails when its device does not cover them or every pixel is 0.
	/// </summary>
	public static Result<NormalizationReference> From(QueryWindows windows, SpectrumMeasurement measurement, int factor)
	{
		if (!windows.Covers(measurement.Device))
		{
			return Result.Fail(new NormalizationUnavailableError());
		}

		var window = windows.For(measurement.Device);
		var pixels = measurement.Pixels.AsSpan(window.First, window.Last - window.First + 1).ToArray();
		return Array.TrueForAll(pixels, pixel => pixel == 0)
			? Result.Fail(new DarkReferenceError())
			: new NormalizationReference(measurement.Device, pixels, factor);
	}
}

/// <summary>No spectrum at the current key is at hand, or its spectrometer does not cover the wavelength.</summary>
public sealed class NormalizationUnavailableError()
	: Error("No spectrum at the current wavelength is at hand to normalize to.");

/// <summary>Every pixel of the reference window is 0, so no point can be divided by it.</summary>
public sealed class DarkReferenceError()
	: Error("Every value in the measurement window is 0, so the trend cannot be normalized to it.");
