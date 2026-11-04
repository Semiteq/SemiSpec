using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Spectrum;

namespace SpectrumVisualizer.UI.Trend;

/// <summary>
/// Fills the intensity cache from the distributor, re-reads the stored history after a key change and reads the
/// spectrum of a clicked moment; a newer request cancels the read in flight of its pipeline, and Back to live cancels
/// the moment read in flight.
/// </summary>
public sealed class TrendFeed(
	FrameDistributor distributor,
	IntensityCache cache,
	TrendChartViewModel chart,
	SwitchingSpectrumSource source,
	Func<ushort, byte, DateTimeOffset, DateTimeOffset, CancellationToken, Task<Result<IntensityChunk>>> readPoints,
	Func<DateTimeOffset, CancellationToken, Task<Result<StoredSpectrum?>>> readSpectrum,
	MessagePanelViewModel messagePanel,
	IScheduler uiScheduler,
	IScheduler throttleScheduler,
	ILogger<TrendFeed> logger) : IDisposable
{
	private static readonly TimeSpan _reReadThrottle = TimeSpan.FromMilliseconds(500);

	// A null request cancels the moment read in flight and reads nothing.
	private readonly Subject<DateTimeOffset?> _momentRequests = new();
	private readonly CompositeDisposable _subscriptions = [];

	// Reads are added on the UI thread and removed on the pool thread that ends them; StopAsync awaits the rest.
	private readonly Lock _readsGate = new();
	private readonly HashSet<Task> _reads = [];

	// Start, StopAsync, Dispose and ReadMoment run on the UI thread only; the reads on the pool also read _stopped, so
	// a read that ends after the stop reports nothing.
	private bool _started;
	private volatile bool _stopped;

	/// <summary>Subscribes the distributor and both read pipelines once; does nothing after a start or a stop.</summary>
	public void Start()
	{
		if (_started || _stopped)
		{
			return;
		}

		_started = true;
		_subscriptions.Add(cache.ReReadRequested.Subscribe(LogKeyChange, ReportPipelineFailure));
		_subscriptions.Add(SubscribeToHistoryReads());
		_subscriptions.Add(SubscribeToMomentReads());
		_subscriptions.Add(source
			.WhenAnyValue(switching => switching.ShownMoment)
			.Where(shown => shown is null)
			.Subscribe(_ => _momentRequests.OnNext(null), ReportPipelineFailure));
		_subscriptions.Add(distributor.Frames.Subscribe(cache.Add, ReportPipelineFailure));
		logger.LogDebug("The trend feed started");
	}

	/// <summary>Reads the spectrum at or before the moment and shows it, in place of the moment read in flight.</summary>
	public void ReadMoment(DateTimeOffset moment)
	{
		if (_stopped)
		{
			return;
		}

		_momentRequests.OnNext(moment);
	}

	/// <summary>Unsubscribes, cancels the reads and completes once every read has ended.</summary>
	public async Task StopAsync()
	{
		_stopped = true;
		_subscriptions.Dispose();
		Task[] reads;
		lock (_readsGate)
		{
			reads = [.. _reads];
		}

		await Task.WhenAll(reads).ConfigureAwait(false);
		logger.LogDebug("The trend feed stopped");
	}

	/// <summary>Cancels the reads without waiting, for the container's synchronous dispose.</summary>
	public void Dispose()
	{
		_stopped = true;
		_subscriptions.Dispose();
		_momentRequests.Dispose();
	}

	private void LogKeyChange(ReReadRequest request)
	{
		logger.LogDebug(
			"The trend key changed to {WavelengthNm} nm, delta {Delta}, key change {KeyChangeCount}",
			request.Key.WavelengthNm,
			request.Key.Delta,
			request.Generation);
	}

	private IDisposable SubscribeToHistoryReads()
	{
		return cache.ReReadRequested
			.Throttle(_reReadThrottle, throttleScheduler)
			.ObserveOn(uiScheduler)
			.Select(request => new HistoryRead(request, new TimeRange(chart.From, chart.To)))
			.Select(ReadHistory)
			.Switch()
			.Subscribe(_ => { }, ReportPipelineFailure);
	}

	private IObservable<Unit> ReadHistory(HistoryRead read)
	{
		return read.Request.ReadsHistory
			? Observable.Create<Unit>((_, token) =>
			{
				var task = Task.Run(() => ReadHistoryAsync(read, token), CancellationToken.None);
				Track(task);
				return task;
			})
			: Observable.Empty<Unit>();
	}

	/// <summary>Never throws: the first failed range reports once and ends the read, a cancel ends it quietly.</summary>
	private async Task ReadHistoryAsync(HistoryRead read, CancellationToken token)
	{
		var (request, window) = read;
		try
		{
			var newest = cache.Extent?.Newest ?? request.RecentStart;
			foreach (var range in TrendHistoryPlan.For(window.From, window.To, newest, request.RecentStart))
			{
				var chunk = await readPoints(request.Key.WavelengthNm, request.Key.Delta, range.From, range.To, token)
					.ConfigureAwait(false);
				if (chunk.IsFailed)
				{
					ReportHistoryFailure(new TrendHistoryError().CausedBy(chunk.Errors));
					return;
				}

				cache.LandHistory(request.Generation, chunk.Value);
			}

			cache.CompleteHistory(request.Generation);
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			// A newer key change or the stop cancelled this read.
		}
		catch (Exception exception)
		{
			ReportHistoryFailure(new TrendHistoryError().CausedBy(exception));
		}
	}

	private void ReportHistoryFailure(IError error)
	{
		if (_stopped)
		{
			return;
		}

		messagePanel.TryReportFailure(error, logger, uiScheduler);
	}

	private IDisposable SubscribeToMomentReads()
	{
		return _momentRequests
			.Select(ReadMomentFrom)
			.Switch()
			.ObserveOn(uiScheduler)
			.Subscribe(ShowRead, ReportPipelineFailure);
	}

	/// <summary>A moment inside the cache's recent spectra needs no store read; Back to live reads nothing.</summary>
	private IObservable<MomentRead> ReadMomentFrom(DateTimeOffset? requested)
	{
		if (requested is not { } moment)
		{
			return Observable.Empty<MomentRead>();
		}

		return cache.SpectrumAt(moment) is { } recent
			? Observable.Return(new MomentRead(moment, Result.Ok<StoredSpectrum?>(recent)))
			: Observable.Create<MomentRead>(async (observer, token) =>
			{
				var read = ReadStoredMomentAsync(moment, token);
				Track(read);
				var result = await read.ConfigureAwait(false);
				if (!token.IsCancellationRequested)
				{
					observer.OnNext(new MomentRead(moment, result));
				}
			});
	}

	/// <summary>Never throws: a thrown read comes back as a failed result, a cancelled one as no spectrum.</summary>
	private async Task<Result<StoredSpectrum?>> ReadStoredMomentAsync(DateTimeOffset moment, CancellationToken token)
	{
		try
		{
			return await readSpectrum(moment, token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			return Result.Ok<StoredSpectrum?>(null);
		}
		catch (Exception exception)
		{
			return Result.Fail<StoredSpectrum?>(new ExceptionalError(exception.Message, exception));
		}
	}

	private void ShowRead(MomentRead read)
	{
		if (read.Result.IsFailed)
		{
			messagePanel.TryReportFailure(new TrendMomentError().CausedBy(read.Result.Errors), logger, uiScheduler);
			return;
		}

		if (read.Result.Value is not { } spectrum)
		{
			logger.LogDebug("No stored spectrum at or before {Moment}", read.Moment);
			return;
		}

		try
		{
			source.ShowMoment(spectrum.Measurement, spectrum.ReceivedAt);
		}
		catch (Exception exception)
		{
			ReportPipelineFailure(exception);
		}
	}

	/// <summary>Keeps the read for StopAsync until it ends; Untrack cannot throw, so its continuation is not observed.</summary>
	private void Track(Task read)
	{
		lock (_readsGate)
		{
			_reads.Add(read);
		}

		_ = read.ContinueWith(
			Untrack, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
	}

	private void Untrack(Task read)
	{
		lock (_readsGate)
		{
			_reads.Remove(read);
		}
	}

	private void ReportPipelineFailure(Exception exception)
	{
		messagePanel.TryReportFailure(new ExceptionalError(exception.Message, exception), logger, uiScheduler);
	}

	/// <summary>Window is the visible window at the UI dispatch; the read on the pool sees only this value.</summary>
	private sealed record HistoryRead(ReReadRequest Request, TimeRange Window);

	private sealed record MomentRead(DateTimeOffset Moment, Result<StoredSpectrum?> Result);
}
