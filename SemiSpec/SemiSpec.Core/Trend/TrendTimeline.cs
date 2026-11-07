using SemiSpec.Core.Protocol;
using SemiSpec.Core.Store;

namespace SemiSpec.Core.Trend;

/// <summary>
/// The intensity points of the current key with the run's breaks and failure marks, each in time order; not
/// thread-safe, IntensityCache calls it under its lock.
/// </summary>
internal sealed class TrendTimeline
{
	private static readonly long _silenceLimitMs = (long)LinkTiming.SilenceLimit.TotalMilliseconds;

	private readonly FrameContinuity _continuity = new();
	private readonly List<TrendBreak> _breaks = [];
	private readonly List<TrendFailure> _failures = [];
	private List<TrendPoint> _points = [];

	/// <summary>The receive times of the oldest and the newest point; null without points.</summary>
	public (DateTimeOffset First, DateTimeOffset Newest)? Span => _points.Count == 0
		? null
		: (DateTimeOffset.FromUnixTimeMilliseconds(_points[0].UnixMs),
			DateTimeOffset.FromUnixTimeMilliseconds(_points[^1].UnixMs));

	/// <summary>Adds the Loss break and the failure mark the frame brings, under the continuity rule.</summary>
	public void Observe(ReceivedFrame received)
	{
		var continuity = _continuity.Observe(received);
		if (continuity.Loss is { } loss)
		{
			TimeOrdered.Insert(_breaks, loss, TimeOf);
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

	/// <summary>
	/// Folds the range; a point more than the silence limit after the point before it, in range or not, adds a
	/// NoSpectra break at its time.
	/// </summary>
	public IntensitySeries Fold(IntensityQuery query)
	{
		var fromMs = query.From.ToUnixTimeMilliseconds();
		var toMs = query.To.ToUnixTimeMilliseconds();
		var buckets = new IntensityBuckets(query);
		var first = TimeOrdered.LowerBound(_points, fromMs, TimeOf);
		long? previousMs = first > 0 ? _points[first - 1].UnixMs : null;
		for (var i = first; i < _points.Count && _points[i].UnixMs < toMs; i++)
		{
			var point = _points[i];
			buckets.Add(point);
			if (point.UnixMs - previousMs > _silenceLimitMs)
			{
				buckets.AddBreak(DateTimeOffset.FromUnixTimeMilliseconds(point.UnixMs), 0);
			}

			previousMs = point.UnixMs;
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
