using System.IO.Pipelines;

using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SpectrumVisualizer.Tests.Unit.Core.Protocol;

[Trait("Component", "Core")]
[Trait("Area", "Protocol")]
[Trait("Category", "Unit")]
public sealed class FrameReaderTests
{
	private const int TestTimeoutMs = 10_000;

	[Fact(Timeout = TestTimeoutMs)]
	public async Task FramesWrittenInRandomChunksAreAllYieldedInOrder()
	{
		var ct = TestContext.Current.CancellationToken;
		BoardFrame[] frames =
		[
			Heartbeat(0),
			Spectrum(Spectrometer.Atp2000, 1),
			Spectrum(Spectrometer.Atp8000, 2),
			Heartbeat(2),
		];
		var stream = Concat(frames.Select(FrameEncoder.Encode));
		var pipe = new Pipe();
		var reader = new FrameReader();
		var reading = CollectAsync(reader, pipe.Reader, ct);

		var random = new Random(20261104);
		for (var offset = 0; offset < stream.Length;)
		{
			var size = Math.Min(random.Next(1, 600), stream.Length - offset);
			await pipe.Writer.WriteAsync(stream.AsMemory(offset, size), ct);
			offset += size;
		}

		await pipe.Writer.CompleteAsync();
		var yielded = await reading;

		yielded.Should().BeEquivalentTo(frames, options => options.WithStrictOrdering());
		reader.DiscardedBytes.Should().Be(0);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task TwoWholeFramesInOneWriteAreBothYieldedWhileTheWriterStaysOpen()
	{
		var ct = TestContext.Current.CancellationToken;
		var pipe = new Pipe();
		var bytes = Concat(FrameEncoder.Encode(Heartbeat(1)), FrameEncoder.Encode(Heartbeat(2)));
		await pipe.Writer.WriteAsync(bytes, ct);

		await using var frames = new FrameReader().ReadAllAsync(pipe.Reader, ct).GetAsyncEnumerator(ct);

		(await frames.MoveNextAsync()).Should().BeTrue();
		frames.Current.Should().Be(Heartbeat(1));
		(await frames.MoveNextAsync()).Should().BeTrue();
		frames.Current.Should().Be(Heartbeat(2));
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task WriterCompletionEndsTheEnumerationAndLeavesAPartialFrameUnread()
	{
		var ct = TestContext.Current.CancellationToken;
		var pipe = new Pipe();
		var partial = FrameEncoder.Encode(Heartbeat(4))[..5];
		await pipe.Writer.WriteAsync(Concat(FrameEncoder.Encode(Heartbeat(3)), partial), ct);
		await pipe.Writer.CompleteAsync();

		var yielded = await CollectAsync(new FrameReader(), pipe.Reader, ct);

		yielded.Should().Equal(Heartbeat(3));
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task CancellationSurfacesAsOperationCanceledException()
	{
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		var pipe = new Pipe();
		var reading = CollectAsync(new FrameReader(), pipe.Reader, cancellation.Token);

		await cancellation.CancelAsync();

		Func<Task> awaitReading = () => reading;
		await awaitReading.Should().ThrowAsync<OperationCanceledException>();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task DiscardedBytesAccumulateAcrossReads()
	{
		var ct = TestContext.Current.CancellationToken;
		var pipe = new Pipe();
		var reader = new FrameReader();
		var reading = CollectAsync(reader, pipe.Reader, ct);

		await pipe.Writer.WriteAsync(Concat([0x01, 0x02, 0x03], FrameEncoder.Encode(Heartbeat(1))), ct);
		await pipe.Writer.WriteAsync(Concat([0x04, 0x05], FrameEncoder.Encode(Heartbeat(2))), ct);
		await pipe.Writer.CompleteAsync();
		var yielded = await reading;

		yielded.Should().Equal(Heartbeat(1), Heartbeat(2));
		reader.DiscardedBytes.Should().Be(5);
	}

	private static async Task<List<BoardFrame>> CollectAsync(
		FrameReader reader,
		PipeReader pipeReader,
		CancellationToken cancellationToken)
	{
		var frames = new List<BoardFrame>();
		await foreach (var frame in reader.ReadAllAsync(pipeReader, cancellationToken))
		{
			frames.Add(frame);
		}

		return frames;
	}
}
