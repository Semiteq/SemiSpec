using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SemiSpec.Core;
using SemiSpec.UI.Logging;
using SemiSpec.UI.Messages;

using Serilog;

namespace SemiSpec.UI;

public static class Program
{
	private const int FailedExitCode = 1;

	[STAThread]
	public static int Main()
	{
		var paths = AppDataPaths.Under(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

		// With no log folder there is nowhere to report the failure; the exit code is all that remains.
		if (paths.IsFailed)
		{
			return FailedExitCode;
		}

		var logging = AppLogging.Create(AppLogging.SiteLogFolder, paths.Value.FallbackLogFolder, AppLogging.CanWrite);

		if (logging.IsFailed)
		{
			return FailedExitCode;
		}

		Log.Logger = logging.Value.Logger;

		try
		{
			Log.Information("SemiSpec starting; log folder {LogFolder}", logging.Value.Folder);

			using var serviceProvider = new ServiceCollection()
				.AddUi(logging.Value, paths.Value)
				.BuildServiceProvider();
			var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

			App.Run(serviceProvider, loggerFactory.CreateLogger(nameof(UnhandledErrorObserver)));

			return 0;
		}
		catch (Exception ex)
		{
			Log.Fatal(ex, "Application terminated unexpectedly");

			return FailedExitCode;
		}
		finally
		{
			Log.CloseAndFlush();
		}
	}
}
