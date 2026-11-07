using Microsoft.Extensions.Logging;

using SemiSpec.UI.Localization;

namespace SemiSpec.UI.Instance;

/// <summary>What a launch that finds a running copy does: has that copy activate its window, then exits.</summary>
internal static class SecondLaunch
{
	public const int AnsweredExitCode = 0;
	public const int NotAnsweredExitCode = 1;

	public static readonly TimeSpan AnswerLimit = TimeSpan.FromSeconds(2);

	// Two launches at once can lose one answer between them; the retry recovers it.
	private const int MaximumAsks = 2;

	public static int Run(SingleInstance instance, TimeSpan limit, Action<string> showMessage, ILogger logger)
	{
		for (var ask = 0; ask < MaximumAsks; ask++)
		{
			if (instance.AskPrimary(limit))
			{
				logger.LogInformation("SemiSpec already runs; its window was brought to the front");

				return AnsweredExitCode;
			}
		}

		logger.LogWarning("SemiSpec already runs but did not answer within {AnswerLimit} twice", limit);
		showMessage(Resources.SingleInstanceNotResponding);

		return NotAnsweredExitCode;
	}
}
