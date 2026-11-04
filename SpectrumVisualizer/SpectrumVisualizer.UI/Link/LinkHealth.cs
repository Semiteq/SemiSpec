using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.UI.Link;

/// <summary>The OptoskyProj#20 health rules of an open port, as pure functions of the read loop's monotonic stamps.</summary>
internal static class LinkHealth
{
	public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

	/// <summary>The ATP-2000 spectrum frame, the largest the board sends; a tail shorter than this is a stopped frame.</summary>
	public static readonly int LargestFrameBytes = Spectrometer.Atp2000.FrameSize;

	/// <summary>The reference is the last valid frame, or the port open before the first one.</summary>
	public static LinkState Evaluate(
		TimeSpan now,
		TimeSpan openedAt,
		TimeSpan? lastByteAt,
		TimeSpan? lastFrameAt,
		long bytesSinceFrame)
	{
		var reference = lastFrameAt ?? openedAt;
		if (now - reference < Window)
		{
			return lastFrameAt is null ? LinkState.Waiting : LinkState.Receiving;
		}

		var byteInWindow = lastByteAt is { } byteAt && now - byteAt < Window;
		return byteInWindow && bytesSinceFrame > LargestFrameBytes ? LinkState.OtherDevice : LinkState.NotResponding;
	}

	public static bool IsDrainDue(
		TimeSpan now,
		TimeSpan reference,
		TimeSpan? lastDrainAt,
		TimeSpan? lastByteAt)
	{
		var since = lastDrainAt is { } drainAt && drainAt > reference ? drainAt : reference;
		var byteSinceReference = lastByteAt is { } byteAt && byteAt > reference;
		return byteSinceReference && now - since >= Window;
	}
}
