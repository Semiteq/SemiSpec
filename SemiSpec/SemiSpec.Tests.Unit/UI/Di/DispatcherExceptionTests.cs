using System.Reactive.Disposables;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Reactive.Testing;

using SemiSpec.Tests.Unit.UI.Messages;
using SemiSpec.UI.Messages;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Di;

// App.Run subscribes the same handler in AfterSetup; that wiring cannot be covered here, because
// TestAppBuilder builds its own application and never calls Run.
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class DispatcherExceptionTests
{
	[AvaloniaFact]
	public void AThrowingJobAddsOneErrorEntryAndTheDispatcherRunsTheNextJob()
	{
		var scheduler = new TestScheduler();
		using var panel = new MessagePanelViewModel(TimeProvider.System);
		var observer = new UnhandledErrorObserver(() => panel, scheduler, NullLogger.Instance);
		var nextJobRan = false;

		using (Subscribe(observer.OnDispatcherUnhandledException))
		{
			Dispatcher.UIThread.Post(() => throw new InvalidOperationException("the job threw"));
			Dispatcher.UIThread.Post(() => nextJobRan = true);
			Dispatcher.UIThread.RunJobs();
		}

		scheduler.AdvanceBy(1);

		nextJobRan.Should().BeTrue();
		panel.Entries.Should().ContainSingle();
		panel.Entries[0].View.Severity.Should().Be(MessageSeverity.Error);
		panel.Entries[0].View.Detail.Should().Contain(nameof(InvalidOperationException));
	}

	[AvaloniaFact]
	public void WithNoPanelTheExceptionStaysUnhandled()
	{
		var observer = new UnhandledErrorObserver(() => null, new TestScheduler(), NullLogger.Instance);

		HandledAfter(observer).Should().BeFalse();
	}

	[AvaloniaFact]
	public void AFailingHandlerLogsAndLeavesTheExceptionUnhandled()
	{
		var logger = new FakeLogger();
		var observer = new UnhandledErrorObserver(
			() => throw new InvalidOperationException("the panel could not be resolved"),
			new TestScheduler(),
			logger);

		HandledAfter(observer).Should().BeFalse();
		logger.Levels().Should().Equal(LogLevel.Error);
	}

	/// <summary>
	/// Runs a throwing job past the observer and reads Handled in a handler subscribed after it; that handler
	/// then handles the exception itself, so the test's dispatcher keeps running.
	/// </summary>
	private static bool HandledAfter(UnhandledErrorObserver observer)
	{
		bool? handledByObserver = null;

		using (Subscribe(observer.OnDispatcherUnhandledException))
		using (Subscribe((_, e) =>
		{
			handledByObserver = e.Handled;
			e.Handled = true;
		}))
		{
			Dispatcher.UIThread.Post(() => throw new InvalidOperationException("the job threw"));
			Dispatcher.UIThread.RunJobs();
		}

		handledByObserver.Should().NotBeNull("the throwing job reaches the dispatcher's handlers");

		return handledByObserver.Value;
	}

	private static IDisposable Subscribe(DispatcherUnhandledExceptionEventHandler handler)
	{
		Dispatcher.UIThread.UnhandledException += handler;

		return Disposable.Create(() => Dispatcher.UIThread.UnhandledException -= handler);
	}
}
