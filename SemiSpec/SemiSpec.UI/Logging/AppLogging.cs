using System.Globalization;

using FluentResults;

using Serilog;
using Serilog.Core;

namespace SemiSpec.UI.Logging;

/// <summary>The application logger, the control that changes its level at run time, and the folder it writes.</summary>
public sealed record LoggingSetup(Logger Logger, LogLevelControl LevelControl, string Folder);

public static class AppLogging
{
	public const string LogFileName = "semispec.log";

	/// <summary>The log folder of every Semi application on an operator PC; the installer creates it.</summary>
	public const string SiteLogFolder = @"C:\DISTR\Logs\SemiSpec";

	private const string ProcessIdProperty = "ProcessId";
	private const string Template =
		"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{" + ProcessIdProperty + "}] [{Level:u3}] "
		+ "{SourceContext}: {Message:lj}{NewLine}{Exception}";
	private const long LogFileSizeLimitBytes = 5 * 1024 * 1024;
	private const int RetainedFileCount = 5;

	/// <summary>
	/// Logs to the site folder when the check can write there, else to the fallback folder with a Warning that names
	/// both.
	/// </summary>
	public static Result<LoggingSetup> Create(string siteFolder, string fallbackFolder, Func<string, bool> canWrite)
	{
		if (canWrite(siteFolder))
		{
			return Create(siteFolder);
		}

		var setup = Create(fallbackFolder);
		if (setup.IsSuccess)
		{
			setup.Value.Logger.Warning(
				"The log folder {SiteFolder} cannot be created or written; logging to {FallbackFolder}",
				siteFolder,
				fallbackFolder);
		}

		return setup;
	}

	/// <summary>Creates the folder and opens the log file for append with the sharing the shared file sink uses.</summary>
	public static bool CanWrite(string folder)
	{
		try
		{
			Directory.CreateDirectory(folder);
			using var probe = new FileStream(
				Path.Combine(folder, LogFileName),
				FileMode.Append,
				FileAccess.Write,
				FileShare.ReadWrite | FileShare.Delete);

			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	public static Result<LoggingSetup> Create(string logFolder)
	{
		try
		{
			Directory.CreateDirectory(logFolder);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return Result.Fail(new ExceptionalError($"Cannot create the log folder '{logFolder}'", ex));
		}

		var levelControl = new LogLevelControl();

		var logger = new LoggerConfiguration()
			.MinimumLevel.ControlledBy(levelControl.Switch)
			.Enrich.FromLogContext()
			.Enrich.WithProperty(ProcessIdProperty, Environment.ProcessId)
			.WriteTo.File(
				path: Path.Combine(logFolder, LogFileName),
				rollingInterval: RollingInterval.Infinite,
				fileSizeLimitBytes: LogFileSizeLimitBytes,
				rollOnFileSizeLimit: true,
				retainedFileCountLimit: RetainedFileCount,
				shared: true,
				outputTemplate: Template,
				formatProvider: CultureInfo.InvariantCulture)
			.CreateLogger();

		return new LoggingSetup(logger, levelControl, logFolder);
	}
}
