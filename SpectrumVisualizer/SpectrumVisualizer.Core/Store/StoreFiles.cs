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

	/// <summary>Creates the folder, wipes the own file, and deletes every other store file no live instance holds open.</summary>
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
		var names = Directory.GetFiles(folder, FilePrefix + "*");
		var deleted = 0;
		var kept = 0;
		foreach (var name in names)
		{
			if (!name.EndsWith(DatabaseExtension, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(name, instanceFile, StringComparison.OrdinalIgnoreCase))
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
			var database = DatabaseOfSidecar(name);
			if (database is not null && !File.Exists(database))
			{
				TryDelete(name);
			}
		}

		return new StoreSweep(deleted, kept);
	}

	private static string? DatabaseOfSidecar(string name)
	{
		foreach (var suffix in _sidecarSuffixes)
		{
			var databaseSuffix = DatabaseExtension + suffix;
			if (name.EndsWith(databaseSuffix, StringComparison.OrdinalIgnoreCase))
			{
				return name[..^suffix.Length];
			}
		}

		return null;
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

/// <summary>Counts of the other instances' store files the sweep deleted and the ones a live instance kept open.</summary>
public sealed record StoreSweep(int DeletedFiles, int KeptFiles);
