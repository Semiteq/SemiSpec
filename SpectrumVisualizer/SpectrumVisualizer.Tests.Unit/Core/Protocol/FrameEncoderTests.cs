using System.Buffers.Binary;

using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;

using Xunit;

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
		var pixels = Enumerable.Range(0, device.PixelCount).Select(i => (ushort)(i * 7)).ToArray();
		var measurement = new SpectrumMeasurement(device, 1310, 5, 1234.5f, 42.25f, 0.75f, pixels);
		var frame = new BoardFrame(BoardState.Atp8000Connected | BoardState.Atp2000MeasurementFailed, 200, measurement);

		var bytes = FrameEncoder.Encode(frame);

		bytes.Should().HaveCount(frameSize);
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

		var crcOffset = frameSize - 2;
		var expectedCrc = Crc16Modbus.Compute(bytes.AsSpan(2, crcOffset - 2));
		BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(crcOffset)).Should().Be(expectedCrc);
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
