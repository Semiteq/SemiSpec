using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Reactive.Testing;

using SpectrumVisualizer.UI.Messages;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Messages;

// These cover the observer itself. That App.BuildAvaloniaApp hands it to ReactiveUI cannot be covered
// here: TestAppBuilder builds its own application and never calls it.
[Trait("Component", "UI")]
[Trait("Area", "Messages")]
[Trait("Category", "Unit")]
public sealed class UnhandledErrorObserverTests
{
	[Fact]
	public void OnNextReportsTheMappedExceptionThroughTheUiScheduler()
	{
		var scheduler = new TestScheduler();
		using var panel = new MessagePanelViewModel();
		var observer = new UnhandledErrorObserver(() => panel, scheduler, NullLogger.Instance);
		var failure = new InvalidOperationException("the command body threw");

		observer.OnNext(failure);

		panel.Entries.Should().BeEmpty();

		scheduler.AdvanceBy(1);

		panel.Entries.Should().ContainSingle();
		panel.Entries[0].View.Should()
			.Be(ErrorMessageMapper.Map(new ExceptionalError(failure.Message, failure)));
		panel.Entries[0].View.Severity.Should().Be(MessageSeverity.Error);
	}

	[Fact]
	public void OnNextLogsTheExceptionAtErrorWhenNoPanelHasBeenResolved()
	{
		var scheduler = new TestScheduler();
		var logger = new FakeLogger();
		var observer = new UnhandledErrorObserver(() => null, scheduler, logger);
		var failure = new InvalidOperationException("no window yet");

		observer.OnNext(failure);
		scheduler.AdvanceBy(1);

		logger.Levels().Should().Equal(LogLevel.Error);
		logger.Failures().Should().Equal(failure);
	}

	[Fact]
	public void OnNextLogsAndReportsWhenThePanelExists()
	{
		var scheduler = new TestScheduler();
		using var panel = new MessagePanelViewModel();
		var logger = new FakeLogger();
		var observer = new UnhandledErrorObserver(() => panel, scheduler, logger);

		observer.OnNext(new InvalidOperationException("both routes"));
		scheduler.AdvanceBy(1);

		logger.Levels().Should().Equal(LogLevel.Error);
		panel.Entries.Should().ContainSingle();
	}

	// The report runs as a dispatcher job, where an escaping throw is unhandled and takes the process down,
	// which is the outcome this observer exists to prevent.
	[Fact]
	public void OnNextWithAPanelThatRefusesTheEntryLogsAndDoesNotEscapeTheScheduledJob()
	{
		var scheduler = new TestScheduler();
		using var panel = new MessagePanelViewModel();
		var logger = new FakeLogger();
		var observer = new UnhandledErrorObserver(() => panel, scheduler, logger);
		ReportingTestDoubles.PoisonEntries(panel);

		observer.OnNext(new InvalidOperationException("the command body threw"));

		var runTheJob = () => scheduler.AdvanceBy(1);

		runTheJob.Should().NotThrow();
		logger.Levels().Should().Equal([LogLevel.Error, LogLevel.Error]);
	}

	// ReactiveUI hands the handler OnError when the pipeline carrying the failures fails itself; losing
	// that exception would drop the one failure the operator most needs to see.
	[Fact]
	public void OnErrorTakesTheSameRouteAsOnNext()
	{
		var scheduler = new TestScheduler();
		using var panel = new MessagePanelViewModel();
		var observer = new UnhandledErrorObserver(() => panel, scheduler, NullLogger.Instance);

		observer.OnError(new InvalidOperationException("the handler stream failed"));
		scheduler.AdvanceBy(1);

		panel.Entries.Should().ContainSingle();
	}
}
