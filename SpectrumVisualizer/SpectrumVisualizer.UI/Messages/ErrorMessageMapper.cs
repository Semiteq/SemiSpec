using FluentResults;

using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Store;

namespace SpectrumVisualizer.UI.Messages;

/// <summary>
/// Turns an <see cref="IError"/> into the state the operator reads: a title, a detail, a remedy and a severity.
/// A link error's view leaves out the port name and a store error's view the file path, so a repeat coalesces
/// into one panel row.
/// </summary>
public static class ErrorMessageMapper
{
	public static MessageView Map(IError error)
	{
		return error switch
		{
			PortBusyError => new MessageView(
				Resources.PortBusyTitle,
				Resources.PortBusyDetail,
				Resources.PortBusyRemedy,
				MessageSeverity.Error),
			PortNotFoundError => new MessageView(
				Resources.PortNotFoundTitle,
				Resources.PortNotFoundDetail,
				Resources.PortNotFoundRemedy,
				MessageSeverity.Error),
			PortListError => new MessageView(
				Resources.PortListTitle,
				Resources.PortListDetail,
				Resources.PortListRemedy,
				MessageSeverity.Warning),
			PortLostError => new MessageView(
				Resources.PortLostTitle,
				Resources.PortLostDetail,
				Resources.PortLostRemedy,
				MessageSeverity.Warning),
			BoardNotRespondingError => new MessageView(
				Resources.BoardNotRespondingTitle,
				Resources.BoardNotRespondingDetail,
				Resources.BoardNotRespondingRemedy,
				MessageSeverity.Warning),
			UnrecognisedDataError => new MessageView(
				Resources.UnrecognisedDataTitle,
				Resources.UnrecognisedDataDetail,
				Resources.UnrecognisedDataRemedy,
				MessageSeverity.Warning),
			StoreOpenError => new MessageView(
				Resources.StoreOpenTitle,
				Resources.StoreOpenDetail,
				Resources.StoreOpenRemedy,
				MessageSeverity.Error),
			StoreWriteError => new MessageView(
				Resources.StoreWriteTitle,
				Resources.StoreWriteDetail,
				Resources.StoreWriteRemedy,
				MessageSeverity.Warning),
			StoreStoppedError => new MessageView(
				Resources.StoreStoppedTitle,
				Resources.StoreStoppedDetail,
				Resources.StoreStoppedRemedy,
				MessageSeverity.Error),
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
