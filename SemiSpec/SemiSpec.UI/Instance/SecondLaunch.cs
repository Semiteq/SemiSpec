using System.Diagnostics;

using Microsoft.Extensions.Logging;

using SemiSpec.UI.Localization;

namespace SemiSpec.UI.Instance;

/// <summary>
/// What a launch that finds a running copy does: asks it to activate its window and exits, or takes the name over
/// once the running copy gave it up.
/// </summary>
internal static class SecondLaunch
{
	public const int AnsweredExitCode = 0;
	public const int NotAnsweredExitCode = 1;

	public static readonly TimeSpan AnswerLimit = TimeSpan.FromSeconds(2);

	/// <summary>Covers a cold start of the first copy and the 5 s close of a copy that is going away.</summary>
	public static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

	/// <summary>
	/// Takes over the secondary claim and disposes it. Each round without an answer claims the name again, so a copy
	/// that closed or died lets this one continue as the primary; the message shows only after the budget, with every
	/// handle of this copy closed, so the operator's restart after killing the hung copy claims a free name.
	/// </summary>
	public static SecondLaunchOutcome Run(
		SingleInstance secondary,
		Func<SingleInstance> claim,
		TimeSpan answerLimit,
		TimeSpan budget,
		Action<string> showMessage,
		ILogger logger)
	{
		var started = Stopwatch.GetTimestamp();
		var instance = secondary;
		while (true)
		{
			var answered = instance.AskPrimary(answerLimit);
			instance.Dispose();
			if (answered)
			{
				logger.LogInformation("SemiSpec already runs; its window was brought to the front");

				return SecondLaunchOutcome.Exit(AnsweredExitCode);
			}

			if (Stopwatch.GetElapsedTime(started) >= budget)
			{
				break;
			}

			instance = claim();
			if (instance.IsPrimary)
			{
				logger.LogInformation("The running copy of SemiSpec gave up its claim; this copy continues as the primary");

				return SecondLaunchOutcome.ContinueAs(instance);
			}
		}

		logger.LogWarning("SemiSpec already runs but did not answer within {Budget}", budget);
		showMessage(Resources.SingleInstanceNotResponding);

		return SecondLaunchOutcome.Exit(NotAnsweredExitCode);
	}
}

/// <summary>Either the exit code of a launch that ends, or the primary claim of a launch that continues.</summary>
internal sealed record SecondLaunchOutcome(int ExitCode, SingleInstance? Primary)
{
	public static SecondLaunchOutcome Exit(int exitCode)
	{
		return new SecondLaunchOutcome(exitCode, null);
	}

	public static SecondLaunchOutcome ContinueAs(SingleInstance primary)
	{
		return new SecondLaunchOutcome(SecondLaunch.AnsweredExitCode, primary);
	}
}
