using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.Core.Trend;

/// <summary>
/// Turns spectra and heartbeats, in receive order, into the trend's breaks and failure marks; a pause over the link's
/// silence limit ends the seq session and breaks the line.
/// </summary>
public sealed class FrameContinuity
{
	private readonly SequenceTracker _tracker = new();
	private DateTimeOffset? _lastFrameAt;
	private DateTimeOffset? _lastSpectrumAt;

	/// <summary>
	/// The board measures with one spectrometer at a time and keeps the idle one's failed bit until that one measures
	/// again, so only the measuring device's bit marks a failure.
	/// </summary>
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

		TrendBreak? noSpectra = null;
		if (frame.Measurement is not null)
		{
			if (receivedAt - _lastSpectrumAt > LinkTiming.SilenceLimit)
			{
				noSpectra = new TrendBreak(unixMs, 0);
			}

			_lastSpectrumAt = receivedAt;
			_measuringDevice = frame.Measurement.Device;
		}

		var failedBits = _measuringDevice is { } device ? frame.State & device.FailedBit : BoardState.None;
		TrendFailure? failure = failedBits == BoardState.None ? null : new TrendFailure(unixMs, failedBits);

		return new Continuity(loss, noSpectra, failure);
	}
}

/// <summary>What one frame adds to the trend: a Loss break, a NoSpectra break, a failure mark, each optional.</summary>
public readonly record struct Continuity(TrendBreak? Loss, TrendBreak? NoSpectra, TrendFailure? Failure);

/// <summary>
/// A cut in the trend line at a frame; Lost counts the spectra lost before it, 0 ends a stretch without spectra.
/// </summary>
public readonly record struct TrendBreak(long UnixMs, int Lost);

/// <summary>
/// A frame whose state carried the failed bit of the device that measured the latest spectrum; State holds that bit.
/// </summary>
public readonly record struct TrendFailure(long UnixMs, BoardState State);
