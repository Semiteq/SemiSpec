using System.Buffers;

using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Protocol;

/// <summary>Checks the frame bytes that the board firmware's pc_link.c writes, see docs/architecture/board-protocol.md.</summary>
[Trait("Component", "Core")]
[Trait("Area", "Protocol")]
[Trait("Category", "Unit")]
public sealed class FirmwareFrameTests
{
	private static readonly Dictionary<string, BoardFrame> _expectedFrames = new()
	{
		["heartbeat-example"] = new BoardFrame(
			BoardState.Atp2000Connected | BoardState.Atp8000Connected,
			42,
			null),
		["atp2000"] = new BoardFrame(
			BoardState.Atp2000Connected | BoardState.Atp8000MeasurementFailed,
			1,
			new SpectrumMeasurement(Spectrometer.Atp2000, 650, 10, 4660f, 258f, 7f, Ramp(Spectrometer.Atp2000))),
		["atp8000"] = new BoardFrame(
			BoardState.Atp8000Connected | BoardState.Atp2000MeasurementFailed,
			255,
			new SpectrumMeasurement(Spectrometer.Atp8000, 1550, 100, 65535f, 1f, 0f, Ramp(Spectrometer.Atp8000))),
		["atp8000-dark"] = new BoardFrame(
			BoardState.Atp2000Connected | BoardState.Atp8000Connected,
			0,
			new SpectrumMeasurement(
				Spectrometer.Atp8000,
				1679,
				10,
				0f,
				0f,
				3f,
				new ushort[Spectrometer.Atp8000.PixelCount])),
	};

	private static readonly Dictionary<string, byte[]> _firmwareFrames = ReadFirmwareFrames();

	[Fact]
	public void TheFileHoldsExactlyTheExpectedFrames()
	{
		_firmwareFrames.Keys.Should().BeEquivalentTo(_expectedFrames.Keys);
	}

	[Theory]
	[InlineData("heartbeat-example")]
	[InlineData("atp2000")]
	[InlineData("atp8000")]
	[InlineData("atp8000-dark")]
	public void AFirmwareFrameDecodesToTheExpectedFrame(string name)
	{
		var buffer = new ReadOnlySequence<byte>(_firmwareFrames[name]);

		var found = FrameDecoder.TryReadFrame(ref buffer, out var frame, out var discarded);

		found.Should().BeTrue();
		discarded.Should().Be(0);
		buffer.IsEmpty.Should().BeTrue();
		frame.Should().BeEquivalentTo(_expectedFrames[name]);
	}

	[Theory]
	[InlineData("heartbeat-example")]
	[InlineData("atp2000")]
	[InlineData("atp8000")]
	[InlineData("atp8000-dark")]
	public void AFirmwareFrameEqualsTheAppEncoding(string name)
	{
		var encoded = FrameEncoder.Encode(_expectedFrames[name]);

		_firmwareFrames[name].Should().Equal(encoded);
	}

	/// <summary>The ramp of the firmware host program: every pixel has different high and low bytes.</summary>
	private static ushort[] Ramp(Spectrometer device)
	{
		return [.. Enumerable.Range(0, device.PixelCount).Select(i => (ushort)((i * 257) + 0x1234))];
	}

	private static Dictionary<string, byte[]> ReadFirmwareFrames()
	{
		var path = Path.Combine(AppContext.BaseDirectory, "Core", "Protocol", "FirmwareFrames.txt");
		return File.ReadAllLines(path)
			.Where(line => line.Length > 0)
			.Select(line => line.Split(' '))
			.ToDictionary(parts => parts[0], parts => Convert.FromHexString(parts[1]));
	}
}
