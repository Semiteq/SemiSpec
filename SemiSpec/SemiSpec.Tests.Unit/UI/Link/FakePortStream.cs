using System.Threading.Channels;

namespace SemiSpec.Tests.Unit.UI.Link;

/// <summary>
/// A port stream whose reads return the steps a test pushes, in order; a read waits until a step arrives.
/// </summary>
internal sealed class FakePortStream : Stream
{
	private readonly Channel<object> _steps = Channel.CreateUnbounded<object>();
	private ReadOnlyMemory<byte> _remainder;

	// The test thread pushes and waits, the link's session thread reads; _gate guards the two fields below.
	private readonly Lock _gate = new();
	private bool _readPending;
	private TaskCompletionSource? _readPendingWaiter;

	// Written by the link's session thread, read by the test thread.
	private volatile bool _isDisposed;

	// Released by default; a test that holds it releases it from the test thread, the session thread awaits it.
	private volatile TaskCompletionSource _disposeRelease = CompletedRelease();

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

	/// <summary>Holds the next DisposeAsync until <see cref="ReleaseDispose"/>.</summary>
	public void HoldDispose()
	{
		_disposeRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	public void ReleaseDispose()
	{
		_disposeRelease.TrySetResult();
	}

	public void PushBytes(params byte[] bytes)
	{
		if (bytes.Length == 0)
		{
			throw new ArgumentException("A chunk holds at least one byte; use PushZeroRead for an empty read.");
		}

		Push(bytes);
	}

	public void PushZeroRead()
	{
		Push(Array.Empty<byte>());
	}

	public void PushTimeout()
	{
		Push(new TimeoutException("the read timed out"));
	}

	public void PushException(Exception exception)
	{
		Push(exception);
	}

	/// <summary>Completes once the reader has consumed every pushed step and waits for the next one.</summary>
	public Task WaitForPendingReadAsync()
	{
		lock (_gate)
		{
			if (_readPending)
			{
				return Task.CompletedTask;
			}

			_readPendingWaiter ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			return _readPendingWaiter.Task;
		}
	}

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		if (_remainder.IsEmpty)
		{
			switch (TakeStep() ?? await _steps.Reader.ReadAsync(cancellationToken))
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

	private void Push(object step)
	{
		lock (_gate)
		{
			_readPending = false;
			_steps.Writer.TryWrite(step);
		}
	}

	/// <summary>Returns a step already pushed, or null after signalling that the read now waits.</summary>
	private object? TakeStep()
	{
		lock (_gate)
		{
			if (_steps.Reader.TryRead(out var step))
			{
				return step;
			}

			_readPending = true;
			_readPendingWaiter?.TrySetResult();
			_readPendingWaiter = null;
			return null;
		}
	}

	public override async ValueTask DisposeAsync()
	{
		await _disposeRelease.Task;
		await base.DisposeAsync();
	}

	protected override void Dispose(bool disposing)
	{
		_isDisposed = true;
		base.Dispose(disposing);
	}

	private static TaskCompletionSource CompletedRelease()
	{
		var release = new TaskCompletionSource();
		release.SetResult();
		return release;
	}
}
