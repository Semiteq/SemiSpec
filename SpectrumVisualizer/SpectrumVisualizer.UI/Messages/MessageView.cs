namespace SpectrumVisualizer.UI.Messages;

/// <summary>
/// One message as the operator reads it: <c>Title</c> in one short line, <c>Detail</c> naming what the
/// application observed, <c>Remedy</c> naming the action that fixes it and empty when none is owed.
/// </summary>
public sealed record MessageView(
	string Title,
	string Detail,
	string Remedy);
