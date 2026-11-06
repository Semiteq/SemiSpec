using System.Buffers.Binary;

namespace SemiSpec.Core.Protocol;

/// <summary>Builds the bytes of a board-to-PC frame; the layout is in docs/architecture.</summary>
public static class FrameEncoder
{
	public static byte[] Encode(BoardFrame frame)
	{
		var measurement = frame.Measurement;
		var length = measurement is null ? FrameLayout.HeartbeatLength : FrameLayout.SpectrumLength(measurement.Device);
		var bytes = new byte[FrameLayout.FrameSize(length)];

		bytes[0] = FrameLayout.SyncFirst;
		bytes[1] = FrameLayout.SyncSecond;
		BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(FrameLayout.LengthOffset), (ushort)length);
		bytes[FrameLayout.StateOffset] = (byte)frame.State;
		bytes[FrameLayout.SeqOffset] = frame.Seq;
		if (measurement is not null)
		{
			var measurementSize = length - FrameLayout.StateAndSeqSize;
			WriteMeasurement(bytes.AsSpan(FrameLayout.MeasurementOffset, measurementSize), measurement);
		}

		var crcOffset = bytes.Length - FrameLayout.CrcSize;
		var crc = Crc16Modbus.Compute(bytes.AsSpan(FrameLayout.LengthOffset, crcOffset - FrameLayout.LengthOffset));
		BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(crcOffset), crc);
		return bytes;
	}

	private static void WriteMeasurement(Span<byte> target, SpectrumMeasurement measurement)
	{
		target[FrameLayout.DeviceOffset] = (byte)measurement.Device;
		BinaryPrimitives.WriteUInt16LittleEndian(target[FrameLayout.WavelengthOffset..], measurement.WavelengthNm);
		target[FrameLayout.DeltaOffset] = measurement.Delta;
		BinaryPrimitives.WriteSingleLittleEndian(target[FrameLayout.AmplitudeOffset..], measurement.Amplitude);
		BinaryPrimitives.WriteSingleLittleEndian(target[FrameLayout.SnrOffset..], measurement.Snr);
		BinaryPrimitives.WriteSingleLittleEndian(target[FrameLayout.QOffset..], measurement.Q);

		var pixelBytes = target[FrameLayout.MeasurementHeaderSize..];
		for (var i = 0; i < measurement.Pixels.Length; i++)
		{
			BinaryPrimitives.WriteUInt16LittleEndian(pixelBytes[(2 * i)..], measurement.Pixels[i]);
		}
	}
}
