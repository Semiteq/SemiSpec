using FluentResults;

using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SemiSpec.UI.Settings;

/// <summary>The YAML file that holds the display choices, in the site settings folder or the fallback one.</summary>
public sealed class SettingsFile(string fullPath, bool inFallbackFolder = false)
{
	public const string FileName = "settings.yaml";

	/// <summary>The settings folder of every Semi application on an operator PC; the installer creates it.</summary>
	public const string SiteFolder = @"C:\DISTR\Config\SemiSpec";

	private static readonly IDeserializer _deserializer = new DeserializerBuilder()
		.WithNamingConvention(UnderscoredNamingConvention.Instance)
		.IgnoreUnmatchedProperties()
		.Build();

	private static readonly ISerializer _serializer = new SerializerBuilder()
		.WithNamingConvention(UnderscoredNamingConvention.Instance)
		.Build();

	public string FullPath { get; } = fullPath;

	/// <summary>True when the site folder could not be written and the file sits in the fallback folder.</summary>
	public bool InFallbackFolder { get; } = inFallbackFolder;

	/// <summary>The file in the site folder when the check can write there, else in the fallback folder.</summary>
	public static SettingsFile Choose(string siteFolder, string fallbackFolder, Func<string, bool> canWrite)
	{
		return canWrite(siteFolder)
			? new SettingsFile(Path.Combine(siteFolder, FileName))
			: new SettingsFile(Path.Combine(fallbackFolder, FileName), inFallbackFolder: true);
	}

	/// <summary>Creates the folder and creates and deletes a probe file in it.</summary>
	public static bool CanWrite(string folder)
	{
		try
		{
			Directory.CreateDirectory(folder);
			var probe = Path.Combine(folder, $".write-probe-{Environment.ProcessId}");
			using (new FileStream(probe, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
			{
			}

			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	/// <summary>The defaults when the file is missing or empty; unknown keys are ignored.</summary>
	public Result<DisplaySettings> Load()
	{
		try
		{
			if (!File.Exists(FullPath))
			{
				return DisplaySettings.Default;
			}

			var document = _deserializer.Deserialize<SettingsDocument?>(File.ReadAllText(FullPath));
			return document is null
				? DisplaySettings.Default
				: new DisplaySettings(document.ShowQFactor, document.TrendAsLines);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or YamlException)
		{
			return Result.Fail(new SettingsReadError(FullPath).CausedBy(ex));
		}
	}

	/// <summary>Writes a temporary file beside the target and moves it over, so a failed write keeps the old file.</summary>
	public Result Save(DisplaySettings settings)
	{
		var temporary = FullPath + ".tmp";
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(FullPath)!);
			var document = new SettingsDocument
			{
				ShowQFactor = settings.ShowQFactor,
				TrendAsLines = settings.TrendAsLines,
			};
			File.WriteAllText(temporary, _serializer.Serialize(document));
			File.Move(temporary, FullPath, overwrite: true);
			return Result.Ok();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return Result.Fail(new SettingsWriteError(FullPath).CausedBy(ex));
		}
	}

	/// <summary>The file's shape: one key per choice, snake case.</summary>
	private sealed class SettingsDocument
	{
		public bool ShowQFactor { get; set; }

		public bool TrendAsLines { get; set; }
	}
}
