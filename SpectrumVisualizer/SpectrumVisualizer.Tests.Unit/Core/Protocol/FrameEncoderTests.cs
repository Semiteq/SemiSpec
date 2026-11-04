using System.Buffers.Binary;

using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SpectrumVisualizer.Tests.Unit.Core.Protocol;

[Trait("Component", "Core")]
[Trait("Area", "Protocol")]
[Trait("Category", "Unit")]
public sealed class FrameEncoderTests
{
	[Fact]
	public void AHeartbeatEncodesToTheProtocolExample()
	{
		var frame = new BoardFrame(BoardState.Atp2000Connected | BoardState.Atp8000Connected, 42, null);

		var bytes = FrameEncoder.Encode(frame);

		bytes.Should().Equal(0xAA, 0x55, 0x02, 0x00, 0x03, 0x2A, 0x80, 0xB3);
	}

	[Theory]
	[InlineData(Spectrometer.Atp2000, 4120, 4114)]
	[InlineData(Spectrometer.Atp8000, 1048, 1042)]
	public void ASpectrumFrameHasItsFieldsAtTheProtocolOffsets(Spectrometer device, int frameSize, int length)
	{
		var frame = Spectrum(device, 200) with
		{
			State = BoardState.Atp8000Connected | BoardState.Atp2000MeasurementFailed,
		};
		var pixels = frame.Measurement!.Pixels;

		var bytes = FrameEncoder.Encode(frame);

		bytes.Should().HaveCount(frameSize);
		device.FrameSize.Should().Be(frameSize);
		bytes[0].Should().Be(0xAA);
		bytes[1].Should().Be(0x55);
		BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)).Should().Be((ushort)length);
		bytes[4].Should().Be(0x06);
		bytes[5].Should().Be(200);
		bytes[6].Should().Be((byte)device);
		BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(7)).Should().Be(1310);
		bytes[9].Should().Be(5);
		BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(10)).Should().Be(1234.5f);
		BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(14)).Should().Be(42.25f);
		BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(18)).Should().Be(0.75f);
		for (var i = 0; i < pixels.Length; i++)
		{
			BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(22 + (2 * i))).Should().Be(pixels[i]);
		}

		HasValidCrc(bytes).Should().BeTrue();
	}

	[Theory]
	[InlineData(Spectrometer.Atp2000, 512)]
	[InlineData(Spectrometer.Atp8000, 2048)]
	[InlineData(Spectrometer.Atp8000, 0)]
	public void AMeasurementRejectsAPixelCountThatDoesNotMatchItsDevice(Spectrometer device, int pixelCount)
	{
		var create = () => new SpectrumMeasurement(device, 1310, 5, 0f, 0f, 0f, new ushort[pixelCount]);

		create.Should().Throw<ArgumentException>();
	}
}
