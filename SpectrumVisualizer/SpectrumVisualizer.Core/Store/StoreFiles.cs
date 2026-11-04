using System.Globalization;

using FluentResults;

namespace SpectrumVisualizer.Core.Store;

/// <summary>Names the spectrum store file of this process and clears the files earlier runs left in the store folder.</summary>
public static class StoreFiles
{
	private const string FilePrefix = "spectra-";
	private const string DatabaseExtension = ".db";
	private static readonly string[] _sidecarSuffixes = ["-wal", "-shm"];

	public static string InstanceFile(string folder, int processId)
	{
		return Path.Combine(folder, $"{FilePrefix}{processId}{DatabaseExtension}");
	}

	/// <summary>
	/// Creates the folder and wipes the own file, or fails; then deletes every other store file no live instance holds
	/// open. A sweep that cannot list the folder deletes nothing and carries its failure in the result.
	/// </summary>
	public static Result<StoreSweep> Prepare(string instanceFile)
	{
		var folder = Path.GetDirectoryName(instanceFile)!;
		try
		{
			Directory.CreateDirectory(folder);
			DeleteWithSidecars(instanceFile);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return Result.Fail(new Error($"The spectrum store file '{instanceFile}' could not be prepared").CausedBy(ex));
		}

		return Sweep(folder, instanceFile);
	}

	private static StoreSweep Sweep(string folder, string instanceFile)
	{
		string[] names;
		try
		{
			names = Directory.GetFiles(folder, FilePrefix + "*");
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return new StoreSweep(DeletedFiles: 0, KeptFiles: 0, ex);
		}

		var deleted = 0;
		var kept = 0;
		foreach (var name in names)
		{
			if (DatabaseOf(name) != name || string.Equals(name, instanceFile, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (TryDelete(name))
			{
				deleted++;
				foreach (var suffix in _sidecarSuffixes)
				{
					TryDelete(name + suffix);
				}
			}
			else
			{
				kept++;
			}
		}

		foreach (var name in names)
		{
			if (DatabaseOf(name) is { } database && database != name && !File.Exists(database))
			{
				TryDelete(name);
			}
		}

		return new StoreSweep(deleted, kept);
	}

	/// <summary>The store file a file belongs to, itself or the file of its sidecar; null outside spectra-&lt;digits&gt;.db.</summary>
	private static string? DatabaseOf(string file)
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
		if (!name.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)
			|| !name.EndsWith(DatabaseExtension, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		var processId = name[FilePrefix.Length..^DatabaseExtension.Length];
		return int.TryParse(processId, NumberStyles.None, CultureInfo.InvariantCulture, out _) ? database : null;
	}

	private static void DeleteWithSidecars(string database)
	{
		File.Delete(database);
		foreach (var suffix in _sidecarSuffixes)
		{
			File.Delete(database + suffix);
		}
	}

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

/// <summary>
/// Counts of the other instances' store files the sweep deleted and the ones a live instance kept open; Failure is what
/// stopped the sweep from listing the folder, null when it ran.
/// </summary>
public sealed record StoreSweep(int DeletedFiles, int KeptFiles, Exception? Failure = null);
