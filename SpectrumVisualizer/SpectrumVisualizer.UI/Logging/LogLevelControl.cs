using Serilog.Core;
using Serilog.Events;

namespace SpectrumVisualizer.UI.Logging;

/// <summary>Run-time minimum level of the application logger: Information, or Debug when verbose.</summary>
public sealed class LogLevelControl
{
	internal LoggingLevelSwitch Switch { get; } = new(LogEventLevel.Information);

	public bool IsVerbose
	{
		get => Switch.MinimumLevel == LogEventLevel.Debug;
		set => Switch.MinimumLevel = value ? LogEventLevel.Debug : LogEventLevel.Information;
	}
}
