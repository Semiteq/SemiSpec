using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

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

	private static readonly long _retentionMs = (long)TimeSpan.FromDays(1).TotalMilliseconds;

	private readonly TimeProvider _timeProvider;

	// Add runs on the distributor task, LandHistory on a pool thread of the re-read, Fold and the lookups on the UI
	// thread; every field from here to _disposed is read and written under _gate only.
	private readonly Lock _gate = new();
	private readonly TrendTimeline _timeline = new();
	private readonly RecentSpectra _recent = new();
	private TrendKey? _key;
	private QueryWindows? _windows;
	private int _generation;
	private bool _disposed;

	// All emit after _gate is released, one at a time under _emitGate: _changed from the distributor task and re-read
	// pool threads, _historyLanded from re-read pool threads, _reReadRequests from the distributor task only. Dispose
	// takes _emitGate too, so no emit reaches a disposed subject; _subjectsDisposed is read and written under it.
	private readonly Lock _emitGate = new();
	private readonly Subject<Unit> _changed = new();
	private readonly Subject<Unit> _historyLanded = new();
	private readonly Subject<ReReadRequest> _reReadRequests = new();
	private bool _subjectsDisposed;

	public IntensityCache(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
		Changed = _changed.AsObservable();
		HistoryLanded = _historyLanded.AsObservable();
		ReReadRequested = _reReadRequests.AsObservable();
	}

	/// <summary>Signals after every added frame and every landed history chunk.</summary>
	public IObservable<Unit> Changed { get; }

	/// <summary>Signals after a history chunk of the current generation landed, just before its Changed.</summary>
	public IObservable<Unit> HistoryLanded { get; }

	/// <summary>Raised on every key change.</summary>
	public IObservable<ReReadRequest> ReReadRequested { get; }

	/// <summary>Null before the first spectrum.</summary>
	public TrendExtent? Extent
	{
		get
		{
			lock (_gate)
			{
				return CurrentExtent();
			}
		}
	}

	public void Add(ReceivedFrame received)
	{
		ReReadRequest? request;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_timeline.Observe(received);
			request = received.Frame.Measurement is { } measurement ? AddSpectrum(received, measurement) : null;
			DropExpired();
		}

		if (request is { } changed)
		{
			Emit(_reReadRequests, changed);
		}

		Emit(_changed, Unit.Default);
	}

	/// <summary>
	/// Inserts the points of a stored chunk read for the generation; a chunk of an older generation is ignored.
	/// </summary>
	public void LandHistory(int generation, IntensityChunk chunk)
	{
		lock (_gate)
		{
			if (_disposed || generation != _generation)
			{
				return;
			}

			_timeline.Land(chunk);
			DropExpired();
		}

		Emit(_historyLanded, Unit.Default);
		Emit(_changed, Unit.Default);
	}

	/// <summary>Folds the points, breaks and failure marks of the query's range; its key is not checked.</summary>
	public IntensitySeries Fold(IntensityQuery query)
	{
		lock (_gate)
		{
			return _timeline.Fold(query);
		}
	}

	/// <summary>
	/// Folds the query built from the current extent under one hold of the lock, so the series matches the extent;
	/// null before the first spectrum. queryFor runs under the lock and must not call back into the cache.
	/// </summary>
	public TrendFold? FoldCurrent(Func<TrendExtent, IntensityQuery> queryFor)
	{
		lock (_gate)
		{
			if (CurrentExtent() is not { } extent)
			{
				return null;
			}

			var query = queryFor(extent);
			return new TrendFold(extent, query, _timeline.Fold(query));
		}
	}

	/// <summary>The recent spectrum at or before the moment, measurement shared; null before the first.</summary>
	public StoredSpectrum? SpectrumAt(DateTimeOffset moment)
	{
		lock (_gate)
		{
			return _recent.At(moment.ToUnixTimeMilliseconds());
		}
	}

	/// <summary>Later frames and chunks are ignored, so a read that outlives the container lands nothing.</summary>
	public void Dispose()
	{
		lock (_gate)
		{
			_disposed = true;
		}

		lock (_emitGate)
		{
			_subjectsDisposed = true;
			_changed.Dispose();
			_historyLanded.Dispose();
			_reReadRequests.Dispose();
		}
	}

	/// <summary>Emits nothing once the subjects are disposed, also by a subscriber of an earlier emit.</summary>
	private void Emit<T>(Subject<T> subject, T value)
	{
		lock (_emitGate)
		{
			if (!_subjectsDisposed)
			{
				subject.OnNext(value);
			}
		}
	}

	private TrendExtent? CurrentExtent()
	{
		return _key is { } key && _timeline.Span is { } span ? new TrendExtent(key, span.First, span.Newest) : null;
	}

	private ReReadRequest? AddSpectrum(ReceivedFrame received, SpectrumMeasurement measurement)
	{
		var unixMs = received.ReceivedAt.ToUnixTimeMilliseconds();
		_recent.Add(received);
		_recent.DropBefore(unixMs - RecentSpanMs);

		var key = new TrendKey(measurement.WavelengthNm, measurement.Delta);
		if (_key is null || _windows is null)
		{
			_key = key;
			_windows = new QueryWindows(key.WavelengthNm, key.Delta);
		}
		else if (key != _key)
		{
			return ChangeKey(key);
		}

		_timeline.Insert(new TrendPoint(unixMs, RecentSpectra.IntensityOf(_windows, measurement)));
		return null;
	}

	private ReReadRequest ChangeKey(TrendKey key)
	{
		var recentStartMs = _recent.StartMs!.Value;
		_key = key;
		_windows = new QueryWindows(key.WavelengthNm, key.Delta);
		_timeline.ReplacePoints(_recent.PointsAt(_windows));
		_generation++;
		return new ReReadRequest(_generation, key, DateTimeOffset.FromUnixTimeMilliseconds(recentStartMs));
	}

	private void DropExpired()
	{
		_timeline.DropBefore(_timeProvider.GetUtcNow().ToUnixTimeMilliseconds() - _retentionMs);
	}
}
