using System.Reactive.Concurrency;

using FluentResults;

using Microsoft.Extensions.Logging;

namespace SemiSpec.UI.Messages;

/// <summary>
/// The one route from a failure to the operator: map it, log it, show it.
/// </summary>
public static class ResultReporting
{
	public static void ReportFailure(this MessagePanelViewModel panel, IError error, ILogger logger)
	{
		var view = ErrorMessageMapper.Map(error);
		var coalesced = false;

		try
		{
			coalesced = panel.Report(view);
		}
		finally
		{
			Log(logger, error, view, coalesced ? LogLevel.Debug : ToLogLevel(view.Severity));
		}
	}

	/// <summary>
	/// The deepest exception in the errors' reason chains, so a wrapped failure logs its original stack.
	/// </summary>
	internal static Exception? CauseOf(IEnumerable<IError> errors)
	{
		return errors.Select(CauseOf).FirstOrDefault(cause => cause is not null);
	}

	private static Exception? CauseOf(IError error)
	{
		return CauseOf(error.Reasons) ?? (error as IExceptionalError)?.Exception;
	}

	private static void Log(ILogger logger, IError error, MessageView view, LogLevel level)
	{
		logger.Log(level, CauseOf(error), "{Title}. {Detail} {Remedy}", view.Title, view.Detail, view.Remedy);
	}

	private static LogLevel ToLogLevel(MessageSeverity severity)
	{
		return severity switch
		{
			MessageSeverity.Error => LogLevel.Error,
			MessageSeverity.Warning => LogLevel.Warning,
			_ => LogLevel.Information
		};
	}

	/// <summary>
	/// Reports from a thread that is not the UI one and never throws. The log line is written at once on the
	/// calling thread, so it survives a dispatcher that has already stopped; only the panel entry, a bound
	/// collection, travels to the UI scheduler, where an escape would end the dispatcher job.
	/// </summary>
	public static void TryReportFailure(
		this MessagePanelViewModel panel, IError error, ILogger logger, IScheduler uiScheduler)
	{
		MessageView view;
		try
		{
			view = ErrorMessageMapper.Map(error);
		}
		catch (Exception mapFailure)
		{
			LogQuietly(logger, mapFailure);
			return;
		}

		try
		{
			Log(logger, error, view, ToLogLevel(view.Severity));
		}
		catch (Exception)
		{
			// The log sink is what threw; the panel entry below is the failure's one remaining route.
		}

		uiScheduler.Schedule(() => ShowGuarded(panel, view, logger));
	}

	private static void ShowGuarded(MessagePanelViewModel panel, MessageView view, ILogger logger)
	{
		try
		{
			panel.Report(view);
		}
		catch (Exception reportFailure)
		{
			LogQuietly(logger, reportFailure);
		}
	}

	private static void LogQuietly(ILogger logger, Exception reportFailure)
	{
		try
		{
			logger.LogError(reportFailure, "The failure could not reach the message panel");
		}
		catch (Exception)
		{
			// The log sink is what threw, so there is nowhere left to write and an escape here would end
			// the dispatcher job this guard exists for.
		}
	}
}
