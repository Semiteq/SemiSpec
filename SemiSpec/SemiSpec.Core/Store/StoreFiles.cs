using System.Globalization;

using FluentResults;

namespace SemiSpec.Core.Store;

/// <summary>
/// Names the history file in the store folder, deletes the per-process store files older builds left there and sets
/// an unreadable history file aside.
/// </summary>
public static class StoreFiles
{
	private const string HistoryFileName = "spectra.db";
	private const string LegacyFilePrefix = "spectra-";
	private const string DatabaseExtension = ".db";
	private const string WalSuffix = "-wal";
	private const string SharedMemorySuffix = "-shm";
	private const string SetAsidePrefix = "spectra.broken-";
	private const string SetAsideStampFormat = "yyyyMMdd-HHmmss";
	private const int KeptSetAsideFiles = 2;
	private static readonly string[] _sidecarSuffixes = [WalSuffix, SharedMemorySuffix];

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

	/// <summary>
	/// Renames the closed history file to spectra.broken-&lt;local stamp&gt;.db beside it with its WAL, deletes its
	/// shared memory and the set-aside files beyond the newest two; the value is the new path.
	/// </summary>
	public static Result<string> SetAside(string historyFile, DateTimeOffset localNow)
	{
		var folder = Path.GetDirectoryName(historyFile)!;
		var stamp = localNow.ToString(SetAsideStampFormat, CultureInfo.InvariantCulture);
		var setAside = Path.Combine(folder, SetAsidePrefix + stamp + DatabaseExtension);
		try
		{
			File.Move(historyFile, setAside);
			if (File.Exists(historyFile + WalSuffix))
			{
				File.Move(historyFile + WalSuffix, setAside + WalSuffix);
			}

			File.Delete(historyFile + SharedMemorySuffix);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return Result.Fail(
				new ExceptionalError($"The spectrum store file '{historyFile}' could not be set aside", ex));
		}

		DeleteOlderSetAsideFiles(folder);
		return setAside;
	}

	private static void DeleteOlderSetAsideFiles(string folder)
	{
		string[] names;
		try
		{
			names = Directory.GetFiles(folder, SetAsidePrefix + "*");
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return;
		}

		var older = names
			.Where(name => name.EndsWith(DatabaseExtension, StringComparison.OrdinalIgnoreCase))
			.OrderDescending(StringComparer.OrdinalIgnoreCase)
			.Skip(KeptSetAsideFiles);
		foreach (var name in older)
		{
			if (TryDelete(name))
			{
				foreach (var suffix in _sidecarSuffixes)
				{
					TryDelete(name + suffix);
				}
			}
		}
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
