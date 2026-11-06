using Serilog.Core;
using Serilog.Events;

namespace SpectrumVisualizer.Tests.Unit.UI.Logging;

/// <summary>Keeps every event that passed the logger's level, in the order it was written.</summary>
internal sealed class CollectingSink : ILogEventSink
{
	private readonly List<LogEvent> _events = [];

	public IReadOnlyList<LogEvent> Events => _events;

	public void Emit(LogEvent logEvent)
	{
		_events.Add(logEvent);
	}
}
