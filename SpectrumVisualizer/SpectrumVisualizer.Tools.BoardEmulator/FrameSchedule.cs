using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.Tools.BoardEmulator;

/// <summary>Decides which frame the emulated board sends at a tick, with the board's seq and heartbeat rules.</summary>
public sealed class FrameSchedule(EmulatorOptions options, TimeProvider timeProvider)
{
	private static readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(3);

	private readonly long _start = timeProvider.GetTimestamp();

	private readonly BoardState _state = StateOf(options);

	private readonly BoardState _ownFailedBit = options.Device.FailedBit;

	private readonly SpectrumMeasurement _measurement =
		SpectrumGenerator.Generate(options.Device, options.WavelengthNm, options.Delta);

	private readonly SpectrumMeasurement _darkMeasurement =
		SpectrumGenerator.Dark(options.Device, options.WavelengthNm, options.Delta);

	private readonly TimeSpan? _spectrumPeriod = options.Rate > 0 ? TimeSpan.FromSeconds(1 / options.Rate) : null;
	private readonly int _badCrcEvery = options.BadCrcEvery;
	private readonly int _darkEvery = options.DarkEvery;
	private TimeSpan _nextSpectrumAt;
	private TimeSpan _lastSentAt;
	private byte _seq;
	private long _spectraSent;
	private long _framesSent;

	/// <summary>The bytes of the frame due now, or null when nothing is due.</summary>
	public byte[]? Next()
	{
		var now = timeProvider.GetElapsedTime(_start);
		BoardFrame frame;
		if (_spectrumPeriod is { } period && now >= _nextSpectrumAt)
		{
			_seq++;
			_spectraSent++;
			var isDark = _darkEvery > 0 && _spectraSent % _darkEvery == 0;
			frame = new BoardFrame(_state & ~_ownFailedBit, _seq, isDark ? _darkMeasurement : _measurement);
			_nextSpectrumAt += period;
			if (_nextSpectrumAt <= now)
			{
				_nextSpectrumAt = now + period;
			}
		}
		else if (now - _lastSentAt >= _heartbeatInterval)
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

	private static BoardState StateOf(EmulatorOptions options)
	{
		var connected = options.BothConnected
			? BoardState.Atp2000Connected | BoardState.Atp8000Connected
			: options.Device.ConnectedBit;
		return options.Failed is { } failed ? connected | failed.FailedBit : connected;
	}
}
