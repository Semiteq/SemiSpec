using AwesomeAssertions;

using Serilog;
using Serilog.Core;
using Serilog.Events;

using SpectrumVisualizer.UI.Logging;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Logging;

[Trait("Component", "UI")]
[Trait("Area", "Logging")]
[Trait("Category", "Unit")]
public sealed class LogLevelControlTests
{
	private readonly LogLevelControl _control = new();
	private readonly CollectingSink _sink = new();

	[Fact]
	public void TheControlStartsNotVerbose()
	{
		_control.IsVerbose.Should().BeFalse();
	}

	[Fact]
	public void DebugEventsAreFilteredAtTheDefaultLevel()
	{
		using var logger = CreateLogger();

		logger.Debug("hidden");
		logger.Information("shown");

		PassedLevels().Should().Equal(LogEventLevel.Information);
	}

	[Fact]
	public void DebugEventsPassWhenVerbose()
	{
		using var logger = CreateLogger();

		_control.IsVerbose = true;
		logger.Debug("shown");

		_control.IsVerbose.Should().BeTrue();
		PassedLevels().Should().Equal(LogEventLevel.Debug);
	}

	[Fact]
	public void DebugEventsAreFilteredAgainAfterVerboseIsSwitchedOff()
	{
		using var logger = CreateLogger();

		_control.IsVerbose = true;
		_control.IsVerbose = false;
		logger.Debug("hidden");

		_sink.Events.Should().BeEmpty();
	}

	private Logger CreateLogger()
	{
		return new LoggerConfiguration()
			.MinimumLevel.ControlledBy(_control.Switch)
			.WriteTo.Sink(_sink)
			.CreateLogger();
	}

	private IEnumerable<LogEventLevel> PassedLevels()
	{
		return _sink.Events.Select(logEvent => logEvent.Level);
	}
}
