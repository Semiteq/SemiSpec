using FluentResults;

namespace SpectrumVisualizer.Core;

/// <summary>Folders the application owns under the per-user local application data root.</summary>
public sealed record AppDataPaths(string AppFolder, string LogFolder)
{
	public const string AppFolderName = "SpectrumVisualizer";
	public const string LogFolderName = "Logs";

	public static Result<AppDataPaths> Under(string localAppDataRoot)
	{
		if (!Path.IsPathFullyQualified(localAppDataRoot))
		{
			return Result.Fail(
				$"The local application data folder '{localAppDataRoot}' is not an absolute path");
		}

		var appFolder = Path.Combine(localAppDataRoot, AppFolderName);

		return new AppDataPaths(appFolder, Path.Combine(appFolder, LogFolderName));
	}
}
