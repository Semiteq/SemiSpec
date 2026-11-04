using System.Threading.Channels;

namespace SpectrumVisualizer.Tests.Unit.UI.Link;

/// <summary>
/// A port stream whose reads return the steps a test pushes, in order; a read waits until a step arrives.
/// </summary>
internal sealed class FakePortStream : Stream
{
	private readonly Channel<object> _steps = Channel.CreateUnbounded<object>();
	private ReadOnlyMemory<byte> _remainder;

	// Written by the link's session thread, read by the test thread.
	private volatile bool _isDisposed;

	public bool IsDisposed => _isDisposed;

	public override bool CanRead => true;

	public override bool CanSeek => false;

	public override bool CanWrite => false;

	public override long Length => throw new NotSupportedException();

	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	public void PushBytes(params byte[] bytes)
	{
		if (bytes.Length == 0)
		{
			throw new ArgumentException("A chunk holds at least one byte; use PushZeroRead for an empty read.");
		}

		_steps.Writer.TryWrite(bytes);
	}

	public void PushZeroRead()
	{
		_steps.Writer.TryWrite(Array.Empty<byte>());
	}

	public void PushTimeout()
	{
		_steps.Writer.TryWrite(new TimeoutException("the read timed out"));
	}

	public void PushException(Exception exception)
	{
		_steps.Writer.TryWrite(exception);
	}

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		if (_remainder.IsEmpty)
		{
			switch (await _steps.Reader.ReadAsync(cancellationToken))
			{
				case Exception exception:
					throw exception;
				case byte[] bytes:
					_remainder = bytes;
					break;
			}
		}

		var count = Math.Min(buffer.Length, _remainder.Length);
		_remainder[..count].CopyTo(buffer);
		_remainder = _remainder[count..];

		return count;
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		throw new NotSupportedException();
	}

	public override void Flush()
	{
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		throw new NotSupportedException();
	}

	public override void SetLength(long value)
	{
		throw new NotSupportedException();
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		throw new NotSupportedException();
	}

	protected override void Dispose(bool disposing)
	{
		_isDisposed = true;
		base.Dispose(disposing);
	}
}
