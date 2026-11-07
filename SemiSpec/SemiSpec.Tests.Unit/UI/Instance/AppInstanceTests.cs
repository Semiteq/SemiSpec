using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using ReactiveUI.Avalonia;

using SemiSpec.Tests.Unit.UI.Di;
using SemiSpec.UI.Instance;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Instance;

/// <summary>The app's claim against the window's close.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Instance")]
[Trait("Category", "Unit")]
public sealed class AppInstanceTests : AppLifecycleTestBase
{
	[AvaloniaFact]
	public async Task ALaunchDuringTheCloseClaimsTheNameAsANewPrimary()
	{
		using var provider = BuildContainer();
		var window = ShowMainWindow(provider);
		// As App.Run does after Configure, so the release also has a listener to end.
		Instance.Listen(AvaloniaScheduler.Instance, window.Activate, NullLogger.Instance);
		var closed = new TaskCompletionSource();
		window.Closed += (_, _) => closed.TrySetResult();

		window.Close();
		using var relaunch = SingleInstance.Claim(InstanceName, NullLogger.Instance);

		relaunch.IsPrimary.Should().BeTrue();
		await closed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
	}

	[AvaloniaFact]
	public async Task BeforeTheCloseALaunchIsSecondary()
	{
		using var provider = BuildContainer();
		var window = ShowMainWindow(provider);
		var closed = new TaskCompletionSource();
		window.Closed += (_, _) => closed.TrySetResult();

		using (var launch = SingleInstance.Claim(InstanceName, NullLogger.Instance))
		{
			launch.IsPrimary.Should().BeFalse();
		}

		window.Close();
		await closed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
	}
}
