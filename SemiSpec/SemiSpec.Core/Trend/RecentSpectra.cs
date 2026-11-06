using System.Runtime.InteropServices;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Store;

namespace SemiSpec.Core.Trend;

/// <summary>
/// The newest spectrum frames, oldest first by millisecond receive time; the pixel arrays are shared.
/// </summary>
internal sealed class RecentSpectra
{
	private readonly List<ReceivedFrame> _frames = [];

	/// <summary>The millisecond receive time of the oldest held frame; null when none is held.</summary>
	public long? StartMs => _frames.Count == 0 ? null : UnixMs(_frames[0]);

	/// <summary>The window mean of the measurement at the windows, NaN when its device does not cover them.</summary>
	public static double IntensityOf(QueryWindows windows, SpectrumMeasurement measurement)
	{
		if (!windows.Covers(measurement.Device))
		{
			return double.NaN;
		}

		var window = windows.For(measurement.Device);
		var pixels = measurement.Pixels.AsSpan(window.First, window.Last - window.First + 1);
		return QueryWindows.MeanOf(MemoryMarshal.AsBytes(pixels));
	}

	public void Add(ReceivedFrame spectrum)
	{
		TimeOrdered.Insert(_frames, spectrum, UnixMs);
	}

	public void DropBefore(long unixMs)
	{
		TimeOrdered.DropBefore(_frames, unixMs, UnixMs);
	}

	public List<TrendPoint> PointsAt(QueryWindows windows)
	{
		var points = new List<TrendPoint>(_frames.Count);
		foreach (var frame in _frames)
		{
			points.Add(new TrendPoint(UnixMs(frame), IntensityOf(windows, frame.Frame.Measurement!)));
		}

		return points;
	}

	/// <summary>
	/// The latest held frame at or before the moment, ties to the later frame; null before the oldest one.
	/// </summary>
	public StoredSpectrum? At(long unixMs)
	{
		if (StartMs is not { } startMs || unixMs < startMs)
		{
			return null;
		}

		var (frame, receivedAt) = _frames[TimeOrdered.UpperBound(_frames, unixMs, UnixMs) - 1];
		return new StoredSpectrum(frame.Measurement!, receivedAt, frame.Seq, frame.State);
	}

	private static long UnixMs(ReceivedFrame frame)
	{
		return frame.ReceivedAt.ToUnixTimeMilliseconds();
	}
}
