using System.Reactive.Concurrency;

using FluentResults;

using Microsoft.Extensions.Logging;

namespace SpectrumVisualizer.UI.Messages;

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
			logger.Log(
				coalesced ? LogLevel.Debug : ToLogLevel(view.Severity),
				(error as IExceptionalError)?.Exception,
				"{Title}. {Detail} {Remedy}",
				view.Title,
				view.Detail,
				view.Remedy);
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

	/// <summary>
	/// Reports from a thread that is not the UI one and never throws: the panel mutates a bound collection,
	/// so the report travels to the UI scheduler, where an escape would end the dispatcher job.
	/// </summary>
	public static void TryReportFailure(
		this MessagePanelViewModel panel, IError error, ILogger logger, IScheduler uiScheduler)
	{
		uiScheduler.Schedule(() => ReportGuarded(panel, error, logger));
	}

	private static void ReportGuarded(MessagePanelViewModel panel, IError error, ILogger logger)
	{
		try
		{
			panel.ReportFailure(error, logger);
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
