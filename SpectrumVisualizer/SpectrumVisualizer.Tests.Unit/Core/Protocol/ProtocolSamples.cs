using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.Tests.Unit.Core.Protocol;

/// <summary>Sample frames whose pixel data holds no AA 55 pair, so no false sync appears unless a test plants one.</summary>
internal static class ProtocolSamples
{
	public static readonly byte[] HeartbeatExample = [0xAA, 0x55, 0x02, 0x00, 0x03, 0x2A, 0x80, 0xB3];

	public static BoardFrame Heartbeat(byte seq)
	{
		return new BoardFrame(BoardState.Atp2000Connected | BoardState.Atp8000Connected, seq, null);
	}

	public static BoardFrame Spectrum(Spectrometer device, byte seq)
	{
		return Spectrum(device, seq, Ramp(device));
	}

	public static BoardFrame Spectrum(Spectrometer device, byte seq, ushort[] pixels)
	{
		var measurement = new SpectrumMeasurement(device, 1310, 5, 1234.5f, 42.25f, 0.75f, pixels);
		return new BoardFrame(BoardState.Atp2000Connected | BoardState.Atp8000Connected, seq, measurement);
	}

	public static ushort[] Ramp(Spectrometer device)
	{
		return [.. Enumerable.Range(0, device.PixelCount).Select(i => (ushort)(i * 7))];
	}

	public static byte[] Concat(params IEnumerable<byte[]> parts)
	{
		return [.. parts.SelectMany(part => part)];
	}
}
