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

	// Start, StopAsync and Dispose run on the UI thread; the writer task only disposes the subscription Start made.
	private IDisposable? _subscription;
	private CancellationTokenSource? _cancellation;
	private CancellationTokenSource? _drainCancellation;
	private Task _writer = Task.CompletedTask;
	private bool _stopped;

	// Written and read by the writer task only.
	private long _storedFrames;
	private bool _writeFailing;

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

	/// <summary>Frames lost to a full queue, a failed batch or a stop past its budget, since the start.</summary>
	public long DroppedFrames => Interlocked.Read(ref _droppedFrames);

	/// <summary>Signals after each change of DroppedFrames, on the thread that counted; read the count after it.</summary>
	public IObservable<Unit> DroppedFramesChanged { get; }

	/// <summary>Subscribes to the distributor and starts the writer task once; does nothing after a start or a stop.</summary>
	public void Start()
	{
		if (_cancellation is not null || _stopped)
		{
			return;
		}

		_cancellation = new CancellationTokenSource();
		_drainCancellation = new CancellationTokenSource();
		var cancellationToken = _cancellation.Token;
		var drainToken = _drainCancellation.Token;
		_subscription = _distributor.Frames.Subscribe(Enqueue);
		_writer = Task.Run(() => RunWriterAsync(cancellationToken, drainToken), CancellationToken.None);
	}

	/// <summary>
	/// Stops taking frames and completes once the writer has written the queue and closed the file; when drainToken
	/// fires first, the frames still queued count as dropped. Completes at once when nothing started.
	/// </summary>
	public async Task StopAsync(CancellationToken drainToken = default)
	{
		_stopped = true;
		if (_cancellation is not { } cancellation || _drainCancellation is not { } drainCancellation)
		{
			return;
		}

		_cancellation = null;
		_drainCancellation = null;
		_subscription?.Dispose();
		_queue.Writer.TryComplete();
		await using (drainToken.Register(drainCancellation.Cancel))
		{
			await _writer.ConfigureAwait(false);
		}

		cancellation.Dispose();
		drainCancellation.Dispose();
	}

	/// <summary>Cancels the writer without waiting, for the container's synchronous dispose; the writer closes the file.</summary>
	public void Dispose()
	{
		_stopped = true;
		_cancellation?.Cancel();
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
		if (OpenDatabase() is not { } database)
		{
			_subscription?.Dispose();
			return;
		}

		var drained = false;
		try
		{
			await WriteQueuedAsync(database, cancellationToken, drainToken).ConfigureAwait(false);
			drained = true;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Dispose ends the writer this way; the file closes below without a checkpoint.
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "The spectrum store writer stopped on a failure");
		}
		finally
		{
			_subscription?.Dispose();
			Close(database, checkpoint: drained);
		}
	}

	private SpectrumDatabase? OpenDatabase()
	{
		var path = _storeFile.Path;
		var opened = StoreFiles.Prepare(path).Bind(sweep =>
		{
			_logger.LogDebug(
				"The spectrum store sweep deleted {Deleted} files and kept {Kept} files of live instances",
				sweep.DeletedFiles,
				sweep.KeptFiles);
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

		var written = database.Write(batch);
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

	private void Close(SpectrumDatabase database, bool checkpoint)
	{
		if (!checkpoint)
		{
			database.Dispose();
		}
		else
		{
			var closed = database.CheckpointAndClose();
			if (closed.IsFailed)
			{
				_logger.LogWarning(CauseOf(closed.Errors), "The spectrum store WAL could not be checkpointed");
			}
			else if (closed.Value == WalCheckpoint.Busy)
			{
				_logger.LogDebug("The spectrum store WAL stays: a reader held a snapshot at the checkpoint");
			}
		}

		_logger.LogDebug(
			"The spectrum store stopped: {Stored} frames stored, {Dropped} frames dropped",
			_storedFrames,
			DroppedFrames);
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
}
