using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using Microsoft.Extensions.Logging;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;

namespace SpectrumVisualizer.Core.Trend;

/// <summary>
/// One intensity point per spectrum at the key of the latest spectrum frame, with the run's breaks and failure marks,
/// for the last 24 h; a key change recomputes the recent spectra and asks for the older points from the store.
/// </summary>
public sealed class IntensityCache : IDisposable
{
	/// <summary>The span of spectrum frames a key change recomputes; it covers the store writer's normal lag.</summary>
	private const long RecentSpanMs = 120_000;

	/// <summary>How long after a key change a return to the previous key still restores its points without a read.</summary>
	private const long ReturnSpanMs = 120_000;

	private static readonly long _retentionMs = (long)TimeSpan.FromDays(1).TotalMilliseconds;

	private readonly TimeProvider _timeProvider;
	private readonly ILogger<IntensityCache> _logger;

	// Add runs on the distributor task, LandHistory on a pool thread of the re-read, Fold and the lookups on the UI
	// thread; every field from here to _keyChanges is read and written under _gate only.
	private readonly Lock _gate = new();
	private readonly FrameContinuity _continuity = new();
	private readonly RecentSpectra _recent = new();
	private readonly List<TrendBreak> _breaks = [];
	private readonly List<TrendFailure> _failures = [];
	private List<TrendPoint> _points = [];
	private TrendKey? _key;
	private QueryWindows? _windows;
	private int _generation;

	/// <summary>True while _points hold every spectrum of the run at the key: no re-read of the key is pending.</summary>
	private bool _historyComplete;

	private KeyBlock? _previous;
	private int _keyChanges;

	// Both emit after _gate is released: _changed from the distributor task and a re-read pool thread, through the
	// synchronized _changedGate, _reReadRequests from the distributor task only.
	private readonly Subject<Unit> _changed = new();
	private readonly ISubject<Unit> _changedGate;
	private readonly Subject<ReReadRequest> _reReadRequests = new();

	public IntensityCache(TimeProvider timeProvider, ILogger<IntensityCache> logger)
	{
		_timeProvider = timeProvider;
		_logger = logger;
		_changedGate = Subject.Synchronize(_changed);
		Changed = _changed.AsObservable();
		ReReadRequested = _reReadRequests.AsObservable();
	}

	/// <summary>Signals after every added frame and every landed history chunk.</summary>
	public IObservable<Unit> Changed { get; }

	/// <summary>Raised on a key change; a request with ReadsHistory false only cancels the pending read.</summary>
	public IObservable<ReReadRequest> ReReadRequested { get; }

	/// <summary>The receive time of the oldest point; null before the first spectrum.</summary>
	public DateTimeOffset? FirstPointAt
	{
		get
		{
			lock (_gate)
			{
				return _points.Count == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(_points[0].UnixMs);
			}
		}
	}

	/// <summary>The receive time of the newest point; null before the first spectrum.</summary>
	public DateTimeOffset? NewestAt
	{
		get
		{
			lock (_gate)
			{
				return _points.Count == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(_points[^1].UnixMs);
			}
		}
	}

	public void Add(ReceivedFrame received)
	{
		ReReadRequest? request;
		int keyChanges;
		lock (_gate)
		{
			AddContinuity(_continuity.Observe(received));
			request = received.Frame.Measurement is { } measurement ? AddSpectrum(received, measurement) : null;
			DropExpired();
			keyChanges = _keyChanges;
		}

		if (request is { } changed)
		{
			_logger.LogDebug(
				"The trend key changed to {WavelengthNm} nm, delta {Delta}, key change {KeyChangeCount}",
				changed.Key.WavelengthNm,
				changed.Key.Delta,
				keyChanges);
			_reReadRequests.OnNext(changed);
		}

		_changedGate.OnNext(Unit.Default);
	}

	/// <summary>Inserts the points of a stored chunk read for the generation; a chunk of an older generation is ignored.</summary>
	public void LandHistory(int generation, IntensityChunk chunk)
	{
		lock (_gate)
		{
			if (generation != _generation)
			{
				return;
			}

			_points.InsertRange(TimeOrdered.LowerBound(_points, chunk.From.ToUnixTimeMilliseconds(), TimeOf), chunk.Points);
			_previous = null;
			DropExpired();
		}

		_changedGate.OnNext(Unit.Default);
	}

	/// <summary>Folds the points, breaks and failure marks of the query's range; the query's key is not checked.</summary>
	public IntensitySeries Fold(IntensityQuery query)
	{
		var fromMs = query.From.ToUnixTimeMilliseconds();
		var toMs = query.To.ToUnixTimeMilliseconds();
		var buckets = new IntensityBuckets(query);
		lock (_gate)
		{
			for (var i = TimeOrdered.LowerBound(_points, fromMs, TimeOf); i < _points.Count && _points[i].UnixMs < toMs; i++)
			{
				var (unixMs, value) = _points[i];
				var moment = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
				if (double.IsNaN(value))
				{
					buckets.AddUncovered(moment);
				}
				else
				{
					buckets.Add(moment, value);
				}
			}

			for (var i = TimeOrdered.LowerBound(_breaks, fromMs, TimeOf); i < _breaks.Count && _breaks[i].UnixMs < toMs; i++)
			{
				buckets.AddBreak(DateTimeOffset.FromUnixTimeMilliseconds(_breaks[i].UnixMs), _breaks[i].Lost);
			}

			for (var i = TimeOrdered.LowerBound(_failures, fromMs, TimeOf);
				i < _failures.Count && _failures[i].UnixMs < toMs;
				i++)
			{
				buckets.AddFailure(DateTimeOffset.FromUnixTimeMilliseconds(_failures[i].UnixMs), _failures[i].State);
			}
		}

		return buckets.ToSeries();
	}

	/// <summary>The recent spectrum at or before the moment as a new measurement; null before the recent spectra.</summary>
	public StoredSpectrum? SpectrumAt(DateTimeOffset moment)
	{
		lock (_gate)
		{
			return _recent.At(moment.ToUnixTimeMilliseconds());
		}
	}

	public void Dispose()
	{
		_changed.Dispose();
		_reReadRequests.Dispose();
	}

	private void AddContinuity(Continuity continuity)
	{
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

	private ReReadRequest? AddSpectrum(ReceivedFrame received, SpectrumMeasurement measurement)
	{
		var unixMs = received.ReceivedAt.ToUnixTimeMilliseconds();
		_recent.Add(received);
		DropOldRecentSpectra(unixMs);

		var key = new TrendKey(measurement.WavelengthNm, measurement.Delta);
		if (_key is null || _windows is null)
		{
			_key = key;
			_windows = new QueryWindows(key.WavelengthNm, key.Delta);
			_historyComplete = true;
		}
		else if (key != _key)
		{
			return _previous?.Key == key ? ReturnToPrevious() : ChangeKey(key);
		}

		TimeOrdered.Insert(_points, new TrendPoint(unixMs, RecentSpectra.IntensityOf(_windows, measurement)), TimeOf);
		return null;
	}

	/// <summary>Keeps RecentSpanMs of spectra, and every spectrum since the last change while a return can restore it.</summary>
	private void DropOldRecentSpectra(long newestMs)
	{
		var cutoffMs = newestMs - RecentSpanMs;
		if (_previous is { } previous)
		{
			if (newestMs - previous.EndMs > RecentSpanMs + ReturnSpanMs)
			{
				_previous = null;
			}
			else
			{
				cutoffMs = Math.Min(cutoffMs, previous.EndMs);
			}
		}

		_recent.DropBefore(cutoffMs);
	}

	private ReReadRequest ChangeKey(TrendKey key)
	{
		var recentStartMs = _recent.StartMs!.Value;
		var older = TimeOrdered.LowerBound(_points, recentStartMs, TimeOf);
		_previous = _historyComplete ? new KeyBlock(_key!.Value, _windows!, _points.GetRange(0, older), recentStartMs) : null;

		_key = key;
		_windows = new QueryWindows(key.WavelengthNm, key.Delta);
		_historyComplete = false;
		_points = _recent.PointsAt(_windows);
		return NextRequest(key, recentStartMs, readsHistory: true);
	}

	/// <summary>Restores the previous key's older points; the recent spectra still reach back to their end.</summary>
	private ReReadRequest ReturnToPrevious()
	{
		var previous = _previous!;
		_previous = null;

		_key = previous.Key;
		_windows = previous.Windows;
		_historyComplete = true;
		_points = previous.Points;
		_points.AddRange(_recent.PointsAt(_windows));
		return NextRequest(previous.Key, previous.EndMs, readsHistory: false);
	}

	private ReReadRequest NextRequest(TrendKey key, long recentStartMs, bool readsHistory)
	{
		_generation++;
		_keyChanges++;
		return new ReReadRequest(
			_generation, key, DateTimeOffset.FromUnixTimeMilliseconds(recentStartMs), readsHistory);
	}

	private void DropExpired()
	{
		var cutoffMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds() - _retentionMs;
		TimeOrdered.DropBefore(_points, cutoffMs, TimeOf);
		TimeOrdered.DropBefore(_breaks, cutoffMs, TimeOf);
		TimeOrdered.DropBefore(_failures, cutoffMs, TimeOf);
		if (_previous is { } previous)
		{
			TimeOrdered.DropBefore(previous.Points, cutoffMs, TimeOf);
		}
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

	/// <summary>The previous key's points older than EndMs, the recent spectra start at that change.</summary>
	private sealed record KeyBlock(TrendKey Key, QueryWindows Windows, List<TrendPoint> Points, long EndMs);
}

/// <summary>The wavelength and delta the trend's intensity is the window mean at.</summary>
public readonly record struct TrendKey(ushort WavelengthNm, byte Delta);

/// <summary>
/// A key change: the store points before RecentStart are to be read at Key and landed under Generation; with
/// ReadsHistory false the cache already holds them and the pending read is only cancelled.
/// </summary>
public sealed record ReReadRequest(int Generation, TrendKey Key, DateTimeOffset RecentStart, bool ReadsHistory);
