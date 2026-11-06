using SemiSpec.Core.Protocol;
using SemiSpec.Core.Store;

namespace SemiSpec.Core.Trend;

/// <summary>
/// The intensity points of the current key with the run's breaks and failure marks, each in time order; not
/// thread-safe, IntensityCache calls it under its lock.
/// </summary>
internal sealed class TrendTimeline
{
	private readonly FrameContinuity _continuity = new();
	private readonly List<TrendBreak> _breaks = [];
	private readonly List<TrendFailure> _failures = [];
	private List<TrendPoint> _points = [];

	/// <summary>The receive times of the oldest and the newest point; null without points.</summary>
	public (DateTimeOffset First, DateTimeOffset Newest)? Span => _points.Count == 0
		? null
		: (DateTimeOffset.FromUnixTimeMilliseconds(_points[0].UnixMs),
			DateTimeOffset.FromUnixTimeMilliseconds(_points[^1].UnixMs));

	/// <summary>Adds the breaks and the failure mark the frame brings, under the continuity rule.</summary>
	public void Observe(ReceivedFrame received)
	{
		var continuity = _continuity.Observe(received);
		if (continuity.Loss is { } loss)
		{
			TimeOrdered.Insert(_breaks, loss, TimeOf);
		}

		if (continuity.NoSpectra is { } noSpectra)
		{
			TimeOrdered.Insert(_breaks, noSpectra, TimeOf);
		}

		if (continuity.Failure is { } failure)
		{
			TimeOrdered.Insert(_failures, failure, TimeOf);
		}
	}

	public void Insert(TrendPoint point)
	{
		TimeOrdered.Insert(_points, point, TimeOf);
	}

	public void Land(IntensityChunk chunk)
	{
		_points.InsertRange(TimeOrdered.LowerBound(_points, chunk.From.ToUnixTimeMilliseconds(), TimeOf), chunk.Points);
	}

	/// <summary>Makes the list the points of a new key; the timeline owns it from here on.</summary>
	public void ReplacePoints(List<TrendPoint> points)
	{
		_points = points;
	}

	public void DropBefore(long unixMs)
	{
		TimeOrdered.DropBefore(_points, unixMs, TimeOf);
		TimeOrdered.DropBefore(_breaks, unixMs, TimeOf);
		TimeOrdered.DropBefore(_failures, unixMs, TimeOf);
	}

	public IntensitySeries Fold(IntensityQuery query)
	{
		var fromMs = query.From.ToUnixTimeMilliseconds();
		var toMs = query.To.ToUnixTimeMilliseconds();
		var buckets = new IntensityBuckets(query);
		for (var i = TimeOrdered.LowerBound(_points, fromMs, TimeOf);
			i < _points.Count && _points[i].UnixMs < toMs;
			i++)
		{
			buckets.Add(_points[i]);
		}

		for (var i = TimeOrdered.LowerBound(_breaks, fromMs, TimeOf);
			i < _breaks.Count && _breaks[i].UnixMs < toMs;
			i++)
		{
			buckets.AddBreak(DateTimeOffset.FromUnixTimeMilliseconds(_breaks[i].UnixMs), _breaks[i].Lost);
		}

		for (var i = TimeOrdered.LowerBound(_failures, fromMs, TimeOf);
			i < _failures.Count && _failures[i].UnixMs < toMs;
			i++)
		{
			buckets.AddFailure(DateTimeOffset.FromUnixTimeMilliseconds(_failures[i].UnixMs), _failures[i].State);
		}

		return buckets.ToSeries();
	}

	private static long TimeOf(TrendPoint point)
	{
		return point.UnixMs;
	}

	private static long TimeOf(TrendBreak trendBreak)
	{
		return trendBreak.UnixMs;
	}

	private static long TimeOf(TrendFailure failure)
	{
		return failure.UnixMs;
	}
}
