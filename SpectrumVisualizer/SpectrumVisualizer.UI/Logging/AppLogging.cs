using System.Globalization;

using FluentResults;

using Serilog;
using Serilog.Core;

namespace SpectrumVisualizer.UI.Logging;

/// <summary>The application logger together with the control that changes its level at run time.</summary>
public sealed record LoggingSetup(Logger Logger, LogLevelControl LevelControl);

public static class AppLogging
{
	public const string LogFileName = "spectrum-visualizer.log";

	private const string ProcessIdProperty = "ProcessId";
	private const string Template =
		"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{" + ProcessIdProperty + "}] [{Level:u3}] "
		+ "{SourceContext}: {Message:lj}{NewLine}{Exception}";
	private const long LogFileSizeLimitBytes = 5 * 1024 * 1024;
	private const int RetainedFileCount = 5;

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

		return new LoggingSetup(logger, levelControl);
	}
}
