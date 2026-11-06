using Microsoft.Extensions.Logging;

using SemiSpec.Core.Store;
using SemiSpec.UI.Messages;

namespace SemiSpec.UI.Store;

/// <summary>
/// The writer's retention pass: deletes the rows older than a day and, once an hour, the store files of other
/// instances. A run of failures logs one Warning with the stack, Debug repeats and one Information line on recovery.
/// </summary>
internal sealed class StoreRetention(StoreFile storeFile, TimeProvider timeProvider, ILogger logger)
{
	private static readonly TimeSpan _retentionAge = TimeSpan.FromHours(24);
	private static readonly TimeSpan _sweepInterval = TimeSpan.FromHours(1);

	// Read and written by the writer task only.
	private DateTimeOffset? _lastSweepAt;
	private int _failedDeletes;
	private int _failedSweeps;

	/// <summary>Reads the clock once, deletes what expired, and sweeps when an hour passed since the last sweep.</summary>
	public void Run(SpectrumDatabase database)
	{
		var now = timeProvider.GetUtcNow();
		DeleteExpired(database, now - _retentionAge);
		if (_lastSweepAt is null)
		{
			_lastSweepAt = now;
		}
		else if (now - _lastSweepAt >= _sweepInterval)
		{
			_lastSweepAt = now;
			LogSweep(StoreFiles.Sweep(storeFile.Path));
		}
	}

	public void LogSweep(StoreSweep sweep)
	{
		if (sweep.Failure is { } failure)
		{
			if (++_failedSweeps == 1)
			{
				logger.LogWarning(
					failure.Exception,
					"The spectrum store sweep could not list the store folder; it deleted nothing");
			}
			else
			{
				logger.LogDebug("The spectrum store sweep could not list the store folder again");
			}

			return;
		}

		if (_failedSweeps > 0)
		{
			logger.LogInformation("The spectrum store sweep recovered after {Failures} failed sweeps", _failedSweeps);
			_failedSweeps = 0;
		}

		logger.LogDebug(
			"The spectrum store sweep deleted {Deleted} files and kept {Kept} files of live instances",
			sweep.DeletedFiles,
			sweep.KeptFiles);
	}

	private void DeleteExpired(SpectrumDatabase database, DateTimeOffset cutoff)
	{
		var deleted = database.DeleteBefore(cutoff);
		if (deleted.IsFailed)
		{
			if (++_failedDeletes == 1)
			{
				logger.LogWarning(
					ResultReporting.CauseOf(deleted.Errors),
					"The spectrum store rows received before {Cutoff:O} could not be deleted",
					cutoff);
			}
			else
			{
				logger.LogDebug("The spectrum store rows received before {Cutoff:O} could not be deleted", cutoff);
			}

			return;
		}

		if (_failedDeletes > 0)
		{
			logger.LogInformation("Spectrum store retention recovered after {Failures} failed passes", _failedDeletes);
			_failedDeletes = 0;
		}
	}
}
