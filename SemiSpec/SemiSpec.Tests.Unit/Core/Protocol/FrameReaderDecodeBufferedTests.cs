using System.IO.Pipelines;

using AwesomeAssertions;

using SemiSpec.Core.Protocol;

using Xunit;

using static SemiSpec.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SemiSpec.Tests.Unit.Core.Protocol;

/// <summary>FrameReader.DecodeBuffered: what it yields from the bytes already in the pipe, without a wait.</summary>
[Trait("Component", "Core")]
[Trait("Area", "Protocol")]
[Trait("Category", "Unit")]
public sealed class FrameReaderDecodeBufferedTests
{
	private const int TestTimeoutMs = 10_000;

	[Fact(Timeout = TestTimeoutMs)]
	public async Task DecodeBufferedYieldsFramesWrittenAcrossManyWritesInOrder()
	{
		var ct = TestContext.Current.CancellationToken;
		BoardFrame[] frames =
		[
			Heartbeat(0),
			SpectrumFrame(Spectrometer.Atp2000, 1),
			SpectrumFrame(Spectrometer.Atp8000, 2),
			Heartbeat(2),
		];
		var stream = Concat(frames.Select(FrameEncoder.Encode));
		var pipe = new Pipe();
		var reader = new FrameReader();
		var decoded = new List<BoardFrame>();

		var random = new Random(20261104);
		for (var offset = 0; offset < stream.Length;)
		{
			var size = Math.Min(random.Next(1, 600), stream.Length - offset);
			await pipe.Writer.WriteAsync(stream.AsMemory(offset, size), ct);
			offset += size;
			reader.DecodeBuffered(pipe.Reader, decoded);
		}

		decoded.Should().BeEquivalentTo(frames, options => options.WithStrictOrdering());
		reader.DiscardedBytes.Should().Be(0);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task DecodeBufferedKeepsAPartialFrameBufferedUntilItsRestArrives()
	{
		var ct = TestContext.Current.CancellationToken;
		var pipe = new Pipe();
		var reader = new FrameReader();
		var decoded = new List<BoardFrame>();
		var bytes = FrameEncoder.Encode(Heartbeat(5));

		await pipe.Writer.WriteAsync(bytes.AsMemory(0, 5), ct);
		reader.DecodeBuffered(pipe.Reader, decoded);
		decoded.Should().BeEmpty();

		await pipe.Writer.WriteAsync(bytes.AsMemory(5), ct);
		reader.DecodeBuffered(pipe.Reader, decoded);
		decoded.Should().Equal(Heartbeat(5));
		reader.DiscardedBytes.Should().Be(0);
	}

	[Fact]
	public void DecodeBufferedOnAnEmptyPipeAddsNoFrame()
	{
		var pipe = new Pipe();
		var decoded = new List<BoardFrame>();

		new FrameReader().DecodeBuffered(pipe.Reader, decoded);

		decoded.Should().BeEmpty();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task DecodeBufferedYieldsTwoWholeFramesFromOneWrite()
	{
		var ct = TestContext.Current.CancellationToken;
		var pipe = new Pipe();
		var decoded = new List<BoardFrame>();
		await pipe.Writer.WriteAsync(Concat(FrameEncoder.Encode(Heartbeat(1)), FrameEncoder.Encode(Heartbeat(2))), ct);

		new FrameReader().DecodeBuffered(pipe.Reader, decoded);

		decoded.Should().Equal(Heartbeat(1), Heartbeat(2));
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task DecodeBufferedAfterWriterCompletionYieldsTheFramesBehindAStalledFalseCandidate()
	{
		var ct = TestContext.Current.CancellationToken;
		var pipe = new Pipe();
		var reader = new FrameReader();
		var decoded = new List<BoardFrame>();
		byte[] falseCandidate = [0xAA, 0x55, 0x12, 0x10];
		await pipe.Writer.WriteAsync(
			Concat(falseCandidate, FrameEncoder.Encode(Heartbeat(6)), FrameEncoder.Encode(Heartbeat(7))),
			ct);

		reader.DecodeBuffered(pipe.Reader, decoded);
		decoded.Should().BeEmpty();

		await pipe.Writer.CompleteAsync();
		reader.DecodeBuffered(pipe.Reader, decoded);

		decoded.Should().Equal(Heartbeat(6), Heartbeat(7));
		reader.DiscardedBytes.Should().Be(falseCandidate.Length);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task DecodeBufferedCountsGarbageAndACompletedPartialFrameAsDiscarded()
	{
		var ct = TestContext.Current.CancellationToken;
		var pipe = new Pipe();
		var reader = new FrameReader();
		var decoded = new List<BoardFrame>();
		var partial = FrameEncoder.Encode(Heartbeat(4))[..5];

		await pipe.Writer.WriteAsync(Concat([0x01, 0x02, 0x03], FrameEncoder.Encode(Heartbeat(3)), partial), ct);
		reader.DecodeBuffered(pipe.Reader, decoded);
		reader.DiscardedBytes.Should().Be(3);

		await pipe.Writer.CompleteAsync();
		reader.DecodeBuffered(pipe.Reader, decoded);

		decoded.Should().Equal(Heartbeat(3));
		reader.DiscardedBytes.Should().Be(3 + partial.Length);
	}
}
