using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace SpectrumVisualizer.Core.Protocol;

/// <summary>Finds and validates board-to-PC frames in a byte stream; the rules are in docs/architecture.</summary>
public static class FrameDecoder
{
	private const byte DefinedStateBits = (byte)(BoardState.Atp2000Connected
		| BoardState.Atp8000Connected
		| BoardState.Atp2000MeasurementFailed
		| BoardState.Atp8000MeasurementFailed);

	private enum Candidate
	{
		Incomplete,
		Invalid,
		Whole,
	}

	/// <summary>
	/// Returns false only when a whole frame is not buffered yet; every dropped byte is sliced off and counted.
	/// </summary>
	public static bool TryReadFrame(
		ref ReadOnlySequence<byte> buffer,
		[NotNullWhen(true)] out BoardFrame? frame,
		out long discarded)
	{
		frame = null;
		discarded = 0;
		while (true)
		{
			var reader = new SequenceReader<byte>(buffer);
			if (!reader.TryAdvanceTo(FrameLayout.SyncFirst, advancePastDelimiter: false))
			{
				discarded += buffer.Length;
				buffer = buffer.Slice(buffer.End);
				return false;
			}

			discarded += reader.Consumed;
			buffer = buffer.Slice(reader.Position);
			var candidate = CheckHeader(buffer, out var frameSize, out var device);
			if (candidate == Candidate.Incomplete)
			{
				return false;
			}

			if (candidate == Candidate.Whole)
			{
				frame = ParseFrame(Contiguous(buffer.Slice(0, frameSize)), device);
				if (frame is not null)
				{
					buffer = buffer.Slice(frameSize);
					return true;
				}
			}

			buffer = buffer.Slice(1);
			discarded++;
		}
	}

	/// <summary>The CRC is not checked here.</summary>
	private static Candidate CheckHeader(ReadOnlySequence<byte> buffer, out int frameSize, out Spectrometer? device)
	{
		frameSize = 0;
		device = null;
		var reader = new SequenceReader<byte>(buffer);
		reader.Advance(1);
		if (!reader.TryRead(out var syncSecond))
		{
			return Candidate.Incomplete;
		}

		if (syncSecond != FrameLayout.SyncSecond)
		{
			return Candidate.Invalid;
		}

		if (!reader.TryReadLittleEndian(out short rawLength))
		{
			return Candidate.Incomplete;
		}

		var length = (ushort)rawLength;
		if (!TryClassifyLength(length, out device))
		{
			return Candidate.Invalid;
		}

		frameSize = FrameLayout.FrameSize(length);
		return buffer.Length < frameSize ? Candidate.Incomplete : Candidate.Whole;
	}

	private static ReadOnlySpan<byte> Contiguous(ReadOnlySequence<byte> frameBytes)
	{
		return frameBytes.IsSingleSegment ? frameBytes.FirstSpan : frameBytes.ToArray();
	}

	private static bool TryClassifyLength(ushort length, out Spectrometer? device)
	{
		device = null;
		if (length == FrameLayout.HeartbeatLength)
		{
			return true;
		}

		if (length == FrameLayout.SpectrumLength(Spectrometer.Atp2000))
		{
			device = Spectrometer.Atp2000;
			return true;
		}

		if (length == FrameLayout.SpectrumLength(Spectrometer.Atp8000))
		{
			device = Spectrometer.Atp8000;
			return true;
		}

		return false;
	}

	/// <summary>Returns null when the CRC fails or the device byte disagrees with the length.</summary>
	private static BoardFrame? ParseFrame(ReadOnlySpan<byte> bytes, Spectrometer? device)
	{
		var crcOffset = bytes.Length - FrameLayout.CrcSize;
		var expectedCrc = BinaryPrimitives.ReadUInt16LittleEndian(bytes[crcOffset..]);
		if (Crc16Modbus.Compute(bytes[FrameLayout.LengthOffset..crcOffset]) != expectedCrc)
		{
			return null;
		}

		var state = (BoardState)(bytes[FrameLayout.StateOffset] & DefinedStateBits);
		var seq = bytes[FrameLayout.SeqOffset];
		if (device is not { } spectrometer)
		{
			return new BoardFrame(state, seq, null);
		}

		var measurementPart = bytes[FrameLayout.MeasurementOffset..crcOffset];
		if (measurementPart[FrameLayout.DeviceOffset] != (byte)spectrometer)
		{
			return null;
		}

		return new BoardFrame(state, seq, ReadMeasurement(measurementPart, spectrometer));
	}

	private static SpectrumMeasurement ReadMeasurement(ReadOnlySpan<byte> part, Spectrometer device)
	{
		var pixels = new ushort[device.PixelCount];
		var pixelBytes = part[FrameLayout.MeasurementHeaderSize..];
		for (var i = 0; i < pixels.Length; i++)
		{
			pixels[i] = BinaryPrimitives.ReadUInt16LittleEndian(pixelBytes[(2 * i)..]);
		}

		return new SpectrumMeasurement(
			device,
			BinaryPrimitives.ReadUInt16LittleEndian(part[FrameLayout.WavelengthOffset..]),
			part[FrameLayout.DeltaOffset],
			BinaryPrimitives.ReadSingleLittleEndian(part[FrameLayout.AmplitudeOffset..]),
			BinaryPrimitives.ReadSingleLittleEndian(part[FrameLayout.SnrOffset..]),
			BinaryPrimitives.ReadSingleLittleEndian(part[FrameLayout.QOffset..]),
			pixels);
	}
}
