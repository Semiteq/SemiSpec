using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.Tools.BoardEmulator;

/// <summary>Decides which frame the emulated board sends at a tick, with the board's seq and heartbeat rules.</summary>
public sealed class FrameSchedule(EmulatorOptions options, TimeProvider timeProvider)
{
	public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(3);

	private readonly long _start = timeProvider.GetTimestamp();

	private readonly BoardState _state =
		options.Device == Spectrometer.Atp2000 ? BoardState.Atp2000Connected : BoardState.Atp8000Connected;

	private readonly SpectrumMeasurement _measurement =
		SpectrumGenerator.Generate(options.Device, options.WavelengthNm, options.Delta);

	private readonly TimeSpan? _spectrumPeriod = options.Rate > 0 ? TimeSpan.FromSeconds(1 / options.Rate) : null;
	private readonly int _badCrcEvery = options.BadCrcEvery;
	private TimeSpan _nextSpectrumAt;
	private TimeSpan _lastSentAt;
	private byte _seq;
	private long _framesSent;

	/// <summary>The bytes of the frame due now, or null when nothing is due.</summary>
	public byte[]? Next()
	{
		var now = timeProvider.GetElapsedTime(_start);
		BoardFrame frame;
		if (_spectrumPeriod is { } period && now >= _nextSpectrumAt)
		{
			_seq++;
			frame = new BoardFrame(_state, _seq, _measurement);
			_nextSpectrumAt += period;
			if (_nextSpectrumAt <= now)
			{
				_nextSpectrumAt = now + period;
			}
		}
		else if (now - _lastSentAt >= HeartbeatInterval)
		{
			frame = new BoardFrame(_state, _seq, null);
		}
		else
		{
			return null;
		}

		_lastSentAt = now;
		_framesSent++;
		var bytes = FrameEncoder.Encode(frame);
		if (_badCrcEvery > 0 && _framesSent % _badCrcEvery == 0)
		{
			bytes[^1] ^= 0xFF;
		}

		return bytes;
	}
}
