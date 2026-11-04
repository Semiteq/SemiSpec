using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Channels;

using FluentResults;

using Microsoft.Extensions.Logging;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Messages;

namespace SpectrumVisualizer.UI.Store;

/// <summary>The spectrum store file of this running instance.</summary>
public sealed record StoreFile(string Path);

/// <summary>
/// Stores every frame the distributor hands out: the subscriber only enqueues, and one writer task drains the queue
/// into the store file in batches, deletes rows older than a day once a minute, and counts every frame it loses.
/// </summary>
public sealed class SpectrumStore : IDisposable
{
	private const int QueueCapacity = 1024;
	private const int BatchSize = 256;

	private static readonly TimeSpan _retentionInterval = TimeSpan.FromMinutes(1);
	private static readonly TimeSpan _retentionAge = TimeSpan.FromHours(24);

	private readonly FrameDistributor _distributor;
	private readonly StoreFile _storeFile;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly IScheduler _uiScheduler;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<SpectrumStore> _logger;
	private readonly Channel<ReceivedFrame> _queue = Channel.CreateBounded<ReceivedFrame>(
		new BoundedChannelOptions(QueueCapacity)
		{
			FullMode = BoundedChannelFullMode.Wait,
			SingleWriter = true,
			SingleReader = true
		});

	// Written by the distributor task and the writer task, read from any thread.
	private readonly ISubject<Unit> _droppedFramesChanged = Subject.Synchronize(new Subject<Unit>());
	private long _droppedFrames;

	// Start, StopAsync and Dispose run on the UI thread.
	private IDisposable? _subscription;
	private Task _writer = Task.CompletedTask;
	private bool _started;

	// Created by Start on the UI thread; cancelled by Dispose and the drain token, disposed by the writer task when it
	// ends. Each touch holds the gate, so no cancel meets a disposed source.
	private readonly Lock _cancellationGate = new();
	private CancellationTokenSource? _cancellation;
	private CancellationTokenSource? _drainCancellation;

	// Set by StopAsync and Dispose on the UI thread, read by the writer task.
	private volatile bool _stopped;

	// Written and read by the writer task only.
	private long _storedFrames;
	private bool _writeFailing;
	private StopCheckpoint _checkpoint;

	public SpectrumStore(
		FrameDistributor distributor,
		StoreFile storeFile,
		MessagePanelViewModel messagePanel,
		IScheduler uiScheduler,
		TimeProvider timeProvider,
		ILogger<SpectrumStore> logger)
	{
		_distributor = distributor;
		_storeFile = storeFile;
		_messagePanel = messagePanel;
		_uiScheduler = uiScheduler;
		_timeProvider = timeProvider;
		_logger = logger;
		DroppedFramesChanged = _droppedFramesChanged.AsObservable();
	}

	/// <summary>Frames lost to a full queue, a failed batch, a failed store or a stop past its budget, since the start.</summary>
	public long DroppedFrames => Interlocked.Read(ref _droppedFrames);

	/// <summary>Signals after each change of DroppedFrames, on the thread that counted; read the count after it.</summary>
	public IObservable<Unit> DroppedFramesChanged { get; }

	/// <summary>Subscribes to the distributor and starts the writer task once; does nothing after a start or a stop.</summary>
	public void Start()
	{
		if (_started || _stopped)
		{
			return;
		}

		_started = true;
		_cancellation = new CancellationTokenSource();
		_drainCancellation = new CancellationTokenSource();
		var cancellationToken = _cancellation.Token;
		var drainToken = _drainCancellation.Token;
		_subscription = _distributor.Frames.Subscribe(Enqueue);
		_writer = Task.Run(() => RunWriterAsync(cancellationToken, drainToken), CancellationToken.None);
	}

	/// <summary>
	/// Stops taking frames and completes once the writer has written the queue and closed the file; from the stop on a
	/// held lock fails a batch after 1 s, and when drainToken fires the frames still queued count as dropped. Completes
	/// at once when nothing started; a later call completes with the writer of the first.
	/// </summary>
	public async Task StopAsync(CancellationToken drainToken = default)
	{
		_stopped = true;
		if (_subscription is not { } subscription)
		{
			await _writer.ConfigureAwait(false);
			return;
		}

		_subscription = null;
		subscription.Dispose();
		_queue.Writer.TryComplete();
		await using (drainToken.Register(CancelDrain))
		{
			await _writer.ConfigureAwait(false);
		}
	}

	/// <summary>Cancels the writer without waiting, for the container's synchronous dispose; the writer closes the file.</summary>
	public void Dispose()
	{
		_stopped = true;
		_subscription?.Dispose();
		_subscription = null;
		lock (_cancellationGate)
		{
			_cancellation?.Cancel();
		}
	}

	private void CancelDrain()
	{
		lock (_cancellationGate)
		{
			_drainCancellation?.Cancel();
		}
	}

	private void Enqueue(ReceivedFrame received)
	{
		if (!_queue.Writer.TryWrite(received))
		{
			CountDropped(1);
		}
	}

	private void CountDropped(long count)
	{
		if (count == 0)
		{
			return;
		}

		Interlocked.Add(ref _droppedFrames, count);
		_droppedFramesChanged.OnNext(Unit.Default);
	}

	private async Task RunWriterAsync(CancellationToken cancellationToken, CancellationToken drainToken)
	{
		try
		{
			if (!await StoreAsync(cancellationToken, drainToken).ConfigureAwait(false))
			{
				await DiscardUntilCompletedAsync(cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Dispose ends the writer this way; the file is already closed without a checkpoint.
			CountDropped(DiscardQueued());
		}
		finally
		{
			ReleaseCancellation();
			_logger.LogDebug(
				"The spectrum store stopped: {Stored} frames stored, {Dropped} frames dropped, checkpoint {Checkpoint}",
				_storedFrames,
				DroppedFrames,
				_checkpoint);
		}
	}

	/// <summary>Opens the file and writes the queue into it until the queue completes; false after a failure that ends history.</summary>
	private async Task<bool> StoreAsync(CancellationToken cancellationToken, CancellationToken drainToken)
	{
		SpectrumDatabase? database = null;
		try
		{
			database = OpenDatabase();
			if (database is null)
			{
				return false;
			}

			await WriteQueuedAsync(database, cancellationToken, drainToken).ConfigureAwait(false);
			CheckpointAndClose(database);
			database = null;
			return true;
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			ReportFailure(exception, opened: database is not null);
			return false;
		}
		finally
		{
			database?.Dispose();
		}
	}

	/// <summary>Counts every frame still queued and every later one as dropped until the queue completes.</summary>
	private async Task DiscardUntilCompletedAsync(CancellationToken cancellationToken)
	{
		do
		{
			CountDropped(DiscardQueued());
		}
		while (await _queue.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false));
	}

	private SpectrumDatabase? OpenDatabase()
	{
		var path = _storeFile.Path;
		var opened = StoreFiles.Prepare(path).Bind(sweep =>
		{
			LogSweep(sweep);
			return SpectrumDatabase.Open(path);
		});
		if (opened.IsFailed)
		{
			_logger.LogError(CauseOf(opened.Errors), "The spectrum store file {Path} could not be opened", path);
			_messagePanel.TryReportFailure(new StoreOpenError(path).CausedBy(opened.Errors), _logger, _uiScheduler);
			return null;
		}

		_logger.LogInformation("Spectrum store file {Path}", path);
		return opened.Value;
	}

	private void LogSweep(StoreSweep sweep)
	{
		if (sweep.Failure is { } failure)
		{
			_logger.LogWarning(failure, "The spectrum store sweep could not list the store folder; it deleted nothing");
			return;
		}

		_logger.LogDebug(
			"The spectrum store sweep deleted {Deleted} files and kept {Kept} files of live instances",
			sweep.DeletedFiles,
			sweep.KeptFiles);
	}

	private void ReportFailure(Exception exception, bool opened)
	{
		if (!opened)
		{
			_logger.LogError(exception, "The spectrum store file {Path} could not be opened", _storeFile.Path);
			_messagePanel.TryReportFailure(new StoreOpenError(_storeFile.Path).CausedBy(exception), _logger, _uiScheduler);
			return;
		}

		_logger.LogError(exception, "The spectrum store writer failed; every later frame counts as dropped");
		_messagePanel.TryReportFailure(new StoreWriteError().CausedBy(exception), _logger, _uiScheduler);
	}

	private async Task WriteQueuedAsync(
		SpectrumDatabase database, CancellationToken cancellationToken, CancellationToken drainToken)
	{
		var batch = new List<ReceivedFrame>(BatchSize);
		using var retentionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var retentionDue = Task.Delay(_retentionInterval, _timeProvider, retentionCancellation.Token);
		Task<bool>? readable = null;
		try
		{
			while (true)
			{
				// Each pending wait is kept until it completes: the queue allows one reader wait at a time.
				readable ??= _queue.Reader.WaitToReadAsync(cancellationToken).AsTask();
				await Task.WhenAny(readable, retentionDue).ConfigureAwait(false);
				if (_stopped)
				{
					database.ShortenBusyTimeout();
				}

				if (retentionDue.IsCompleted)
				{
					await retentionDue.ConfigureAwait(false);
					DeleteExpired(database);
					retentionDue = Task.Delay(_retentionInterval, _timeProvider, retentionCancellation.Token);
				}

				if (!readable.IsCompleted)
				{
					continue;
				}

				if (!await readable.ConfigureAwait(false))
				{
					return;
				}

				readable = null;
				if (drainToken.IsCancellationRequested)
				{
					CountDropped(DiscardQueued());
					return;
				}

				WriteBatch(database, batch);
			}
		}
		finally
		{
			await retentionCancellation.CancelAsync().ConfigureAwait(false);
		}
	}

	private void WriteBatch(SpectrumDatabase database, List<ReceivedFrame> batch)
	{
		batch.Clear();
		while (batch.Count < BatchSize && _queue.Reader.TryRead(out var received))
		{
			batch.Add(received);
		}

		Result written;
		try
		{
			written = database.Write(batch);
		}
		catch (Exception)
		{
			CountDropped(batch.Count);
			throw;
		}

		if (written.IsSuccess)
		{
			_storedFrames += batch.Count;
			_writeFailing = false;
			return;
		}

		CountDropped(batch.Count);
		_logger.LogError(
			CauseOf(written.Errors),
			"A batch of {Count} frames could not be written to the spectrum store",
			batch.Count);
		if (_writeFailing)
		{
			return;
		}

		_writeFailing = true;
		_messagePanel.TryReportFailure(new StoreWriteError().CausedBy(written.Errors), _logger, _uiScheduler);
	}

	private void DeleteExpired(SpectrumDatabase database)
	{
		var cutoff = _timeProvider.GetUtcNow() - _retentionAge;
		var deleted = database.DeleteBefore(cutoff);
		if (deleted.IsFailed)
		{
			_logger.LogWarning(
				CauseOf(deleted.Errors),
				"The spectrum store rows received before {Cutoff:O} could not be deleted",
				cutoff);
		}
	}

	private long DiscardQueued()
	{
		var discarded = 0L;
		while (_queue.Reader.TryRead(out _))
		{
			discarded++;
		}

		return discarded;
	}

	private void CheckpointAndClose(SpectrumDatabase database)
	{
		var closed = database.CheckpointAndClose();
		if (closed.IsFailed)
		{
			_checkpoint = StopCheckpoint.Failed;
			_logger.LogWarning(CauseOf(closed.Errors), "The spectrum store WAL could not be checkpointed");
			return;
		}

		_checkpoint = closed.Value == WalCheckpoint.Busy ? StopCheckpoint.Busy : StopCheckpoint.Truncated;
	}

	private void ReleaseCancellation()
	{
		lock (_cancellationGate)
		{
			_cancellation?.Dispose();
			_cancellation = null;
			_drainCancellation?.Dispose();
			_drainCancellation = null;
		}
	}

	private static Exception? CauseOf(IEnumerable<IError> errors)
	{
		foreach (var error in errors)
		{
			if (error is IExceptionalError exceptional)
			{
				return exceptional.Exception;
			}

			if (CauseOf(error.Reasons) is { } cause)
			{
				return cause;
			}
		}

		return null;
	}

	/// <summary>The writer's last closing step, named in its stopped line.</summary>
	private enum StopCheckpoint
	{
		Skipped,
		Truncated,
		Busy,
		Failed,
	}
}
