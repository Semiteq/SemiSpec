using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using Serilog;
using Serilog.Core;
using Serilog.Events;

using SpectrumVisualizer.UI;
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

		_sink.Levels.Should().Equal(LogEventLevel.Information);
	}

	[Fact]
	public void DebugEventsPassWhenVerbose()
	{
		using var logger = CreateLogger();

		_control.IsVerbose = true;
		logger.Debug("shown");

		_control.IsVerbose.Should().BeTrue();
		_sink.Levels.Should().Equal(LogEventLevel.Debug);
	}

	[Fact]
	public void DebugEventsAreFilteredAgainAfterVerboseIsSwitchedOff()
	{
		using var logger = CreateLogger();

		_control.IsVerbose = true;
		_control.IsVerbose = false;
		logger.Debug("hidden");

		_sink.Levels.Should().BeEmpty();
	}

	[Fact]
	public void AddUiRegistersTheGivenControlAsTheSingleInstance()
	{
		using var provider = new ServiceCollection().AddUi(_control).BuildServiceProvider();

		provider.GetRequiredService<LogLevelControl>().Should().BeSameAs(_control);
		provider.GetRequiredService<LogLevelControl>().Should().BeSameAs(_control);
	}

	private Logger CreateLogger()
	{
		return new LoggerConfiguration()
			.MinimumLevel.ControlledBy(_control.Switch)
			.WriteTo.Sink(_sink)
			.CreateLogger();
	}

	private sealed class CollectingSink : ILogEventSink
	{
		private readonly List<LogEventLevel> _levels = [];

		public IReadOnlyList<LogEventLevel> Levels => _levels;

		public void Emit(LogEvent logEvent)
		{
			_levels.Add(logEvent.Level);
		}
	}
}
