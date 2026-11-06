using System.Reactive.Concurrency;

using FluentResults;

using Microsoft.Extensions.Logging;

using SemiSpec.Core.Store;
using SemiSpec.UI.Messages;

namespace SemiSpec.UI.Store;

/// <summary>
/// The writer's open sequence: prepares the folder, opens the history file, replaces a file of another schema
/// version with a fresh one and sets an unreadable file aside before it starts a fresh one.
/// </summary>
internal sealed class StoreOpening(
	StoreFile storeFile,
	MessagePanelViewModel messagePanel,
	IScheduler uiScheduler,
	TimeProvider timeProvider,
	ILogger logger)
{
	public Result<SpectrumDatabase> Open()
	{
		var prepared = StoreFiles.Prepare(storeFile.Path);
		if (prepared.IsFailed)
		{
			return prepared.ToResult();
		}

		LogLegacyFilesDeleted(prepared.Value);
		var opened = SpectrumDatabase.Open(storeFile.Path);
		if (opened.HasError<StoreSchemaMismatchError>(out var mismatches))
		{
			return ReplaceMismatched(mismatches.First());
		}

		return opened.HasError<StoreFileUnreadableError>(out var unreadable)
			? ReplaceUnreadable(unreadable.First())
			: opened;
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
	/// Sets the unreadable file aside under the operator's local time, opens a fresh one and reports the replacement.
	/// </summary>
	private Result<SpectrumDatabase> ReplaceUnreadable(StoreFileUnreadableError unreadable)
	{
		var setAside = StoreFiles.SetAside(storeFile.Path, timeProvider.GetLocalNow());
		if (setAside.IsFailed)
		{
			return Result.Fail(unreadable).WithErrors(setAside.Errors);
		}

		var reopened = SpectrumDatabase.Open(storeFile.Path);
		if (reopened.IsFailed)
		{
			return reopened;
		}

		logger.LogWarning(
			"The spectrum store file {Path} could not be read; it was set aside as {SetAsidePath} "
				+ "and a new one started",
			storeFile.Path,
			setAside.Value);
		messagePanel.TryReportFailure(
			new StoreFileReplacedError(storeFile.Path, setAside.Value).CausedBy(unreadable),
			logger,
			uiScheduler);
		return reopened;
	}
}
