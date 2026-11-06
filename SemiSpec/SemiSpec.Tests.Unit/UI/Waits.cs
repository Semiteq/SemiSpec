using Avalonia.Threading;

using SemiSpec.UI.Link;
using SemiSpec.UI.MainWindow;

namespace SemiSpec.Tests.Unit.UI;

/// <summary>
/// Real-time waits for work on another thread; each fails after Limit, so a hung loop fails the test.
/// </summary>
internal static class Waits
{
	public static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

	/// <summary>Polls every 10 ms; with pumpDispatcher the UI thread's queued jobs run before each check.</summary>
	public static async Task UntilAsync(Func<bool> condition, bool pumpDispatcher = false)
	{
		using var limit = new CancellationTokenSource(Limit);
		while (true)
		{
			if (pumpDispatcher)
			{
				Dispatcher.UIThread.RunJobs();
			}

			if (condition())
			{
				return;
			}

			await Task.Delay(10, limit.Token);
		}
	}

	/// <summary>
	/// The link raises its status off the UI thread, so the bar catches up once the dispatcher runs the hop.
	/// </summary>
	public static Task StatusBarSettledAsync(AppStatusBarViewModel statusBar, SerialLink serialLink)
	{
		return UntilAsync(() => ReferenceEquals(statusBar.Status, serialLink.Status), pumpDispatcher: true);
	}
}
