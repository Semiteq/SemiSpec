using Microsoft.Extensions.Logging;

using SemiSpec.Core.Store;
using SemiSpec.UI.Messages;

namespace SemiSpec.UI.Store;

/// <summary>
/// The writer's retention pass: deletes the rows older than a day. A run of failures logs one Warning with the stack,
/// Debug repeats and one Information line on recovery.
/// </summary>
internal sealed class StoreRetention(TimeProvider timeProvider, ILogger logger)
{
	private static readonly TimeSpan _retentionAge = TimeSpan.FromHours(24);

	// Read and written by the writer task only.
	private int _failedDeletes;

	/// <summary>Reads the clock once and deletes what expired.</summary>
	public void Run(SpectrumDatabase database)
	{
		var cutoff = timeProvider.GetUtcNow() - _retentionAge;
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
