using System.Runtime.InteropServices;

using SpectrumVisualizer.Core.Calibration;
using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.Core.Store;

/// <summary>The queried window of each device in the stored pixel blob, and whether the device covers the wavelength.</summary>
public sealed class QueryWindows(ushort nm, byte delta)
{
	private readonly PixelWindow _atp2000 = SpectrometerCalibration.For(Spectrometer.Atp2000).Window(nm, delta);
	private readonly PixelWindow _atp8000 = SpectrometerCalibration.For(Spectrometer.Atp8000).Window(nm, delta);
	private readonly bool _atp2000Covers = SpectrumIntensity.Covers(Spectrometer.Atp2000, nm);
	private readonly bool _atp8000Covers = SpectrumIntensity.Covers(Spectrometer.Atp8000, nm);

	public int LargestByteCount => Math.Max(ByteCount(_atp2000), ByteCount(_atp8000));

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
		return SpectrumIntensity.Mean(pixels, new PixelWindow(0, pixels.Length - 1));
	}
}
