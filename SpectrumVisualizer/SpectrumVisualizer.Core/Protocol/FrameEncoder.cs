using System.Buffers.Binary;

namespace SpectrumVisualizer.Core.Protocol;

/// <summary>Builds the bytes of a board-to-PC frame; the layout is in docs/architecture.</summary>
public static class FrameEncoder
{
	private const byte SyncFirst = 0xAA;
	private const byte SyncSecond = 0x55;
	private const int LengthOffset = 2;
	private const int StateAndSeqSize = 2;
	private const int MeasurementHeaderSize = 16;
	private const int CrcSize = 2;

	public static byte[] Encode(BoardFrame frame)
	{
		var measurement = frame.Measurement;
		var measurementSize = measurement is null ? 0 : MeasurementHeaderSize + (2 * measurement.Pixels.Length);
		var length = StateAndSeqSize + measurementSize;
		var bytes = new byte[LengthOffset + 2 + length + CrcSize];

		bytes[0] = SyncFirst;
		bytes[1] = SyncSecond;
		BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(LengthOffset), (ushort)length);
		bytes[4] = (byte)frame.State;
		bytes[5] = frame.Seq;
		if (measurement is not null)
		{
			WriteMeasurement(bytes.AsSpan(6, measurementSize), measurement);
		}

		var crcOffset = bytes.Length - CrcSize;
		var crc = Crc16Modbus.Compute(bytes.AsSpan(LengthOffset, crcOffset - LengthOffset));
		BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(crcOffset), crc);
		return bytes;
	}

	private static void WriteMeasurement(Span<byte> target, SpectrumMeasurement measurement)
	{
		target[0] = (byte)measurement.Device;
		BinaryPrimitives.WriteUInt16LittleEndian(target[1..], measurement.WavelengthNm);
		target[3] = measurement.Delta;
		BinaryPrimitives.WriteSingleLittleEndian(target[4..], measurement.Amplitude);
		BinaryPrimitives.WriteSingleLittleEndian(target[8..], measurement.Snr);
		BinaryPrimitives.WriteSingleLittleEndian(target[12..], measurement.Q);

		var pixelBytes = target[MeasurementHeaderSize..];
		for (var i = 0; i < measurement.Pixels.Length; i++)
		{
			BinaryPrimitives.WriteUInt16LittleEndian(pixelBytes[(2 * i)..], measurement.Pixels[i]);
		}
	}
}
