namespace SemiSpec.Core.Protocol;

/// <summary>Counts spectra lost between frames from the seq counter; see docs/architecture/board-protocol.md.</summary>
public sealed class SequenceTracker
{
	private const int SeqRange = 256;

	private bool _hasBaseline;
	private byte _lastSeq;

	/// <summary>Returns the spectra lost before a spectrum frame, or since the last spectrum for a heartbeat.</summary>
	public int Observe(BoardFrame frame)
	{
		if (!_hasBaseline)
		{
			_hasBaseline = true;
			_lastSeq = frame.Seq;
			return 0;
		}

		var step = (byte)(frame.Seq - _lastSeq);
		_lastSeq = frame.Seq;
		if (frame.Measurement is null)
		{
			return step;
		}

		return step == 0 ? SeqRange : step - 1;
	}

	public void Reset()
	{
		_hasBaseline = false;
	}
}
