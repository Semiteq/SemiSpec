using SemiSpec.Core.Protocol;

namespace SemiSpec.Core.Calibration;

/// <summary>
/// Pixel-to-wavelength polynomial of one spectrometer and the board firmware's wavelength-to-pixel rule.
/// </summary>
public sealed class SpectrometerCalibration
{
	private static readonly SpectrometerCalibration _atp2000 = new(
		Spectrometer.Atp2000,
		[185.89856, 0.390762, 1.126E-5, -8.969E-10],
		firstNm: 189,
		lastNm: 1028);

	private static readonly SpectrometerCalibration _atp8000 = new(
		Spectrometer.Atp8000,
		[895.373169, 3.00962138, 9.568E-5, 4.519E-8],
		firstNm: 898,
		lastNm: 2461);

	private readonly double[] _wavelengths;

	private SpectrometerCalibration(Spectrometer device, double[] coefficients, ushort firstNm, ushort lastNm)
	{
		FirstNm = firstNm;
		LastNm = lastNm;
		_wavelengths = new double[device.PixelCount];
		for (var pixel = 0; pixel < _wavelengths.Length; pixel++)
		{
			_wavelengths[pixel] = Evaluate(coefficients, pixel);
		}
	}

	/// <summary>Lowest wavelength of the firmware table; PixelFor clamps lower values to it.</summary>
	public ushort FirstNm { get; }

	/// <summary>Highest wavelength of the firmware table; PixelFor clamps higher values to it.</summary>
	public ushort LastNm { get; }

	public static SpectrometerCalibration For(Spectrometer device)
	{
		return device switch
		{
			Spectrometer.Atp2000 => _atp2000,
			Spectrometer.Atp8000 => _atp8000,
			_ => throw new ArgumentOutOfRangeException(nameof(device), device, "Unknown spectrometer."),
		};
	}

	public double WavelengthOf(int pixel)
	{
		return _wavelengths[pixel];
	}

	/// <summary>The pixel nearest to the wavelength, clamped to the table range as PixelForWave() does.</summary>
	public int PixelFor(ushort nm)
	{
		var target = (double)Math.Clamp(nm, FirstNm, LastNm);
		var index = Array.BinarySearch(_wavelengths, target);
		if (index >= 0)
		{
			return index;
		}

		var above = ~index;
		if (above == _wavelengths.Length)
		{
			return _wavelengths.Length - 1;
		}

		var below = above - 1;
		return target - _wavelengths[below] <= _wavelengths[above] - target ? below : above;
	}

	/// <summary>The inclusive pixel range for the wavelength plus or minus delta nm (OptoskyProj#7).</summary>
	public PixelWindow Window(ushort nm, byte delta)
	{
		var low = Math.Max(nm - delta, 0);
		var high = Math.Min(nm + delta, ushort.MaxValue);
		return new PixelWindow(PixelFor((ushort)low), PixelFor((ushort)high));
	}

	private static double Evaluate(double[] coefficients, int pixel)
	{
		var wavelength = 0.0;
		var power = 1.0;
		foreach (var coefficient in coefficients)
		{
			wavelength += coefficient * power;
			power *= pixel;
		}

		return wavelength;
	}
}

public readonly record struct PixelWindow(int First, int Last);
