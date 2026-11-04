using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.UI.Link;

/// <summary>A decoded frame and the wall-clock time the link released it from the decoder.</summary>
public sealed record ReceivedFrame(BoardFrame Frame, DateTimeOffset ReceivedAt);
