using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;

namespace SemiSpec.Core.Protocol;

/// <summary>Turns a byte pipe into board frames and counts the bytes the decoder dropped.</summary>
public sealed class FrameReader
{
	// Written by the thread that enumerates ReadAllAsync or calls DecodeBuffered, read from any thread.
	private long _discardedBytes;

	public long DiscardedBytes => Interlocked.Read(ref _discardedBytes);

	/// <summary>Yields frames until the writer completes; cancellation throws OperationCanceledException.</summary>
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
			if (result.IsCompleted)
			{
				DrainAfterCompletion(ref buffer, frames);
			}

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

	/// <summary>Decodes the bytes already in the pipe without waiting.</summary>
	public void DecodeBuffered(PipeReader reader, ICollection<BoardFrame> frames)
	{
		if (!reader.TryRead(out var result))
		{
			return;
		}

		var buffer = result.Buffer;
		DecodeAll(ref buffer, frames);
		if (result.IsCompleted)
		{
			DrainAfterCompletion(ref buffer, frames);
		}

		reader.AdvanceTo(buffer.Start, buffer.End);
	}

	private void DecodeAll(ref ReadOnlySequence<byte> buffer, ICollection<BoardFrame> frames)
	{
		long discarded;
		while (FrameDecoder.TryReadFrame(ref buffer, out var frame, out discarded))
		{
			Interlocked.Add(ref _discardedBytes, discarded);
			frames.Add(frame);
		}

		Interlocked.Add(ref _discardedBytes, discarded);
	}

	/// <summary>No more bytes will come, so a pending candidate is invalid: drop one byte and decode again.</summary>
	private void DrainAfterCompletion(ref ReadOnlySequence<byte> buffer, ICollection<BoardFrame> frames)
	{
		while (!buffer.IsEmpty)
		{
			buffer = buffer.Slice(1);
			Interlocked.Increment(ref _discardedBytes);
			DecodeAll(ref buffer, frames);
		}
	}
}
