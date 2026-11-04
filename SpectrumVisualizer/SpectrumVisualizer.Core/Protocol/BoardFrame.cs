using System.Runtime.InteropServices;

namespace SpectrumVisualizer.Core.Protocol;

/// <summary>One frame from the board: a heartbeat when Measurement is null, a spectrum frame otherwise.</summary>
public sealed record BoardFrame(BoardState State, byte Seq, SpectrumMeasurement? Measurement);

[Flags]
public enum BoardState : byte
{
	None = 0,
	Atp2000Connected = 1 << 0,
	Atp8000Connected = 1 << 1,
	Atp2000MeasurementFailed = 1 << 2,
	Atp8000MeasurementFailed = 1 << 3,
}

public sealed record SpectrumMeasurement
{
	public SpectrumMeasurement(
		Spectrometer device,
		ushort wavelengthNm,
		byte delta,
		float amplitude,
		float snr,
		float q,
		ushort[] pixels)
	{
		if (pixels.Length != device.PixelCount)
		{
			throw new ArgumentException(
				$"{device} has {device.PixelCount} pixels, got {pixels.Length}.",
				nameof(pixels));
		}

		Device = device;
		WavelengthNm = wavelengthNm;
		Delta = delta;
		Amplitude = amplitude;
		Snr = snr;
		Q = q;
		Pixels = pixels;
	}

	public Spectrometer Device { get; }

	public ushort WavelengthNm { get; }

	/// <summary>Half-width of the measurement window, in nm.</summary>
	public byte Delta { get; }

	public float Amplitude { get; }

	public float Snr { get; }

	public float Q { get; }

	public ushort[] Pixels { get; }

	public bool Equals(SpectrumMeasurement? other)
	{
		return other is not null
			&& Device == other.Device
			&& WavelengthNm == other.WavelengthNm
			&& Delta == other.Delta
			&& Amplitude.Equals(other.Amplitude)
			&& Snr.Equals(other.Snr)
			&& Q.Equals(other.Q)
			&& Pixels.AsSpan().SequenceEqual(other.Pixels);
	}

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(Device);
		hash.Add(WavelengthNm);
		hash.Add(Delta);
		hash.Add(Amplitude);
		hash.Add(Snr);
		hash.Add(Q);
		hash.AddBytes(MemoryMarshal.AsBytes(Pixels.AsSpan()));
		return hash.ToHashCode();
	}
}
