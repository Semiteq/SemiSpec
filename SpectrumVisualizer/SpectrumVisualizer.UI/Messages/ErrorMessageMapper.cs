using FluentResults;

using SpectrumVisualizer.UI.Localization;

namespace SpectrumVisualizer.UI.Messages;

/// <summary>
/// Turns an <see cref="IError"/> into the state the operator reads: a title, a detail and a remedy.
/// The detail keeps the error's own message, so distinct failures stay distinct panel rows.
/// </summary>
public static class ErrorMessageMapper
{
	public static MessageView Map(IError error)
	{
		return error switch
		{
			IExceptionalError thrown => MapThrown(thrown),
			_ => MapUnknown(error)
		};
	}

	private static MessageView MapThrown(IExceptionalError error)
	{
		return new MessageView(
			Resources.FailureThrownTitle,
			Resources.FormatFailureThrownDetail(error.Exception.GetType().Name, error.Exception.Message),
			Resources.FailureThrownRemedy,
			MessageSeverity.Error);
	}

	private static MessageView MapUnknown(IError error)
	{
		return new MessageView(
			Resources.FailureUnknownTitle,
			error.Message,
			Resources.FailureUnknownRemedy,
			MessageSeverity.Error);
	}
}
