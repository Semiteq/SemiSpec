using System.Reactive.Concurrency;

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
public sealed class ResultReportingTests : IDisposable
{
	private readonly MessagePanelViewModel _panel = new(TimeProvider.System);

	public void Dispose()
	{
		_panel.Dispose();
	}

	[Fact]
	public void ReportFailureTakesTitleDetailAndRemedyFromTheMapper()
	{
		var error = new Error("the port is taken");

		_panel.ReportFailure(error, NullLogger.Instance);

		_panel.Entries.Should().ContainSingle();
		_panel.Entries[0].View.Should().Be(ErrorMessageMapper.Map(error));
	}

	[Fact]
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

	[Fact]
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

	// The log and the panel are independent sinks.
	[Fact]
	public void ReportFailureWithALogSinkThatThrowsStillShowsTheEntry()
	{
		var error = new Error("the port is taken");

		var report = () => _panel.ReportFailure(error, new ThrowingLogger());

		report.Should().Throw<InvalidOperationException>();
		_panel.Entries.Should().ContainSingle().Which.View.Should().Be(ErrorMessageMapper.Map(error));
	}

	// The caller is an Rx onError or a dispatcher job, so the guard swallows what the plain overload throws.
	[Fact]
	public void TryReportFailureWithAPanelThatThrowsKeepsTheFailureInTheLog()
	{
		var logger = new FakeLogger();
		using var poison = ReportingTestDoubles.PoisonEntries(_panel);

		var report = () => _panel.TryReportFailure(new Error("the port is taken"), logger, ImmediateScheduler.Instance);

		report.Should().NotThrow();
		logger.Levels().Should().Equal(LogLevel.Error, LogLevel.Error);
	}

	[Fact]
	public void TryReportFailureWithALogSinkThatThrowsDoesNotThrow()
	{
		var error = new Error("the port is taken");

		var report = () => _panel.TryReportFailure(error, new ThrowingLogger(), ImmediateScheduler.Instance);

		report.Should().NotThrow();
		_panel.Entries.Should().ContainSingle().Which.View.Should().Be(ErrorMessageMapper.Map(error));
	}
}
