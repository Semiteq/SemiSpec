using System.Reactive.Concurrency;

using FluentResults;

using Microsoft.Extensions.Logging;

namespace SpectrumVisualizer.UI.Messages;

/// <summary>
/// The one route from a failed <see cref="Result"/> to the operator: map it, log it, show it.
/// </summary>
public static class ResultReporting
{
	/// <summary>Reports a failed result and does nothing for a successful one.</summary>
	public static void ReportFailure(this MessagePanelViewModel panel, IResultBase result, ILogger logger)
	{
		if (result.IsSuccess)
		{
			return;
		}

		panel.ReportFailure(result.Errors, logger);
	}

	/// <summary>
	/// One entry per failed operation, so a repeated failure still coalesces; the errors the entry leaves
	/// out still reach the log.
	/// </summary>
	public static void ReportFailure(
		this MessagePanelViewModel panel, IReadOnlyList<IError> errors, ILogger logger)
	{
		panel.ReportFailure(errors[0], logger);

		for (var index = 1; index < errors.Count; index++)
		{
			logger.LogWarning("Failure {Index} of the same operation: {Message}", index, errors[index].Message);
		}
	}

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
			// The log line is written even when the panel throws: no failure stops at the panel.
			logger.Log(
				coalesced ? LogLevel.Debug : ToLogLevel(view.Severity),
				// The mapped detail keeps type and message only, so the object carries the stack.
				(error as IExceptionalError)?.Exception,
				"{Severity}: {Title}. {Detail} {Remedy}",
				view.Severity,
				view.Title,
				view.Detail,
				view.Remedy);
		}
	}

	/// <summary>
	/// Reports without ever throwing, for a caller whose escape would end an Rx stream or a dispatcher job.
	/// </summary>
	public static void TryReportFailure(this MessagePanelViewModel panel, IError error, ILogger logger)
	{
		panel.TryReportFailure([error], logger);
	}

	/// <summary>Reports every error of one failure without ever throwing; the first becomes the entry.</summary>
	public static void TryReportFailure(
		this MessagePanelViewModel panel, IReadOnlyList<IError> errors, ILogger logger)
	{
		try
		{
			panel.ReportFailure(errors, logger);
		}
		catch (Exception reportFailure)
		{
			LogQuietly(logger, reportFailure);
		}
	}

	/// <summary>
	/// Reports from a thread that is not the UI one: the panel mutates a bound collection, so the report
	/// travels to the UI scheduler and is guarded there.
	/// </summary>
	public static void TryReportFailure(
		this MessagePanelViewModel panel, IError error, ILogger logger, IScheduler uiScheduler)
	{
		uiScheduler.Schedule(() => panel.TryReportFailure(error, logger));
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
			// the stream this guard exists for.
		}
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
}
