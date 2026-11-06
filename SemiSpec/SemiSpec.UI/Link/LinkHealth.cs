using SemiSpec.Core.Protocol;

namespace SemiSpec.UI.Link;

/// <summary>
/// The OptoskyProj#20 health rules of an open port, as pure functions of the read loop's monotonic stamps.
/// </summary>
internal static class LinkHealth
{
	/// <summary>
	/// The ATP-2000 spectrum frame, the largest the board sends; a tail shorter than this is a stopped frame.
	/// </summary>
	private static readonly int _largestFrameBytes = Spectrometer.Atp2000.FrameSize;

	public static LinkState Evaluate(
		TimeSpan now,
		TimeSpan openedAt,
		TimeSpan? lastByteAt,
		TimeSpan? lastFrameAt,
		long bytesSinceFrame)
	{
		var reference = lastFrameAt ?? openedAt;
		if (now - reference < LinkTiming.SilenceLimit)
		{
			return lastFrameAt is null ? LinkState.Waiting : LinkState.Receiving;
		}

		var byteInWindow = lastByteAt is { } byteAt && now - byteAt < LinkTiming.SilenceLimit;
		return byteInWindow && bytesSinceFrame > _largestFrameBytes ? LinkState.OtherDevice : LinkState.NotResponding;
	}

	public static bool IsDrainDue(
		TimeSpan now,
		TimeSpan reference,
		TimeSpan? lastDrainAt,
		TimeSpan? lastByteAt)
	{
		var since = lastDrainAt is { } drainAt && drainAt > reference ? drainAt : reference;
		var byteSinceReference = lastByteAt is { } byteAt && byteAt > reference;
		return byteSinceReference && now - since >= LinkTiming.SilenceLimit;
	}
}
