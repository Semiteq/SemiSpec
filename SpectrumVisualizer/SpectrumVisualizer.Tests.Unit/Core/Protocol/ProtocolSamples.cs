using System.Buffers.Binary;

using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.Tests.Unit.Core.Protocol;

/// <summary>Sample frames whose pixels hold no AA 55 pair, so no false sync appears unless a test plants one.</summary>
internal static class ProtocolSamples
{
	private const int LengthOffset = 2;
	private const int CrcSize = 2;

	public static readonly byte[] HeartbeatExample = [0xAA, 0x55, 0x02, 0x00, 0x03, 0x2A, 0x80, 0xB3];

	public static BoardFrame Heartbeat(byte seq)
	{
		return new BoardFrame(BoardState.Atp2000Connected | BoardState.Atp8000Connected, seq, null);
	}

	public static BoardFrame SpectrumFrame(Spectrometer device, byte seq)
	{
		return SpectrumFrame(device, seq, Ramp(device));
	}

	public static BoardFrame SpectrumFrame(Spectrometer device, byte seq, ushort[] pixels)
	{
		var measurement = new SpectrumMeasurement(device, 1310, 5, 1234.5f, 42.25f, 0.75f, pixels);
		return new BoardFrame(BoardState.Atp2000Connected | BoardState.Atp8000Connected, seq, measurement);
	}

	public static ushort[] Ramp(Spectrometer device)
	{
		return [.. Enumerable.Range(0, device.PixelCount).Select(i => (ushort)(i * 7))];
	}

	/// <summary>The frame's bytes with a broken CRC, so the decoder drops the frame.</summary>
	public static byte[] Corrupted(BoardFrame frame)
	{
		var bytes = FrameEncoder.Encode(frame);
		bytes[^1] ^= 0xFF;
		return bytes;
	}

	public static byte[] Concat(params IEnumerable<byte[]> parts)
	{
		return [.. parts.SelectMany(part => part)];
	}

	public static bool HasValidCrc(byte[] frameBytes)
	{
		return BinaryPrimitives.ReadUInt16LittleEndian(frameBytes.AsSpan(frameBytes.Length - CrcSize))
			== CrcOf(frameBytes);
	}

	public static void RecomputeCrc(byte[] frameBytes)
	{
		BinaryPrimitives.WriteUInt16LittleEndian(frameBytes.AsSpan(frameBytes.Length - CrcSize), CrcOf(frameBytes));
	}

	private static ushort CrcOf(byte[] frameBytes)
	{
		return Crc16Modbus.Compute(frameBytes.AsSpan(LengthOffset..^CrcSize));
	}
}
