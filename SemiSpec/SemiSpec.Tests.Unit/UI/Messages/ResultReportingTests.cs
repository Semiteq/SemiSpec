using System.Reactive.Concurrency;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Reactive.Testing;

using SemiSpec.UI.Link;
using SemiSpec.UI.Messages;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Messages;

// The entry's text is asserted against the mapper's own output rather than against a literal, so a
// reworded resx value moves both sides and only a seam that stops calling the mapper turns these red.
[Trait("Component", "UI")]
[Trait("Area", "Messages")]
[Trait("Category", "Unit")]
public sealed class ResultReportingTests : IDisposable
{
	private readonly MessagePanelViewModel _panel = new(TimeProvider.System);

	public void Dispose()
	{
		_panel.Dispose();
	}

	[AvaloniaFact]
	public void ReportFailureTakesTitleDetailAndRemedyFromTheMapper()
	{
		var error = new Error("the port is taken");

		_panel.ReportFailure(error, NullLogger.Instance);

		_panel.Entries.Should().ContainSingle();
		_panel.Entries[0].View.Should().Be(ErrorMessageMapper.Map(error));
	}

	[AvaloniaFact]
	public void ReportFailureLogsAndShowsTheFailure()
	{
		var logger = new FakeLogger();
		var thrown = new InvalidOperationException("the frame was cut short");

		_panel.ReportFailure(new ExceptionalError(thrown.Message, thrown), logger);

		_panel.Entries.Should().ContainSingle();
		_panel.HasEntries.Should().BeTrue();
		logger.Levels().Should().Equal(LogLevel.Error);
		logger.Failures().Should().Equal(thrown);
	}

	[AvaloniaFact]
	public void ReportFailureLogsTheExceptionOfAWrappedCause()
	{
		var logger = new FakeLogger();
		var thrown = new IOException("the port list could not be read");

		_panel.ReportFailure(new PortListError().CausedBy(new ExceptionalError(thrown.Message, thrown)), logger);

		logger.Failures().Should().Equal(thrown);
	}

	[AvaloniaFact]
	public void ReportFailureLogsTheDeepestExceptionOfTheReasonChain()
	{
		var logger = new FakeLogger();
		var outer = new InvalidOperationException("the batch failed");
		var inner = new IOException("the disk is full");
		var error = new ExceptionalError(outer.Message, outer)
			.CausedBy(new Error("the write failed").CausedBy(new ExceptionalError(inner.Message, inner)));

		_panel.ReportFailure(error, logger);

		logger.Failures().Should().Equal(inner);
	}

	[AvaloniaFact]
	public void ReportFailureDropsARepeatOfTheNewestEntryToDebug()
	{
		var logger = new FakeLogger();
		var repeated = new Error("the port is taken");

		_panel.ReportFailure(repeated, logger);
		_panel.ReportFailure(repeated, logger);
		_panel.ReportFailure(repeated, logger);
		_panel.ReportFailure(new Error("the device stopped answering"), logger);

		_panel.Entries.Should().HaveCount(2);
		logger.Levels().Should().Equal(LogLevel.Error, LogLevel.Debug, LogLevel.Debug, LogLevel.Error);
	}

	[AvaloniaFact]
	public void ReportFailureLogsAWarningErrorAtWarningAndItsRepeatAtDebug()
	{
		var logger = new FakeLogger();

		_panel.ReportFailure(new PortLostError("COM3"), logger);
		_panel.ReportFailure(new PortLostError("COM4"), logger);

		_panel.Entries.Should().ContainSingle().Which.View.Severity.Should().Be(MessageSeverity.Warning);
		logger.Levels().Should().Equal(LogLevel.Warning, LogLevel.Debug);
	}

	[AvaloniaFact]
	public void ReportFailureWithALogSinkThatThrowsStillShowsTheEntry()
	{
		var error = new Error("the port is taken");

		var report = () => _panel.ReportFailure(error, new ThrowingLogger());

		report.Should().Throw<InvalidOperationException>();
		_panel.Entries.Should().ContainSingle().Which.View.Should().Be(ErrorMessageMapper.Map(error));
	}

	[AvaloniaFact]
	public void TryReportFailureWithAPanelThatThrowsKeepsTheFailureInTheLog()
	{
		var logger = new FakeLogger();
		using var poison = ReportingTestDoubles.PoisonEntries(_panel);

		var report = () => _panel.TryReportFailure(new Error("the port is taken"), logger, ImmediateScheduler.Instance);

		report.Should().NotThrow();
		logger.Levels().Should().Equal(LogLevel.Error, LogLevel.Error);
	}

	[AvaloniaFact]
	public void TryReportFailureLogsAtOnceWhenTheSchedulerNeverRunsTheJob()
	{
		var logger = new FakeLogger();
		var thrown = new IOException("the disk is full");

		_panel.TryReportFailure(new ExceptionalError(thrown.Message, thrown), logger, new TestScheduler());

		logger.Levels().Should().Equal(LogLevel.Error);
		logger.Failures().Should().Equal(thrown);
		_panel.Entries.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void TryReportFailureJobAddsTheEntryWithoutASecondLogLine()
	{
		var logger = new FakeLogger();
		var scheduler = new TestScheduler();
		var error = new Error("the port is taken");

		_panel.TryReportFailure(error, logger, scheduler);
		scheduler.AdvanceBy(1);

		_panel.Entries.Should().ContainSingle().Which.View.Should().Be(ErrorMessageMapper.Map(error));
		logger.Levels().Should().Equal(LogLevel.Error);
	}

	[AvaloniaFact]
	public void TryReportFailureWithALogSinkThatThrowsDoesNotThrow()
	{
		var error = new Error("the port is taken");

		var report = () => _panel.TryReportFailure(error, new ThrowingLogger(), ImmediateScheduler.Instance);

		report.Should().NotThrow();
		_panel.Entries.Should().ContainSingle().Which.View.Should().Be(ErrorMessageMapper.Map(error));
	}
}
