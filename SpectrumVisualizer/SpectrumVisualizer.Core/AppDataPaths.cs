using FluentResults;

namespace SpectrumVisualizer.Core;

/// <summary>Folders the application owns under the per-user local application data root.</summary>
public sealed record AppDataPaths(string LogFolder, string StoreFolder)
{
	private const string AppFolderName = "SpectrumVisualizer";
	private const string LogFolderName = "Logs";
	private const string StoreFolderName = "Spectra";

	public static Result<AppDataPaths> Under(string localAppDataRoot)
	{
		if (!Path.IsPathFullyQualified(localAppDataRoot))
		{
			return Result.Fail(
				$"The local application data folder '{localAppDataRoot}' is not an absolute path");
		}

		var appFolder = Path.Combine(localAppDataRoot, AppFolderName);
		return new AppDataPaths(
			Path.Combine(appFolder, LogFolderName),
			Path.Combine(appFolder, StoreFolderName));
	}
}
