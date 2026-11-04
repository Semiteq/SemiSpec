using Serilog.Core;
using Serilog.Events;

namespace SpectrumVisualizer.Tests.Unit.UI.Logging;

/// <summary>Keeps every event that passed the logger's level, in the order it was written.</summary>
internal sealed class CollectingSink : ILogEventSink
{
	private readonly Lock _gate = new();

	// Written by the test thread, the link's session task and the frame distributor's read task.
	private readonly List<LogEvent> _events = [];

	/// <summary>A snapshot of the events written so far.</summary>
	public IReadOnlyList<LogEvent> Events
	{
		get
		{
			lock (_gate)
			{
				return [.. _events];
			}
		}
	}

	public void Emit(LogEvent logEvent)
	{
		lock (_gate)
		{
			_events.Add(logEvent);
		}
	}
}
