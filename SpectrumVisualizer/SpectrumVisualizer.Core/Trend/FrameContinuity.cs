using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.Core.Trend;

/// <summary>Turns spectra and heartbeats, in receive order, into the trend's breaks and failure marks.</summary>
public sealed class FrameContinuity
{
	private const BoardState FailureBits = BoardState.Atp2000MeasurementFailed | BoardState.Atp8000MeasurementFailed;

	/// <summary>The link's silence limit: a longer pause ends the seq session and breaks the line.</summary>
	private static readonly TimeSpan _silenceLimit = TimeSpan.FromSeconds(10);

	private readonly SequenceTracker _tracker = new();
	private DateTimeOffset? _lastFrameAt;
	private DateTimeOffset? _lastSpectrumAt;

	public Continuity Observe(ReceivedFrame received)
	{
		var (frame, receivedAt) = received;
		var unixMs = receivedAt.ToUnixTimeMilliseconds();

		if (receivedAt - _lastFrameAt > _silenceLimit)
		{
			_tracker.Reset();
		}

		_lastFrameAt = receivedAt;
		var lost = _tracker.Observe(frame);
		TrendBreak? loss = lost > 0 ? new TrendBreak(unixMs, BreakKind.Loss, lost) : null;

		TrendBreak? noSpectra = null;
		if (frame.Measurement is not null)
		{
			if (receivedAt - _lastSpectrumAt > _silenceLimit)
			{
				noSpectra = new TrendBreak(unixMs, BreakKind.NoSpectra, 0);
			}

			_lastSpectrumAt = receivedAt;
		}

		var failedBits = frame.State & FailureBits;
		TrendFailure? failure = failedBits == BoardState.None ? null : new TrendFailure(unixMs, failedBits);

		return new Continuity(loss, noSpectra, failure);
	}
}

/// <summary>What one frame adds to the trend: a Loss break, a NoSpectra break, a failure mark, each optional.</summary>
public readonly record struct Continuity(TrendBreak? Loss, TrendBreak? NoSpectra, TrendFailure? Failure);

/// <summary>A cut in the trend line at a frame; Lost counts the spectra lost before it, 0 for NoSpectra.</summary>
public readonly record struct TrendBreak(long UnixMs, BreakKind Kind, int Lost);

public enum BreakKind
{
	Loss,
	NoSpectra,
}

/// <summary>A frame whose state carried measurement-failed bits; State holds only those bits.</summary>
public readonly record struct TrendFailure(long UnixMs, BoardState State);
