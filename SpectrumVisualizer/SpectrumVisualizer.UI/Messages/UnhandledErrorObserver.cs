using System.Reactive.Concurrency;

using Avalonia.Threading;

using FluentResults;

using Microsoft.Extensions.Logging;

namespace SpectrumVisualizer.UI.Messages;

/// <summary>
/// The last-resort route to the operator: what ReactiveUI raises through its own machinery and what a
/// dispatcher job throws.
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

	/// <summary>
	/// Reports what a dispatcher job threw; with no panel yet the exception stays unhandled and ends the app.
	/// </summary>
	public void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
	{
		try
		{
			if (resolvePanel() is not { } panel)
			{
				return;
			}

			panel.TryReportFailure(new ExceptionalError(e.Exception.Message, e.Exception), logger, uiScheduler);
			e.Handled = true;
		}
		catch (Exception handlerFailure)
		{
			logger.LogError(handlerFailure, "The dispatcher exception could not reach the message panel");
		}
	}
}
