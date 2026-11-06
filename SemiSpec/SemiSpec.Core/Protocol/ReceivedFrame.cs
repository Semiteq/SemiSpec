namespace SemiSpec.Core.Protocol;

/// <summary>A decoded frame and the wall-clock time the link released it from the decoder.</summary>
public sealed record ReceivedFrame(BoardFrame Frame, DateTimeOffset ReceivedAt);
