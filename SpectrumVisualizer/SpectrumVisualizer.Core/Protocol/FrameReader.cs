using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace SpectrumVisualizer.Core.Protocol;

/// <summary>Turns a byte pipe into board frames and counts the bytes the decoder dropped.</summary>
public sealed class FrameReader
{
	// Written by the thread that enumerates ReadAllAsync, read from any thread.
	private long _discardedBytes;

	public long DiscardedBytes => Interlocked.Read(ref _discardedBytes);

	/// <summary>Yields frames until the writer completes; cancellation surfaces as OperationCanceledException.</summary>
	public async IAsyncEnumerable<BoardFrame> ReadAllAsync(
		PipeReader reader,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var frames = new List<BoardFrame>();
		while (true)
		{
			var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
			var buffer = result.Buffer;
			DecodeAll(ref buffer, frames);
			reader.AdvanceTo(buffer.Start, buffer.End);

			foreach (var frame in frames)
			{
				yield return frame;
			}

			frames.Clear();
			if (result.IsCompleted)
			{
				yield break;
			}
		}
	}

	private void DecodeAll(ref ReadOnlySequence<byte> buffer, List<BoardFrame> frames)
	{
		long discarded;
		while (FrameDecoder.TryReadFrame(ref buffer, out var frame, out discarded))
		{
			Interlocked.Add(ref _discardedBytes, discarded);
			frames.Add(frame);
		}

		Interlocked.Add(ref _discardedBytes, discarded);
	}
}
