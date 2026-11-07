using System.Reactive.Concurrency;

using FluentResults;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

using SemiSpec.Core.Store;
using SemiSpec.UI.Messages;

namespace SemiSpec.UI.Store;

/// <summary>
/// The writer's open sequence: prepares the folder, opens the history file, replaces a file of another schema
/// version with a fresh one and sets an unreadable or damage-marked file aside before it starts a fresh one.
/// </summary>
internal sealed class StoreOpening(
	StoreFile storeFile,
	MessagePanelViewModel messagePanel,
	IScheduler uiScheduler,
	TimeProvider timeProvider,
	ILogger logger)
{
	private const int SqliteBusy = 5;
	private const int SqliteLocked = 6;

	private static readonly TimeSpan _busyRetryInterval = TimeSpan.FromSeconds(0.5);

	// Counted from the first busy failure, which itself waited out the 2 s busy timeout: 6 s from the start of the
	// open, past the 5 s close limit of a closing copy of the app that holds the file.
	private static readonly TimeSpan _busyRetryBudget = TimeSpan.FromSeconds(4);

	/// <summary>A file another connection holds is retried, never replaced; giveUp ends the retries early.</summary>
	public async Task<Result<SpectrumDatabase>> OpenAsync(CancellationToken giveUp)
	{
		var prepared = StoreFiles.Prepare(storeFile.Path);
		if (prepared.IsFailed)
		{
			return prepared.ToResult();
		}

		LogLegacyFilesDeleted(prepared.Value);
		if (StoreFiles.IsMarkedDamaged(storeFile.Path))
		{
			return ReplaceDamaged(
				new Error("A failed read or write of an earlier run marked the spectrum store file as damaged"));
		}

		var opened = await OpenWhileBusyAsync(giveUp).ConfigureAwait(false);
		if (opened.HasError<StoreSchemaMismatchError>(out var mismatches))
		{
			return ReplaceMismatched(mismatches.First());
		}

		return opened.HasError<StoreFileUnreadableError>(out var unreadable)
			? ReplaceDamaged(unreadable.First())
			: opened;
	}

	private async Task<Result<SpectrumDatabase>> OpenWhileBusyAsync(CancellationToken giveUp)
	{
		long? firstBusy = null;
		while (true)
		{
			var opened = SpectrumDatabase.Open(storeFile.Path);
			if (!IsBusy(opened))
			{
				return opened;
			}

			firstBusy ??= timeProvider.GetTimestamp();
			if (timeProvider.GetElapsedTime(firstBusy.Value) >= _busyRetryBudget)
			{
				return opened;
			}

			logger.LogDebug("The spectrum store file {Path} is held by another connection; retrying", storeFile.Path);
			try
			{
				await Task.Delay(_busyRetryInterval, timeProvider, giveUp).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (giveUp.IsCancellationRequested)
			{
				return opened;
			}
		}
	}

	private static bool IsBusy(Result<SpectrumDatabase> opened)
	{
		return opened.HasException<SqliteException>(
			exception => exception.SqliteErrorCode is SqliteBusy or SqliteLocked);
	}

	private void LogLegacyFilesDeleted(int legacyFiles)
	{
		if (legacyFiles > 0)
		{
			logger.LogInformation("The spectrum store deleted {Count} store files of older builds", legacyFiles);
		}
	}

	/// <summary>
	/// Deletes the file of another version without a panel entry, since an app update causes it and the operator can
	/// do nothing about it, then opens a fresh one.
	/// </summary>
	private Result<SpectrumDatabase> ReplaceMismatched(StoreSchemaMismatchError mismatch)
	{
		try
		{
			StoreFiles.DeleteWithSidecars(storeFile.Path);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return Result.Fail(new ExceptionalError(
				$"The spectrum store file '{storeFile.Path}' of schema version {mismatch.Found} could not be deleted",
				ex));
		}

		logger.LogWarning(
			"The spectrum store file {Path} had schema version {Found}, this build uses {Expected}; "
				+ "the file was deleted and a new one started",
			storeFile.Path,
			mismatch.Found,
			SpectrumDatabase.SchemaVersion);
		return SpectrumDatabase.Open(storeFile.Path);
	}

	/// <summary>
	/// Sets the unreadable or marked file aside under the operator's local time, opens a fresh one and reports the
	/// replacement.
	/// </summary>
	private Result<SpectrumDatabase> ReplaceDamaged(IError damage)
	{
		var setAside = StoreFiles.SetAside(storeFile.Path, timeProvider.GetLocalNow());
		if (setAside.IsFailed)
		{
			return Result.Fail(damage).WithErrors(setAside.Errors);
		}

		var reopened = SpectrumDatabase.Open(storeFile.Path);
		if (reopened.IsFailed)
		{
			return reopened;
		}

		logger.LogWarning(
			"The spectrum store file {Path} was set aside as {SetAsidePath} and a new one started: {Damage}",
			storeFile.Path,
			setAside.Value,
			damage.Message);
		messagePanel.TryReportFailure(
			new StoreFileReplacedError(storeFile.Path, setAside.Value).CausedBy(damage),
			logger,
			uiScheduler);
		return reopened;
	}
}
