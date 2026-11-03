using FluentResults;

namespace SpectrumVisualizer.Core;

/// <summary>Folders the application owns under the per-user local application data root.</summary>
public sealed record AppDataPaths(string LogFolder)
{
	private const string AppFolderName = "SpectrumVisualizer";
	private const string LogFolderName = "Logs";

	public static Result<AppDataPaths> Under(string localAppDataRoot)
	{
		if (!Path.IsPathFullyQualified(localAppDataRoot))
		{
			return Result.Fail(
				$"The local application data folder '{localAppDataRoot}' is not an absolute path");
		}

		return new AppDataPaths(Path.Combine(localAppDataRoot, AppFolderName, LogFolderName));
	}
}
