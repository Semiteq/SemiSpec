using System.IO.Pipelines;
using System.Reactive.Concurrency;
using System.Threading.Channels;

using FluentResults;

using Microsoft.Extensions.Logging;

using SemiSpec.Core.Protocol;

namespace SemiSpec.UI.Link;

/// <summary>One Connect of the link.</summary>
internal sealed class LinkSession(
	string portName,
	ISerialPortProvider serialPortProvider,
	IScheduler scheduler,
	ChannelWriter<ReceivedFrame> frames,
	Action<LinkStatus> publish,
	ILogger logger)
{
	private const int ReadSize = 4096;
	private const int EndOfStream = 0;
	private const int SilentTicksPerPortListCheck = 3;

	private static readonly TimeSpan _reopenInterval = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan _disposeLimit = TimeSpan.FromSeconds(2);

	private static readonly PipeOptions _pipeOptions = new(
		pauseWriterThreshold: 65536,
		resumeWriterThreshold: 32768,
		useSynchronizationContext: false);

	// Monotonic, so a wall clock step never fakes or hides a silence.
	private readonly IStopwatch _stopwatch = scheduler.StartStopwatch();
	private readonly SequenceTracker _sequenceTracker = new();
	private readonly List<BoardFrame> _decoded = [];
	private long _lostSpectra;

	// A run lasts from one Receiving to the next; only its first failed open or port loss carries a fault.
	private bool _runFaultReported;

	private Pipe _pipe = new(_pipeOptions);
	private FrameReader _frameReader = new();
	private TimeSpan _openedAt;
	private TimeSpan? _lastByteAt;
	private TimeSpan? _lastFrameAt;
	private TimeSpan? _lastDrainAt;
	private long _bytesSinceFrame;
	private int _silentTicks;
	private BoardState? _board;
	private LinkState _state;

	/// <summary>Reads the port and reopens it after every loss, until cancelled or an exception escapes.</summary>
	public async Task<IError?> RunAsync(CancellationToken cancellationToken)
	{
		try
		{
			publish(StatusOf(LinkState.Connecting));
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await OpenAndReadAsync(cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return null;
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "The link on port {Port} failed", portName);
			return new ExceptionalError(exception.Message, exception);
		}
	}

	/// <summary>One open attempt: reads until the port is lost, then waits out the reopen interval.</summary>
	private async Task OpenAndReadAsync(CancellationToken cancellationToken)
	{
		logger.LogDebug("Opening port {Port}", portName);
		var opened = serialPortProvider.Open(portName);
		if (opened.IsFailed)
		{
			logger.LogDebug("Opening port {Port} failed: {Reason}", portName, opened.Errors[0].Message);
			await WaitForReopenAsync(opened.Errors[0], cancellationToken).ConfigureAwait(false);
			return;
		}

		var port = opened.Value;
		Task reopen;
		try
		{
			await ReadPortAsync(port, cancellationToken).ConfigureAwait(false);

			// Started before the port closes, so a driver that blocks in dispose cannot hold the last status.
			reopen = WaitForReopenAsync(new PortLostError(portName), cancellationToken);
		}
		finally
		{
			await ClosePortAsync(port).ConfigureAwait(false);
		}

		await reopen.ConfigureAwait(false);
	}

	/// <summary>
	/// Arms the reopen timer before publishing PortUnavailable, so a status reader may move the clock.
	/// </summary>
	private async Task WaitForReopenAsync(IError failure, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var reopen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var timer = scheduler.Schedule(_reopenInterval, () => reopen.TrySetResult());
		using var cancellation = cancellationToken.Register(() => reopen.TrySetCanceled(cancellationToken));

		var fault = _runFaultReported ? null : failure;
		_runFaultReported = true;
		_state = LinkState.PortUnavailable;
		publish(StatusOf(LinkState.PortUnavailable, fault));

		await reopen.Task.ConfigureAwait(false);
	}

	/// <summary>
	/// Returns when the port is lost; an exception or cancellation leaves through the caller's teardown.
	/// </summary>
	private async Task ReadPortAsync(Stream port, CancellationToken cancellationToken)
	{
		StartReading();
		while (true)
		{
			var read = await ReadOnceAsync(port, cancellationToken).ConfigureAwait(false);
			if (read.Outcome == ReadOutcome.PortLost)
			{
				return;
			}

			var now = _stopwatch.Elapsed;
			if (read.Outcome == ReadOutcome.Bytes)
			{
				_silentTicks = 0;
				await AcceptBytesAsync(read.ByteCount, now, cancellationToken).ConfigureAwait(false);
			}
			else if (++_silentTicks % SilentTicksPerPortListCheck == 0 && !IsPortStillListed())
			{
				logger.LogDebug("Port {Port} left the port list", portName);
				return;
			}

			await UpdateHealthAsync(now).ConfigureAwait(false);
		}
	}

	private void StartReading()
	{
		_pipe = new Pipe(_pipeOptions);
		_sequenceTracker.Reset();
		_frameReader = new FrameReader();
		_openedAt = _stopwatch.Elapsed;
		_lastByteAt = null;
		_lastFrameAt = null;
		_lastDrainAt = null;
		_bytesSinceFrame = 0;
		_silentTicks = 0;
		_board = null;
		_state = LinkState.Waiting;
		publish(StatusOf(_state));
	}

	/// <summary>A zero-byte read, a read failure and a closed port are all one lost-port signal.</summary>
	private async Task<PortRead> ReadOnceAsync(Stream port, CancellationToken cancellationToken)
	{
		try
		{
			var memory = _pipe.Writer.GetMemory(ReadSize);
			var count = await port.ReadAsync(memory, cancellationToken).ConfigureAwait(false);
			return count == EndOfStream ? new PortRead(ReadOutcome.PortLost) : new PortRead(ReadOutcome.Bytes, count);
		}
		catch (TimeoutException)
		{
			return new PortRead(ReadOutcome.Timeout);
		}
		catch (Exception exception) when (exception is IOException or InvalidOperationException)
		{
			logger.LogDebug(exception, "A read on port {Port} failed", portName);
			return new PortRead(ReadOutcome.PortLost);
		}
	}

	/// <summary>A port list that cannot be read is no proof of a loss, so the port counts as listed.</summary>
	private bool IsPortStillListed()
	{
		var listed = serialPortProvider.PortNames();
		return listed.IsFailed || listed.Value.Contains(portName, StringComparer.OrdinalIgnoreCase);
	}

	private async Task AcceptBytesAsync(int count, TimeSpan now, CancellationToken cancellationToken)
	{
		_pipe.Writer.Advance(count);
		await _pipe.Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
		_lastByteAt = now;
		_bytesSinceFrame += count;
		_frameReader.DecodeBuffered(_pipe.Reader, _decoded);
		Deliver(now);
	}

	/// <summary>Drains before judging, so a stall that hid real frames never shows a fault state.</summary>
	private async Task UpdateHealthAsync(TimeSpan now)
	{
		if (LinkHealth.IsDrainDue(now, _lastFrameAt ?? _openedAt, _lastDrainAt, _lastByteAt))
		{
			await DrainAsync(now).ConfigureAwait(false);
		}

		var state = LinkHealth.Evaluate(now, _openedAt, _lastByteAt, _lastFrameAt, _bytesSinceFrame);
		var fault = state == _state ? null : FaultOnEntry(state);
		_state = state;
		if (state == LinkState.Receiving)
		{
			_runFaultReported = false;
		}

		publish(StatusOf(state, fault));
	}

	/// <summary>Releases the frames behind a stalled candidate and drops the partial frame in flight.</summary>
	private async Task DrainAsync(TimeSpan now)
	{
		await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
		_frameReader.DecodeBuffered(_pipe.Reader, _decoded);
		await _pipe.Reader.CompleteAsync().ConfigureAwait(false);
		_pipe = new Pipe(_pipeOptions);
		_lastDrainAt = now;
		logger.LogDebug(
			"Drained the decoder of port {Port}, {Count} frames released",
			portName,
			_decoded.Count);
		Deliver(now);
	}

	/// <summary>
	/// The frames of one decode pass or drain share one stamp: the monotonic _lastFrameAt and the wall-clock ReceivedAt.
	/// </summary>
	private void Deliver(TimeSpan now)
	{
		if (_decoded.Count == 0)
		{
			return;
		}

		var receivedAt = scheduler.Now;
		foreach (var frame in _decoded)
		{
			var lost = _sequenceTracker.Observe(frame);
			if (lost > 0)
			{
				_lostSpectra += lost;
				logger.LogInformation(
					"{Lost} spectra lost before seq {Seq} on port {Port}",
					lost,
					frame.Seq,
					portName);
			}

			_board = frame.State;
			frames.TryWrite(new ReceivedFrame(frame, receivedAt));
		}

		_decoded.Clear();
		_lastFrameAt = now;
		_bytesSinceFrame = 0;
	}

	private async Task ClosePortAsync(Stream port)
	{
		try
		{
			await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
			await _pipe.Reader.CompleteAsync().ConfigureAwait(false);
		}
		finally
		{
			await DisposePortAsync(port).ConfigureAwait(false);
		}
	}

	/// <summary>Abandons a dispose that outlasts the limit, so a driver that hangs in it cannot stop the reopens.</summary>
	private async Task DisposePortAsync(Stream port)
	{
		var limit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var timer = scheduler.Schedule(_disposeLimit, () => limit.TrySetResult());

		// RJCP keeps Stream's DisposeAsync, which runs the blocking Dispose on the calling thread.
		var dispose = Task.Run(() => port.DisposeAsync().AsTask());
		if (await Task.WhenAny(dispose, limit.Task).ConfigureAwait(false) == dispose)
		{
			await dispose.ConfigureAwait(false);
			return;
		}

		logger.LogWarning(
			"Closing port {Port} took longer than {Limit}; the port is abandoned",
			portName,
			_disposeLimit);
		_ = dispose.ContinueWith(
			task => logger.LogDebug(task.Exception, "The abandoned port {Port} failed to close", portName),
			CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted,
			TaskScheduler.Default);
	}

	private LinkError? FaultOnEntry(LinkState state)
	{
		return state switch
		{
			LinkState.NotResponding => new BoardNotRespondingError(portName),
			LinkState.OtherDevice => new UnrecognisedDataError(portName),
			_ => null
		};
	}

	private LinkStatus StatusOf(LinkState state, IError? fault = null)
	{
		return new LinkStatus(state, portName, _board, _lostSpectra, fault);
	}

	private enum ReadOutcome
	{
		Bytes,
		Timeout,
		PortLost,
	}

	/// <summary>One read of the port; ByteCount is set for Bytes only.</summary>
	private readonly record struct PortRead(ReadOutcome Outcome, int ByteCount = 0);
}
