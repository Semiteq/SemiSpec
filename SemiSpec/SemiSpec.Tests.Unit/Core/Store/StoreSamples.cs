using SemiSpec.Core.Protocol;
using SemiSpec.Tests.Unit.Core.Protocol;

namespace SemiSpec.Tests.Unit.Core.Store;

/// <summary>Received frames at chosen times, wavelengths and devices for the store tests.</summary>
internal static class StoreSamples
{
	public const ushort SampleNm = 1310;
	public const byte SampleDelta = 5;

	public static readonly DateTimeOffset Start = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

	public static ReceivedFrame Spectrum(
		DateTimeOffset receivedAt,
		Spectrometer device = Spectrometer.Atp8000,
		ushort nm = SampleNm,
		byte delta = SampleDelta,
		byte seq = 1)
	{
		return Spectrum(receivedAt, device, ProtocolSamples.Ramp(device), nm, delta, seq);
	}

	public static ReceivedFrame Spectrum(
		DateTimeOffset receivedAt,
		Spectrometer device,
		ushort[] pixels,
		ushort nm = SampleNm,
		byte delta = SampleDelta,
		byte seq = 1)
	{
		var sample = ProtocolSamples.SpectrumFrame(device, seq, pixels);
		var measurement = new SpectrumMeasurement(
			device,
			nm,
			delta,
			sample.Measurement!.Amplitude,
			sample.Measurement.Snr,
			sample.Measurement.Q,
			pixels);
		return new ReceivedFrame(sample with { Measurement = measurement }, receivedAt);
	}

	/// <summary>A dark frame: every pixel, the amplitude and the SNR zero.</summary>
	public static ReceivedFrame Dark(DateTimeOffset receivedAt, Spectrometer device = Spectrometer.Atp8000)
	{
		var pixels = new ushort[device.PixelCount];
		var measurement = new SpectrumMeasurement(device, SampleNm, SampleDelta, 0f, 0f, 0f, pixels);
		return new ReceivedFrame(new BoardFrame(device.ConnectedBit, 1, measurement), receivedAt);
	}

	public static ReceivedFrame Heartbeat(DateTimeOffset receivedAt, byte seq = 1)
	{
		return new ReceivedFrame(ProtocolSamples.Heartbeat(seq), receivedAt);
	}
}
