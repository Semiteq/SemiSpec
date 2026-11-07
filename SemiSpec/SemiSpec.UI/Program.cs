using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SemiSpec.Core;
using SemiSpec.UI.Instance;
using SemiSpec.UI.Logging;
using SemiSpec.UI.Messages;

using Serilog;
using Serilog.Extensions.Logging;

namespace SemiSpec.UI;

public static class Program
{
	private const int FailedExitCode = 1;
	private const string ProductName = "SemiSpec";

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
			using var instanceLoggers = new SerilogLoggerFactory(logging.Value.Logger);
			var instanceLogger = instanceLoggers.CreateLogger(nameof(SingleInstance));
			var claimed = SingleInstance.Claim(SingleInstance.ProductionName, instanceLogger);

			if (!claimed.IsPrimary)
			{
				// Only the foreground process may hand the foreground on, and this just-launched copy is that process.
				_ = NativeMethods.AllowSetForegroundWindow(NativeMethods.AnyProcess);

				var outcome = SecondLaunch.Run(
					claimed,
					() => SingleInstance.Claim(SingleInstance.ProductionName, instanceLogger),
					SecondLaunch.AnswerLimit,
					SecondLaunch.Budget,
					ShowWarning,
					instanceLogger);
				if (outcome.Primary is not { } primary)
				{
					return outcome.ExitCode;
				}

				claimed = primary;
			}

			using var instance = claimed;

			Log.Information("SemiSpec starting; log folder {LogFolder}", logging.Value.Folder);

			using var serviceProvider = new ServiceCollection()
				.AddUi(logging.Value, paths.Value)
				.BuildServiceProvider();
			var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

			App.Run(serviceProvider, loggerFactory.CreateLogger(nameof(UnhandledErrorObserver)), instance);

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

	/// <summary>A second launch opens no Avalonia window, so its message is a plain Win32 box.</summary>
	private static void ShowWarning(string text)
	{
		_ = NativeMethods.MessageBoxW(0, text, ProductName, NativeMethods.OkButtonWithWarningIcon);
	}
}
