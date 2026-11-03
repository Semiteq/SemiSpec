using Microsoft.Extensions.DependencyInjection;

using Serilog;

using SpectrumVisualizer.Core;
using SpectrumVisualizer.UI.Logging;

namespace SpectrumVisualizer.UI;

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

		var logging = AppLogging.Create(paths.Value.LogFolder);

		if (logging.IsFailed)
		{
			return FailedExitCode;
		}

		Log.Logger = logging.Value.Logger;

		try
		{
			Log.Information("SpectrumVisualizer starting; log folder {LogFolder}", paths.Value.LogFolder);

			using var serviceProvider = new ServiceCollection()
				.AddUi(logging.Value.LevelControl)
				.BuildServiceProvider();

			App.Run(serviceProvider);

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
