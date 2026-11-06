using System.Reactive.Concurrency;

using FluentResults;

using Microsoft.Extensions.Logging;

namespace SpectrumVisualizer.UI.Messages;

/// <summary>
/// The last-resort route to the operator: what ReactiveUI raises through its own machinery.
/// </summary>
public sealed class UnhandledErrorObserver(
	Func<MessagePanelViewModel?> resolvePanel,
	IScheduler uiScheduler,
	ILogger logger) : IObserver<Exception>
{
	public void OnNext(Exception value)
	{
		var panel = resolvePanel();

		if (panel is null)
		{
			logger.LogError(value, "ReactiveUI raised an unhandled exception");

			return;
		}

		panel.TryReportFailure(new ExceptionalError(value.Message, value), logger, uiScheduler);
	}

	public void OnError(Exception error)
	{
		OnNext(error);
	}

	public void OnCompleted()
	{
	}
}
