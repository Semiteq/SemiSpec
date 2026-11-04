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
	public async Task WriterCompletionEndsTheEnumerationAndCountsAPartialFrameAsDiscarded()
	{
		var ct = TestContext.Current.CancellationToken;
		var pipe = new Pipe();
		var reader = new FrameReader();
		var partial = FrameEncoder.Encode(Heartbeat(4))[..5];
		await pipe.Writer.WriteAsync(Concat(FrameEncoder.Encode(Heartbeat(3)), partial), ct);
		await pipe.Writer.CompleteAsync();

		var yielded = await CollectAsync(reader, pipe.Reader, ct);

		yielded.Should().Equal(Heartbeat(3));
		reader.DiscardedBytes.Should().Be(partial.Length);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task APartialFrameMakesTheNextReadWaitForNewBytes()
	{
		var ct = TestContext.Current.CancellationToken;
		var pipe = new Pipe();
		var countingReader = new CountingPipeReader(pipe.Reader);
		var bytes = FrameEncoder.Encode(Heartbeat(5));
		await pipe.Writer.WriteAsync(bytes.AsMemory(0, 5), ct);

		await using var frames = new FrameReader().ReadAllAsync(countingReader, ct).GetAsyncEnumerator(ct);
		var next = frames.MoveNextAsync();

		next.IsCompleted.Should().BeFalse();
		countingReader.ReadCount.Should().Be(2);

		await pipe.Writer.WriteAsync(bytes.AsMemory(5), ct);
		(await next).Should().BeTrue();
		frames.Current.Should().Be(Heartbeat(5));
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task GarbageAloneIsCountedBeforeAnyFrameArrives()
	{
		var ct = TestContext.Current.CancellationToken;
		var pipe = new Pipe();
		var reader = new FrameReader();
		await pipe.Writer.WriteAsync(new byte[] { 0x01, 0x02, 0x03 }, ct);

		await using var frames = reader.ReadAllAsync(pipe.Reader, ct).GetAsyncEnumerator(ct);
		var next = frames.MoveNextAsync();

		reader.DiscardedBytes.Should().Be(3);

		await pipe.Writer.WriteAsync(FrameEncoder.Encode(Heartbeat(1)), ct);
		(await next).Should().BeTrue();
		frames.Current.Should().Be(Heartbeat(1));
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

	/// <summary>Counts ReadAsync calls and fails past a limit, so a reader that spins cannot hang the run.</summary>
	private sealed class CountingPipeReader(PipeReader inner) : PipeReader
	{
		private const int SpinLimit = 10;

		public int ReadCount { get; private set; }

		public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
		{
			ReadCount++;
			return ReadCount > SpinLimit
				? throw new InvalidOperationException($"ReadAsync called {ReadCount} times without new bytes.")
				: inner.ReadAsync(cancellationToken);
		}

		public override bool TryRead(out ReadResult result)
		{
			return inner.TryRead(out result);
		}

		public override void AdvanceTo(SequencePosition consumed)
		{
			inner.AdvanceTo(consumed);
		}

		public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
		{
			inner.AdvanceTo(consumed, examined);
		}

		public override void CancelPendingRead()
		{
			inner.CancelPendingRead();
		}

		public override void Complete(Exception? exception = null)
		{
			inner.Complete(exception);
		}
	}
}
