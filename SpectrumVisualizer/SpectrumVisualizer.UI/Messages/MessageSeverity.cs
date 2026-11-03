namespace SpectrumVisualizer.UI.Messages;

/// <summary>
/// How much the operator must do about a message. The axis is action, not volume.
/// </summary>
public enum MessageSeverity
{
	/// <summary>The application recovers by itself; the entry records that it happened.</summary>
	Info,

	/// <summary>The operation failed but retries by itself; the operator watches rather than acts.</summary>
	Warning,

	/// <summary>Nothing recovers until the operator changes something outside the application.</summary>
	Error
}
