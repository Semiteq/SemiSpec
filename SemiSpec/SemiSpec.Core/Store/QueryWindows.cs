using System.Runtime.InteropServices;

using SemiSpec.Core.Calibration;
using SemiSpec.Core.Protocol;

namespace SemiSpec.Core.Store;

/// <summary>
/// The queried window of each device in the stored pixel blob, and whether the device covers the wavelength.
/// </summary>
public sealed class QueryWindows(ushort wavelengthNm, byte delta)
{
	private readonly PixelWindow _atp2000 =
		SpectrometerCalibration.For(Spectrometer.Atp2000).Window(wavelengthNm, delta);
	private readonly PixelWindow _atp8000 =
		SpectrometerCalibration.For(Spectrometer.Atp8000).Window(wavelengthNm, delta);
	private readonly bool _atp2000Covers = InFirmwareTable(Spectrometer.Atp2000, wavelengthNm);
	private readonly bool _atp8000Covers = InFirmwareTable(Spectrometer.Atp8000, wavelengthNm);

	public int LargestByteCount => Math.Max(ByteCount(_atp2000), ByteCount(_atp8000));

	/// <summary>True when the wavelength lies inside the device's firmware table range.</summary>
	public bool Covers(Spectrometer device)
	{
		return device == Spectrometer.Atp2000 ? _atp2000Covers : _atp8000Covers;
	}

	public PixelWindow For(Spectrometer device)
	{
		return device == Spectrometer.Atp2000 ? _atp2000 : _atp8000;
	}

	/// <summary>The 1-based byte offset of the window's first pixel, as substr counts.</summary>
	public static int ByteStart(PixelWindow window)
	{
		return (sizeof(ushort) * window.First) + 1;
	}

	public static int ByteCount(PixelWindow window)
	{
		return sizeof(ushort) * (window.Last - window.First + 1);
	}

	/// <summary>The mean of a window cut out of the blob: little-endian u16 pixels.</summary>
	public static double MeanOf(ReadOnlySpan<byte> windowBytes)
	{
		var pixels = MemoryMarshal.Cast<byte, ushort>(windowBytes);
		long sum = 0;
		foreach (var pixel in pixels)
		{
			sum += pixel;
		}

		return (double)sum / pixels.Length;
	}

	private static bool InFirmwareTable(Spectrometer device, ushort wavelengthNm)
	{
		var calibration = SpectrometerCalibration.For(device);
		return calibration.FirstNm <= wavelengthNm && wavelengthNm <= calibration.LastNm;
	}
}
