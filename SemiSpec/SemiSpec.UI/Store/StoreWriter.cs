using System.Reactive.Concurrency;
using System.Threading.Channels;

using FluentResults;

using Microsoft.Extensions.Logging;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Store;
using SemiSpec.UI.Messages;

namespace SemiSpec.UI.Store;

/// <summary>
/// The writer task of the spectrum store: drains the queue into the store file in batches, runs the retention pass
/// at the open and once a minute and closes the file with a checkpoint; every frame it loses goes to countDropped.
/// It completes opened true once the open and the retention at open ran, false when it ends without that.
/// </summary>
internal sealed class StoreWriter(
	StoreFile storeFile,
	ChannelReader<ReceivedFrame> queue,
	Action<long> countDropped,
	TaskCompletionSource<bool> opened,
	MessagePanelViewModel messagePanel,
	IScheduler uiScheduler,
	TimeProvider timeProvider,
	ILogger logger)
{
	private const int BatchSize = 256;

	private static readonly TimeSpan _retentionInterval = TimeSpan.FromMinutes(1);

	private readonly StoreOpening _opening = new(storeFile, messagePanel, uiScheduler, timeProvider, logger);
	private readonly StoreRetention _retention = new(storeFile, timeProvider, logger);

	// Set by Stop on the UI thread, read by the writer task.
	private volatile bool _stopped;

	// Written by the writer task; read once it ended.
	private int _failedBatches;
	private long _failedFrames;
	private Result<WalCheckpoint>? _checkpoint;

	public long StoredFrames { get; private set; }

	/// <summary>The closing checkpoint as the stopped line names it: Skipped, Truncated, Busy or Failed.</summary>
	public string CheckpointOutcome => _checkpoint switch
	{
		null => "Skipped",
		{ IsFailed: true } => "Failed",
		{ } closed => closed.Value.ToString(),
	};

	/// <summary>From now on no retention pass starts, and a statement gives up on a held lock after 1 s.</summary>
	public void Stop()
	{
		_stopped = true;
	}

	/// <summary>
	/// Writes the queue until it completes; after a failure, or once cancelled, counts every frame as dropped.
	/// </summary>
	public async Task RunAsync(CancellationToken cancellationToken, CancellationToken drainToken)
	{
		try
		{
			if (!await StoreAsync(cancellationToken, drainToken).ConfigureAwait(false))
			{
				opened.TrySetResult(false);
				await DiscardUntilCompletedAsync(cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Dispose ends the writer this way; the file is already closed without a checkpoint.
			countDropped(DiscardQueued());
		}
		finally
		{
			opened.TrySetResult(false);
		}
	}

	/// <summary>Opens the file and writes the queue into it until the queue completes; false after a failure.</summary>
	private async Task<bool> StoreAsync(CancellationToken cancellationToken, CancellationToken drainToken)
	{
		SpectrumDatabase? database = null;
		try
		{
			var openResult = _opening.Open();
			if (openResult.IsFailed)
			{
				ReportOpenFailure(openResult.Errors);
				return false;
			}

			database = openResult.Value;
			RunRetentionUnlessStopped(database);
			logger.LogInformation("Spectrum store file {Path}", storeFile.Path);
			cancellationToken.ThrowIfCancellationRequested();
			opened.TrySetResult(true);
			await WriteQueuedAsync(database, cancellationToken, drainToken).ConfigureAwait(false);
			CheckpointAndClose(database);
			database = null;
			return true;
		}
		catch (Exception exception)
			when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			if (database is null)
			{
				ReportOpenFailure([new ExceptionalError(exception)]);
			}
			else
			{
				messagePanel.TryReportFailure(new StoreStoppedError().CausedBy(exception), logger, uiScheduler);
			}

			return false;
		}
		finally
		{
			database?.Dispose();
		}
	}

	private async Task DiscardUntilCompletedAsync(CancellationToken cancellationToken)
	{
		do
		{
			countDropped(DiscardQueued());
		}
		while (await queue.WaitToReadAsync(cancellationToken).ConfigureAwait(false));
	}

	private void RunRetentionUnlessStopped(SpectrumDatabase database)
	{
		if (!_stopped)
		{
			_retention.Run(database);
		}
	}

	private void ReportOpenFailure(IEnumerable<IError> causes)
	{
		messagePanel.TryReportFailure(new StoreOpenError(storeFile.Path).CausedBy(causes), logger, uiScheduler);
	}

	private async Task WriteQueuedAsync(
		SpectrumDatabase database, CancellationToken cancellationToken, CancellationToken drainToken)
	{
		var batch = new List<ReceivedFrame>(BatchSize);
		using var retentionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var retentionDue = Task.Delay(_retentionInterval, timeProvider, retentionCancellation.Token);
		Task<bool>? readable = null;
		try
		{
			while (true)
			{
				// Each pending wait is kept until it completes: the queue allows one reader wait at a time.
				readable ??= queue.WaitToReadAsync(cancellationToken).AsTask();
				await Task.WhenAny(readable, retentionDue).ConfigureAwait(false);
				if (retentionDue.IsCompleted)
				{
					await retentionDue.ConfigureAwait(false);
					RunRetentionUnlessStopped(database);
					retentionDue = Task.Delay(_retentionInterval, timeProvider, retentionCancellation.Token);
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
					countDropped(DiscardQueued());
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
		ShortenBusyTimeoutOnceStopped(database);
		batch.Clear();
		while (batch.Count < BatchSize && queue.TryRead(out var received))
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
			countDropped(batch.Count);
			throw;
		}

		if (written.IsSuccess)
		{
			StoredFrames += batch.Count;
			EndFailureIncident();
			return;
		}

		countDropped(batch.Count);
		MarkOnDamage(written.Errors);
		_failedBatches++;
		_failedFrames += batch.Count;
		if (_failedBatches == 1)
		{
			messagePanel.TryReportFailure(new StoreWriteError().CausedBy(written.Errors), logger, uiScheduler);
			return;
		}

		logger.LogDebug("A batch of {Count} frames could not be written to the spectrum store", batch.Count);
	}

	/// <summary>
	/// One Information line closes a run of failed batches, whose repeats logged at Debug only, so a failing disk
	/// cannot roll the run's first Warning out of the log.
	/// </summary>
	private void EndFailureIncident()
	{
		if (_failedBatches == 0)
		{
			return;
		}

		logger.LogInformation(
			"Spectrum store writes recovered after {Failures} failed batches, {Dropped} frames dropped",
			_failedBatches,
			_failedFrames);
		_failedBatches = 0;
		_failedFrames = 0;
	}

	private long DiscardQueued()
	{
		var discarded = 0L;
		while (queue.TryRead(out _))
		{
			discarded++;
		}

		return discarded;
	}

	private void ShortenBusyTimeoutOnceStopped(SpectrumDatabase database)
	{
		if (_stopped)
		{
			database.ShortenBusyTimeout();
		}
	}

	private void CheckpointAndClose(SpectrumDatabase database)
	{
		ShortenBusyTimeoutOnceStopped(database);
		var closed = database.CheckpointAndClose();
		_checkpoint = closed;
		if (closed.IsFailed)
		{
			MarkOnDamage(closed.Errors);
			logger.LogWarning(
				ResultReporting.CauseOf(closed.Errors),
				"The spectrum store WAL could not be checkpointed");
		}
	}

	/// <summary>The running writer keeps the file; the marker makes the next open set it aside.</summary>
	private void MarkOnDamage(IEnumerable<IError> errors)
	{
		if (StoreDamage.IsDamage(errors))
		{
			StoreFiles.MarkDamaged(storeFile.Path);
		}
	}
}
