using FluentResults;

using Microsoft.Extensions.Logging;

using SemiSpec.Core.Store;

namespace SemiSpec.UI.Store;

/// <summary>
/// The writer's open sequence: prepares the folder, opens the history file, and replaces a file of another schema
/// version with a fresh one.
/// </summary>
internal sealed class StoreOpening(StoreFile storeFile, ILogger logger)
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
		return opened.HasError<StoreSchemaMismatchError>(out var mismatches)
			? ReplaceMismatched(mismatches.First())
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
}
