using System.Buffers;
using System.Buffers.Binary;

using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SpectrumVisualizer.Tests.Unit.Core.Protocol;

[Trait("Component", "Core")]
[Trait("Area", "Protocol")]
[Trait("Category", "Unit")]
public sealed class FrameDecoderTests
{
	[Fact]
	public void TheHeartbeatExampleDecodesToItsStateAndSeq()
	{
		var buffer = new ReadOnlySequence<byte>(HeartbeatExample);

		var found = FrameDecoder.TryReadFrame(ref buffer, out var frame, out var discarded);

		found.Should().BeTrue();
		frame.Should().Be(new BoardFrame(BoardState.Atp2000Connected | BoardState.Atp8000Connected, 42, null));
		discarded.Should().Be(0);
		buffer.Length.Should().Be(0);
	}

	[Theory]
	[InlineData(Spectrometer.Atp2000)]
	[InlineData(Spectrometer.Atp8000)]
	public void ASpectrumFrameRoundTripsThroughEncoderAndDecoder(Spectrometer device)
	{
		var original = Spectrum(device, 7);
		var buffer = new ReadOnlySequence<byte>(FrameEncoder.Encode(original));

		var found = FrameDecoder.TryReadFrame(ref buffer, out var frame, out var discarded);

		found.Should().BeTrue();
		frame.Should().BeEquivalentTo(original);
		discarded.Should().Be(0);
		buffer.Length.Should().Be(0);
	}

	[Theory]
	[InlineData(Spectrometer.Atp2000)]
	[InlineData(Spectrometer.Atp8000)]
	public void AFrameSplitAtEveryByteBoundaryWaitsForTheRestThenDecodes(Spectrometer device)
	{
		var bytes = FrameEncoder.Encode(Spectrum(device, 7));
		var failedSplits = new List<int>();

		for (var split = 1; split < bytes.Length; split++)
		{
			var prefix = new ReadOnlySequence<byte>(bytes.AsMemory(0, split));
			var waited = !FrameDecoder.TryReadFrame(ref prefix, out _, out var prefixDiscarded)
				&& prefixDiscarded == 0
				&& prefix.Length == split;

			var whole = TwoSegments(bytes, split);
			var decoded = FrameDecoder.TryReadFrame(ref whole, out var frame, out var discarded)
				&& discarded == 0
				&& whole.Length == 0
				&& FrameEncoder.Encode(frame).AsSpan().SequenceEqual(bytes);

			if (!waited || !decoded)
			{
				failedSplits.Add(split);
			}
		}

		failedSplits.Should().BeEmpty();
	}

	[Fact]
	public void AFalseSyncInsidePixelsIsPartOfAWholeFrame()
	{
		var first = FrameEncoder.Encode(Spectrum(Spectrometer.Atp8000, 1, PixelsWithFalseSync(0x0002)));
		var second = FrameEncoder.Encode(Heartbeat(1));
		var buffer = new ReadOnlySequence<byte>(Concat(first, second));

		FrameDecoder.TryReadFrame(ref buffer, out var firstFrame, out _).Should().BeTrue();
		FrameDecoder.TryReadFrame(ref buffer, out var secondFrame, out var discarded).Should().BeTrue();

		firstFrame!.Measurement.Should().NotBeNull();
		secondFrame.Should().Be(Heartbeat(1));
		discarded.Should().Be(0);
	}

	[Theory]
	[InlineData((ushort)0x0002)]
	[InlineData((ushort)0x1012)]
	public void AFalseSyncInsideACutFrameNeverHidesTheNextFrame(ushort falseLength)
	{
		var cut = FrameEncoder.Encode(Spectrum(Spectrometer.Atp8000, 1, PixelsWithFalseSync(falseLength)))[1..];
		var next = Spectrum(Spectrometer.Atp2000, 2);
		var buffer = new ReadOnlySequence<byte>(Concat(cut, FrameEncoder.Encode(next)));

		var found = FrameDecoder.TryReadFrame(ref buffer, out var frame, out var discarded);

		found.Should().BeTrue();
		frame.Should().BeEquivalentTo(next);
		discarded.Should().Be(cut.Length);
	}

	[Fact]
	public void AnInvalidLengthDropsOneByteAndResynchronises()
	{
		byte[] invalidLength = [0xAA, 0x55, 0xFF, 0x00];

		AssertDropsThenFindsHeartbeat(invalidLength);
	}

	[Fact]
	public void ASyncByteNotFollowedBySyncSecondDropsOneByte()
	{
		AssertDropsThenFindsHeartbeat([0xAA]);
	}

	[Fact]
	public void ABadCrcDropsOneByteAndFindsAFrameInsideTheRejectedCandidate()
	{
		byte[] truncatedHeader = [0xAA, 0x55, 0x02, 0x00];

		AssertDropsThenFindsHeartbeat(truncatedHeader);
	}

	[Theory]
	[InlineData(Spectrometer.Atp2000)]
	[InlineData(Spectrometer.Atp8000)]
	public void ACorruptedPixelByteDropsTheWholeSpectrumFrameAndTheNextFrameDecodes(Spectrometer device)
	{
		var corrupted = FrameEncoder.Encode(Spectrum(device, 3));
		corrupted[100] ^= 0x01;

		AssertDropsThenFindsHeartbeat(corrupted);
	}

	[Fact]
	public void ADeviceThatDisagreesWithTheLengthDropsOneByteAndResynchronises()
	{
		var mismatched = FrameEncoder.Encode(Spectrum(Spectrometer.Atp2000, 3));
		mismatched[6] = (byte)Spectrometer.Atp8000;
		var crcOffset = mismatched.Length - 2;
		var crc = Crc16Modbus.Compute(mismatched.AsSpan(2, crcOffset - 2));
		BinaryPrimitives.WriteUInt16LittleEndian(mismatched.AsSpan(crcOffset), crc);

		AssertDropsThenFindsHeartbeat(mismatched);
	}

	[Fact]
	public void ReservedStateBitsAreMaskedOff()
	{
		var bytes = FrameEncoder.Encode(new BoardFrame((BoardState)0xF3, 9, null));
		var buffer = new ReadOnlySequence<byte>(bytes);

		FrameDecoder.TryReadFrame(ref buffer, out var frame, out _).Should().BeTrue();

		frame!.State.Should().Be(BoardState.Atp2000Connected | BoardState.Atp8000Connected);
	}

	[Fact]
	public void GarbageBeforeAFrameIsCountedAsDiscarded()
	{
		byte[] garbage = [0x00, 0x11, 0x22, 0x55, 0x33];

		AssertDropsThenFindsHeartbeat(garbage);
	}

	[Fact]
	public void ABufferWithoutASyncByteIsDiscardedWhole()
	{
		var buffer = new ReadOnlySequence<byte>([0x00, 0x11, 0x55]);

		var found = FrameDecoder.TryReadFrame(ref buffer, out _, out var discarded);

		found.Should().BeFalse();
		discarded.Should().Be(3);
		buffer.Length.Should().Be(0);
	}

	[Fact]
	public void ATrailingSyncByteIsKeptForTheNextRead()
	{
		var buffer = new ReadOnlySequence<byte>([0x00, 0x11, 0xAA]);

		var found = FrameDecoder.TryReadFrame(ref buffer, out _, out var discarded);

		found.Should().BeFalse();
		discarded.Should().Be(2);
		buffer.ToArray().Should().Equal(0xAA);
	}

	private static void AssertDropsThenFindsHeartbeat(byte[] leading)
	{
		var buffer = new ReadOnlySequence<byte>(Concat(leading, HeartbeatExample));

		var found = FrameDecoder.TryReadFrame(ref buffer, out var frame, out var discarded);

		found.Should().BeTrue();
		frame.Should().Be(Heartbeat(42));
		discarded.Should().Be(leading.Length);
		buffer.Length.Should().Be(0);
	}

	/// <summary>Plants AA 55 followed by the given little-endian length at pixel 100.</summary>
	private static ushort[] PixelsWithFalseSync(ushort falseLength)
	{
		var pixels = Ramp(Spectrometer.Atp8000);
		pixels[100] = 0x55AA;
		pixels[101] = falseLength;
		return pixels;
	}

	private static ReadOnlySequence<byte> TwoSegments(byte[] bytes, int split)
	{
		var first = new BufferSegment(bytes.AsMemory(0, split), 0);
		var second = first.Append(bytes.AsMemory(split));
		return new ReadOnlySequence<byte>(first, 0, second, second.Memory.Length);
	}

	private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
	{
		public BufferSegment(ReadOnlyMemory<byte> memory, long runningIndex)
		{
			Memory = memory;
			RunningIndex = runningIndex;
		}

		public BufferSegment Append(ReadOnlyMemory<byte> memory)
		{
			var next = new BufferSegment(memory, RunningIndex + Memory.Length);
			Next = next;
			return next;
		}
	}
}
