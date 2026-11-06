using System.Globalization;

using FluentResults;

namespace SemiSpec.Core.Store;

/// <summary>
/// Names the history file in the store folder and deletes the per-process store files older builds left there.
/// </summary>
public static class StoreFiles
{
	private const string HistoryFileName = "spectra.db";
	private const string LegacyFilePrefix = "spectra-";
	private const string DatabaseExtension = ".db";
	private static readonly string[] _sidecarSuffixes = ["-wal", "-shm"];

	public static string HistoryFile(string folder)
	{
		return Path.Combine(folder, HistoryFileName);
	}

	/// <summary>
	/// Creates the folder of the history file, then deletes every legacy store file no live process holds open and the
	/// legacy sidecars whose store file is gone; the value is the count of legacy store files deleted.
	/// </summary>
	public static Result<int> Prepare(string historyFile)
	{
		var folder = Path.GetDirectoryName(historyFile)!;
		string[] names;
		try
		{
			Directory.CreateDirectory(folder);
			names = Directory.GetFiles(folder, LegacyFilePrefix + "*");
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return Result.Fail(
				new ExceptionalError($"The spectrum store file '{historyFile}' could not be prepared", ex));
		}

		var deleted = 0;
		foreach (var name in names)
		{
			if (LegacyDatabaseOf(name) == name && TryDelete(name))
			{
				deleted++;
				foreach (var suffix in _sidecarSuffixes)
				{
					TryDelete(name + suffix);
				}
			}
		}

		foreach (var name in names)
		{
			if (LegacyDatabaseOf(name) is { } database && database != name && !File.Exists(database))
			{
				TryDelete(name);
			}
		}

		return deleted;
	}

	/// <summary>
	/// The legacy store file a file belongs to, itself or the file of its sidecar; null outside spectra-&lt;digits&gt;.db.
	/// </summary>
	private static string? LegacyDatabaseOf(string file)
	{
		var database = file;
		foreach (var suffix in _sidecarSuffixes)
		{
			if (file.EndsWith(DatabaseExtension + suffix, StringComparison.OrdinalIgnoreCase))
			{
				database = file[..^suffix.Length];
			}
		}

		var name = Path.GetFileName(database);
		if (!name.StartsWith(LegacyFilePrefix, StringComparison.OrdinalIgnoreCase)
			|| !name.EndsWith(DatabaseExtension, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		var processId = name[LegacyFilePrefix.Length..^DatabaseExtension.Length];
		return int.TryParse(processId, NumberStyles.None, CultureInfo.InvariantCulture, out _) ? database : null;
	}

	/// <summary>Deletes the store file and its WAL and shared-memory sidecars; a missing file is no error.</summary>
	public static void DeleteWithSidecars(string database)
	{
		File.Delete(database);
		foreach (var suffix in _sidecarSuffixes)
		{
			File.Delete(database + suffix);
		}
	}

	/// <summary>False when the delete failed, which for a legacy store file means a running older build holds it.</summary>
	private static bool TryDelete(string file)
	{
		try
		{
			File.Delete(file);
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}
}
