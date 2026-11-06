using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Time.Testing;

using SemiSpec.UI.Messages;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Messages;

// The clock steps one second per read because a tight loop of reports would otherwise share one timestamp and
// hide a last-seen stamp that never moves.
[Trait("Component", "UI")]
[Trait("Area", "Messages")]
[Trait("Category", "Unit")]
public sealed class MessagePanelViewModelTests
{
	private static readonly DateTimeOffset _origin = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
	private static readonly TimeSpan _step = TimeSpan.FromSeconds(1);

	[AvaloniaFact]
	public void ReportCoalescesAnIdenticalFailureIntoOneEntry()
	{
		var clock = SteppingClock();
		using var panel = new MessagePanelViewModel(clock);

		for (var repeat = 0; repeat < 20; repeat++)
		{
			panel.Report(Failure("archive"));
		}

		panel.Entries.Should().HaveCount(1);
		panel.Entries[0].RepeatCount.Should().Be(20);
		panel.Entries[0].HasRepeated.Should().BeTrue();
		panel.Entries[0].LastSeen.Should().Be(_origin + (_step * 19));
	}

	[AvaloniaFact]
	public void ReportKeepsTwoDifferentFailuresApart()
	{
		var clock = SteppingClock();
		using var panel = new MessagePanelViewModel(clock);

		panel.Report(Failure("archive"));
		panel.Report(Failure("catalogue"));

		panel.Entries.Should().HaveCount(2);
		panel.Entries.Select(entry => entry.View.Title).Should().Equal("catalogue", "archive");
		panel.Entries.Should().OnlyContain(entry => entry.RepeatCount == 1);
	}

	[AvaloniaFact]
	public void ReportStartsANewEntryWhenAnotherFailureCameBetween()
	{
		var clock = SteppingClock();
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
		var clock = SteppingClock();
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
		using var panel = new MessagePanelViewModel(SteppingClock());

		panel.HasEntries.Should().BeFalse("a session that never fails is not given the row");

		panel.Report(Failure("archive"));

		panel.HasEntries.Should().BeTrue("the entry would otherwise land off screen");
	}

	[AvaloniaFact]
	public void ReportKeepsDifferentExceptionMessagesApartAndCoalescesIdenticalOnes()
	{
		using var panel = new MessagePanelViewModel(SteppingClock());

		panel.Report(Thrown("the port is taken"));
		panel.Report(Thrown("the port is taken"));
		panel.Report(Thrown("the device stopped answering"));

		panel.Entries.Should().HaveCount(2);
		panel.Entries.Select(entry => entry.RepeatCount).Should().Equal(1, 2);
	}

	[AvaloniaFact]
	public void HasEntriesNotifiesWhenTheFirstEntryArrives()
	{
		using var panel = new MessagePanelViewModel(SteppingClock());
		var raised = new List<string?>();
		panel.PropertyChanged += (_, arguments) => raised.Add(arguments.PropertyName);

		panel.Report(Failure("archive"));

		raised.Should().Contain(nameof(MessagePanelViewModel.HasEntries));
	}

	[AvaloniaFact]
	public void ReportAnswersWhetherItCoalescedAgainstTheNewestEntryOnly()
	{
		using var panel = new MessagePanelViewModel(SteppingClock());
		var archive = Failure("archive");

		panel.Report(archive).Should().BeFalse("the list is empty");
		panel.Report(archive).Should().BeTrue();
		panel.Report(Failure("catalogue")).Should().BeFalse();
		panel.Report(archive).Should().BeFalse("another failure is now the newest entry");
	}

	[AvaloniaFact]
	public void ClearCommandEmptiesTheListAndTakesThePanelOffScreen()
	{
		using var panel = new MessagePanelViewModel(SteppingClock());
		panel.Report(Failure("archive"));

		panel.ClearCommand.Execute().Subscribe();

		panel.Entries.Should().BeEmpty();
		panel.HasEntries.Should().BeFalse();
	}

	[AvaloniaFact]
	public void ReportKeepsTwoViewsThatDifferOnlyInSeverityApart()
	{
		using var panel = new MessagePanelViewModel(SteppingClock());

		panel.Report(Failure("archive", MessageSeverity.Warning)).Should().BeFalse();
		panel.Report(Failure("archive", MessageSeverity.Error)).Should().BeFalse();

		panel.Entries.Select(entry => entry.View.Severity)
			.Should().Equal(MessageSeverity.Error, MessageSeverity.Warning);
	}

	private static MessageView Failure(string title, MessageSeverity severity = MessageSeverity.Error)
	{
		return new MessageView(title, "detail", "remedy", severity);
	}

	private static MessageView Thrown(string message)
	{
		var exception = new InvalidOperationException(message);

		return ErrorMessageMapper.Map(new ExceptionalError(exception.Message, exception));
	}

	private static FakeTimeProvider SteppingClock()
	{
		return new FakeTimeProvider(_origin) { AutoAdvanceAmount = _step };
	}
}
