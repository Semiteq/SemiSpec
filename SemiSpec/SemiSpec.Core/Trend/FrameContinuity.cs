using SemiSpec.Core.Protocol;

namespace SemiSpec.Core.Trend;

/// <summary>
/// Turns spectra and heartbeats, in receive order, into the trend's Loss breaks and failure marks; a pause over the
/// link's silence limit ends the seq session.
/// </summary>
public sealed class FrameContinuity
{
	private readonly SequenceTracker _tracker = new();
	private DateTimeOffset? _lastFrameAt;

	/// <summary>The board keeps the idle device's failed bit, so only the latest spectrum's device marks.</summary>
	private Spectrometer? _measuringDevice;

	public Continuity Observe(ReceivedFrame received)
	{
		var (frame, receivedAt) = received;
		var unixMs = receivedAt.ToUnixTimeMilliseconds();

		if (receivedAt - _lastFrameAt > LinkTiming.SilenceLimit)
		{
			_tracker.Reset();
		}

		_lastFrameAt = receivedAt;
		var lost = _tracker.Observe(frame);
		TrendBreak? loss = lost > 0 ? new TrendBreak(unixMs, lost) : null;

		if (frame.Measurement is not null)
		{
			_measuringDevice = frame.Measurement.Device;
		}

		var failedBits = _measuringDevice is { } device ? frame.State & device.FailedBit : BoardState.None;
		TrendFailure? failure = failedBits == BoardState.None ? null : new TrendFailure(unixMs, failedBits);

		return new Continuity(loss, failure);
	}
}

/// <summary>What one frame adds to the trend: a Loss break and a failure mark, each optional.</summary>
public readonly record struct Continuity(TrendBreak? Loss, TrendFailure? Failure);

/// <summary>
/// A cut in the trend line at a frame; Lost counts the spectra lost before it, 0 ends a stretch without spectra.
/// </summary>
public readonly record struct TrendBreak(long UnixMs, int Lost);

/// <summary>
/// A frame whose state carried the failed bit of the device that measured the latest spectrum; State holds that bit.
/// </summary>
public readonly record struct TrendFailure(long UnixMs, BoardState State);
