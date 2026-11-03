using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using SpectrumVisualizer.UI.Messages;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Messages;

// The clock is stepped by hand because a tight loop of reports would otherwise share one timestamp and
// hide a last-seen stamp that never moves.
[Trait("Component", "UI")]
[Trait("Area", "Messages")]
[Trait("Category", "Unit")]
public sealed class MessagePanelViewModelTests
{
	[AvaloniaFact]
	public void ReportCoalescesAnIdenticalFailureIntoOneEntry()
	{
		var clock = new SteppingClock();
		using var panel = new MessagePanelViewModel(clock);

		for (var repeat = 0; repeat < 20; repeat++)
		{
			panel.Report(Failure("archive"));
		}

		panel.Entries.Should().HaveCount(1);
		panel.Entries[0].RepeatCount.Should().Be(20);
		panel.Entries[0].HasRepeated.Should().BeTrue();
		panel.Entries[0].LastSeen.Should().Be(SteppingClock.Origin + (SteppingClock.Step * 19));
	}

	[AvaloniaFact]
	public void ReportKeepsTwoDifferentFailuresApart()
	{
		var clock = new SteppingClock();
		using var panel = new MessagePanelViewModel(clock);

		panel.Report(Failure("archive"));
		panel.Report(Failure("catalogue"));

		panel.Entries.Should().HaveCount(2);
		panel.Entries.Select(entry => entry.View.Title).Should().Equal("catalogue", "archive");
		panel.Entries.Should().OnlyContain(entry => entry.RepeatCount == 1);
	}

	// Coalescing is against the newest entry only, so a failure that comes back after another one starts
	// a new row rather than reviving the old one.
	[AvaloniaFact]
	public void ReportStartsANewEntryWhenAnotherFailureCameBetween()
	{
		var clock = new SteppingClock();
		using var panel = new MessagePanelViewModel(clock);

		panel.Report(Failure("archive"));
		panel.Report(Failure("catalogue"));
		panel.Report(Failure("archive"));

		panel.Entries.Should().HaveCount(3);
		panel.Entries.Select(entry => entry.View.Title).Should().Equal("archive", "catalogue", "archive");
	}

	[AvaloniaFact]
	public void ReportDropsTheOldestEntryOnceTheCapIsReached()
	{
		var clock = new SteppingClock();
		using var panel = new MessagePanelViewModel(clock);

		for (var index = 0; index <= MessagePanelViewModel.MaximumEntries; index++)
		{
			panel.Report(Failure($"failure {index}"));
		}

		panel.Entries.Should().HaveCount(MessagePanelViewModel.MaximumEntries);
		panel.Entries[0].View.Title.Should().Be($"failure {MessagePanelViewModel.MaximumEntries}");
		panel.Entries[^1].View.Title.Should().Be("failure 1");
	}

	[AvaloniaFact]
	public void ReportOpensThePanelOnTheFirstEntry()
	{
		using var panel = new MessagePanelViewModel(new SteppingClock());

		panel.HasEntries.Should().BeFalse("a session that never fails is not given the row");

		panel.Report(Failure("archive"));

		panel.HasEntries.Should().BeTrue("the entry would otherwise land off screen");
	}

	// The detail carries the exception's own message, so two different failures of one exception type are
	// two rows, while the same failure repeated stays one.
	[AvaloniaFact]
	public void ReportKeepsDifferentExceptionMessagesApartAndCoalescesIdenticalOnes()
	{
		using var panel = new MessagePanelViewModel(new SteppingClock());

		panel.Report(Thrown("the port is taken"));
		panel.Report(Thrown("the port is taken"));
		panel.Report(Thrown("the device stopped answering"));

		panel.Entries.Should().HaveCount(2);
		panel.Entries.Select(entry => entry.RepeatCount).Should().Equal(1, 2);
	}

	[AvaloniaFact]
	public void HasEntriesNotifiesWhenTheFirstEntryArrives()
	{
		using var panel = new MessagePanelViewModel(new SteppingClock());
		var raised = new List<string?>();
		panel.PropertyChanged += (_, arguments) => raised.Add(arguments.PropertyName);

		panel.Report(Failure("archive"));

		raised.Should().Contain(nameof(MessagePanelViewModel.HasEntries));
	}

	// The answer ResultReporting picks its log level from, so the log and the entry count cannot drift apart.
	[AvaloniaFact]
	public void ReportAnswersWhetherItCoalescedAgainstTheNewestEntryOnly()
	{
		using var panel = new MessagePanelViewModel(new SteppingClock());
		var archive = Failure("archive");

		panel.Report(archive).Should().BeFalse("the list is empty");
		panel.Report(archive).Should().BeTrue();
		panel.Report(Failure("catalogue")).Should().BeFalse();
		panel.Report(archive).Should().BeFalse("another failure is now the newest entry");
	}

	[AvaloniaFact]
	public void ClearCommandEmptiesTheListAndTakesThePanelOffScreen()
	{
		using var panel = new MessagePanelViewModel(new SteppingClock());
		panel.Report(Failure("archive"));

		panel.ClearCommand.Execute().Subscribe();

		panel.Entries.Should().BeEmpty();
		panel.HasEntries.Should().BeFalse();
	}

	private static MessageView Failure(string title)
	{
		return new MessageView(title, "detail", "remedy");
	}

	private static MessageView Thrown(string message)
	{
		var exception = new InvalidOperationException(message);

		return ErrorMessageMapper.Map(new ExceptionalError(exception.Message, exception));
	}

	private sealed class SteppingClock : TimeProvider
	{
		public static readonly DateTimeOffset Origin = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

		public static readonly TimeSpan Step = TimeSpan.FromSeconds(1);

		private int _reads;

		public override DateTimeOffset GetUtcNow()
		{
			return Origin + (Step * _reads++);
		}
	}
}
