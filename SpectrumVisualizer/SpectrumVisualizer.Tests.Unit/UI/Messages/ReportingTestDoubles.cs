using System.Collections.Specialized;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using SpectrumVisualizer.UI.Messages;

namespace SpectrumVisualizer.Tests.Unit.UI.Messages;

internal static class ReportingTestDoubles
{
	/// <summary>
	/// Makes the panel's bound collection throw on every edit, until the returned handle is disposed.
	/// </summary>
	public static IDisposable PoisonEntries(MessagePanelViewModel panel)
	{
		var entries = (INotifyCollectionChanged)panel.Entries;
		entries.CollectionChanged += AlwaysThrow;

		return new Unpoison(entries);
	}

	private static void AlwaysThrow(object? sender, NotifyCollectionChangedEventArgs arguments)
	{
		throw new PoisonedPanelException();
	}

	private sealed class Unpoison(INotifyCollectionChanged entries) : IDisposable
	{
		public void Dispose()
		{
			entries.CollectionChanged -= AlwaysThrow;
		}
	}
}

/// <summary>What the poisoned panel throws.</summary>
internal sealed class PoisonedPanelException() : InvalidOperationException("the bound list threw");

/// <summary>Reads what a <see cref="FakeLogger"/> kept, in the order the code under test wrote it.</summary>
internal static class FakeLoggerReading
{
	public static IReadOnlyList<LogLevel> Levels(this FakeLogger logger)
	{
		return [.. logger.Collector.GetSnapshot().Select(record => record.Level)];
	}

	public static IReadOnlyList<Exception> Failures(this FakeLogger logger)
	{
		return [.. logger.Collector.GetSnapshot().Select(record => record.Exception).OfType<Exception>()];
	}
}

internal sealed class ThrowingLogger : ILogger
{
	public IDisposable? BeginScope<TState>(TState state)
		where TState : notnull
	{
		return null;
	}

	public bool IsEnabled(LogLevel logLevel)
	{
		return true;
	}

	public void Log<TState>(
		LogLevel logLevel,
		EventId eventId,
		TState state,
		Exception? exception,
		Func<TState, Exception?, string> formatter)
	{
		throw new InvalidOperationException("the log sink threw");
	}
}
