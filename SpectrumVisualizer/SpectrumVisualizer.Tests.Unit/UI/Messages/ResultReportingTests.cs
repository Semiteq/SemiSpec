using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;

using SpectrumVisualizer.UI.Messages;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Messages;

// The entry's text is asserted against the mapper's own output rather than against a literal, so a
// reworded resx value moves both sides and only a seam that stops calling the mapper turns these red.
[Trait("Component", "UI")]
[Trait("Area", "Messages")]
[Trait("Category", "Unit")]
public sealed class ResultReportingTests
{
	[Fact]
	public void ReportFailureTakesTitleDetailRemedyAndSeverityFromTheMapper()
	{
		using var panel = new MessagePanelViewModel();
		var error = new Error("the port is taken");

		panel.ReportFailure(error, NullLogger.Instance);

		panel.Entries.Should().ContainSingle();
		panel.Entries[0].View.Should().Be(ErrorMessageMapper.Map(error));
	}

	[Fact]
	public void ReportFailureOverAResultUsesTheFirstErrorOnly()
	{
		using var panel = new MessagePanelViewModel();
		var first = new Error("first");
		var second = new Error("second");
		var result = Result.Fail(first).WithError(second);

		panel.ReportFailure(result, NullLogger.Instance);

		panel.Entries.Should().ContainSingle();
		panel.Entries[0].View.Should().Be(ErrorMessageMapper.Map(first));
	}

	[Fact]
	public void ReportFailureOverAResultLogsTheErrorsTheEntryLeavesOut()
	{
		using var panel = new MessagePanelViewModel();
		var logger = new FakeLogger();

		panel.ReportFailure(Result.Fail("first").WithError("second"), logger);

		logger.Levels().Should().Equal(LogLevel.Error, LogLevel.Warning);
	}

	[Fact]
	public void ReportFailureOverASuccessfulResultWritesNothing()
	{
		using var panel = new MessagePanelViewModel();
		var logger = new FakeLogger();

		panel.ReportFailure(Result.Ok(), logger);

		panel.Entries.Should().BeEmpty();
		logger.Levels().Should().BeEmpty();
	}

	[Fact]
	public void ReportFailureLogsAndShowsTheFailure()
	{
		using var panel = new MessagePanelViewModel();
		var logger = new FakeLogger();
		var thrown = new InvalidOperationException("the frame was cut short");

		panel.ReportFailure(new ExceptionalError(thrown.Message, thrown), logger);

		panel.Entries.Should().ContainSingle();
		panel.IsVisible.Should().BeTrue();
		logger.Levels().Should().Equal(LogLevel.Error);
		logger.Failures().Should().Equal(thrown);
	}

	[Fact]
	public void ReportFailureDropsARepeatOfTheNewestEntryToDebug()
	{
		using var panel = new MessagePanelViewModel();
		var logger = new FakeLogger();
		var repeated = new Error("the port is taken");

		panel.ReportFailure(repeated, logger);
		panel.ReportFailure(repeated, logger);
		panel.ReportFailure(repeated, logger);
		panel.ReportFailure(new Error("the device stopped answering"), logger);

		panel.Entries.Should().HaveCount(2);
		logger.Levels().Should().Equal(LogLevel.Error, LogLevel.Debug, LogLevel.Debug, LogLevel.Error);
	}

	// The log and the panel are independent sinks.
	[Fact]
	public void ReportFailureWithALogSinkThatThrowsStillShowsTheEntry()
	{
		using var panel = new MessagePanelViewModel();
		var error = new Error("the port is taken");

		var report = () => panel.ReportFailure(error, new ThrowingLogger());

		report.Should().Throw<InvalidOperationException>();
		panel.Entries.Should().ContainSingle().Which.View.Should().Be(ErrorMessageMapper.Map(error));
	}

	// The caller is an Rx onError or a dispatcher job, so the guard swallows what the plain overload throws.
	[Fact]
	public void TryReportFailureWithAPanelThatThrowsKeepsTheFailureInTheLog()
	{
		using var panel = new MessagePanelViewModel();
		var logger = new FakeLogger();
		using var poison = ReportingTestDoubles.PoisonEntries(panel);

		var report = () => panel.TryReportFailure(new Error("the port is taken"), logger);

		report.Should().NotThrow();
		logger.Levels().Should().Equal(LogLevel.Error, LogLevel.Error);
	}
}
