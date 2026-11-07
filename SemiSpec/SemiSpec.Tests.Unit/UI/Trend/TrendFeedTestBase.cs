using System.Collections.Concurrent;
using System.Reactive.Concurrency;
using System.Threading.Channels;

using Avalonia.Threading;

using FluentResults;

using Microsoft.Extensions.Logging.Testing;
using Microsoft.Reactive.Testing;

using ReactiveUI.Avalonia;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Store;
using SemiSpec.Core.Trend;
using SemiSpec.Tests.Unit.Core.Store;
using SemiSpec.UI.Link;
using SemiSpec.UI.Trend;

using static SemiSpec.Tests.Unit.Core.Store.StoreSamples;

namespace SemiSpec.Tests.Unit.UI.Trend;

/// <summary>
/// A trend feed over the chart stand: a real distributor over a channel, gated fake store readers, a test scheduler
/// for the re-read throttle and the headless dispatcher as the feed's UI scheduler, so every hop runs on the UI thread.
/// </summary>
public abstract class TrendFeedTestBase : TrendChartTestBase
{
	protected const ushort OtherNm = SampleNm + 50;
	protected const ushort ThirdNm = SampleNm + 100;

	protected static readonly TimeSpan ReReadThrottle = TimeSpan.FromMilliseconds(500);

	private readonly Channel<ReceivedFrame> _channel = Channel.CreateUnbounded<ReceivedFrame>();
	private readonly IDisposable _requestCapture;

	// Replaced on the test thread, read by the fake readers on the pool threads of the reads.
	private volatile TaskCompletionSource _pointsGate = Opened();
	private volatile TaskCompletionSource _spectrumGate = Opened();

	protected TrendFeedTestBase()
	{
		Distributor = new FrameDistributor(
			_channel.Reader, MessagePanel, ImmediateScheduler.Instance, new FakeLogger<FrameDistributor>());
		Feed = new TrendFeed(
			Distributor,
			Cache,
			Chart,
			Switching,
			ReadPointsAsync,
			ReadSpectrumAsync,
			StoreOpened.Task,
			MessagePanel,
			AvaloniaScheduler.Instance,
			Throttle,
			FeedLogger);
		_requestCapture = Cache.ReReadRequested.Subscribe(Requests.Add);
	}

	/// <summary>Every re-read request the cache raised, in order; the cache raises them on the test thread.</summary>
	protected List<ReReadRequest> Requests { get; } = [];

	/// <summary>The store's ready signal; pending unless a test completes it, so the start read waits.</summary>
	protected TaskCompletionSource<bool> StoreOpened { get; } =
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	protected TestScheduler Throttle { get; } = new();

	protected FakeLogger<TrendFeed> FeedLogger { get; } = new();

	protected FrameDistributor Distributor { get; }

	protected TrendFeed Feed { get; }

	protected ConcurrentQueue<PointsCall> PointsCalls { get; } = new();

	protected ConcurrentQueue<SpectrumCall> SpectrumCalls { get; } = new();

	/// <summary>The result of every points read; an empty chunk of the asked range by default.</summary>
	protected Func<PointsCall, Result<IntensityChunk>> PointsResult { get; set; } =
		call => Result.Ok(new IntensityChunk(call.From, []));

	/// <summary>The result of every spectrum read; no stored spectrum by default.</summary>
	protected Func<SpectrumCall, Result<StoredSpectrum?>> SpectrumResult { get; set; } =
		_ => Result.Ok<StoredSpectrum?>(null);

	protected void WriteToChannel(ReceivedFrame frame)
	{
		_channel.Writer.TryWrite(frame);
	}

	/// <summary>A spectrum at the key's wavelength and the sample delta, with the next seq.</summary>
	protected void AddAt(double second, ushort nm)
	{
		Add(StoreSamples.Spectrum(At(second), nm: nm, seq: NextSeq()));
	}

	/// <summary>
	/// Twenty minutes of spectra at the sample key, the chart refreshed, then one spectrum at the new wavelength.
	/// </summary>
	protected void ChangeKeyAfterTwentyMinutes(ushort nm)
	{
		AddSpectra(0, 1200);
		Sample();
		AddAt(1200, nm);
	}

	/// <summary>The ranges the feed reads for the newest request over the window.</summary>
	protected IReadOnlyList<TimeRange> PlanFor(DateTimeOffset from, DateTimeOffset to)
	{
		return TrendHistoryPlan.For(from, to, Cache.Extent!.Value.Newest, Requests[^1].RecentStart);
	}

	/// <summary>A stored ATP-8000 spectrum at the sample key, received at the moment.</summary>
	protected static StoredSpectrum Stored(DateTimeOffset receivedAt)
	{
		var frame = StoreSamples.Spectrum(receivedAt).Frame;
		return new StoredSpectrum(frame.Measurement!, receivedAt, frame.Seq, frame.State);
	}

	protected static IEnumerable<TimeRange> RangesOf(IEnumerable<PointsCall> calls)
	{
		return calls.Select(call => new TimeRange(call.From, call.To));
	}

	/// <summary>Ends the re-read throttle and runs the UI hop it queued, which starts the read.</summary>
	protected void PassThrottle()
	{
		Throttle.AdvanceBy(ReReadThrottle.Ticks);
		Dispatcher.UIThread.RunJobs();
	}

	/// <summary>Holds every later points read until ReleasePoints; the hold ignores the read's token.</summary>
	protected void HoldPoints()
	{
		_pointsGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	protected void ReleasePoints()
	{
		_pointsGate.TrySetResult();
	}

	/// <summary>Holds every later spectrum read until ReleaseSpectrum; the hold ignores the read's token.</summary>
	protected void HoldSpectrum()
	{
		_spectrumGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	protected void ReleaseSpectrum()
	{
		_spectrumGate.TrySetResult();
	}

	protected override void Dispose(bool disposing)
	{
		ReleasePoints();
		ReleaseSpectrum();
		_requestCapture.Dispose();
		Feed.Dispose();
		Distributor.Dispose();
		base.Dispose(disposing);
	}

	private static TaskCompletionSource Opened()
	{
		var gate = new TaskCompletionSource();
		gate.SetResult();
		return gate;
	}

	/// <summary>The fake points reader every feed of the stand uses.</summary>
	protected async Task<Result<IntensityChunk>> ReadPointsAsync(
		ushort wavelengthNm, byte delta, DateTimeOffset from, DateTimeOffset to, CancellationToken token)
	{
		var call = new PointsCall(wavelengthNm, delta, from, to, token);
		var gate = _pointsGate.Task;
		PointsCalls.Enqueue(call);
		await gate.ConfigureAwait(false);
		token.ThrowIfCancellationRequested();
		return PointsResult(call);
	}

	/// <summary>The fake spectrum reader every feed of the stand uses.</summary>
	protected async Task<Result<StoredSpectrum?>> ReadSpectrumAsync(DateTimeOffset moment, CancellationToken token)
	{
		var call = new SpectrumCall(moment, token);
		var gate = _spectrumGate.Task;
		SpectrumCalls.Enqueue(call);
		await gate.ConfigureAwait(false);
		token.ThrowIfCancellationRequested();
		return SpectrumResult(call);
	}

	protected sealed record PointsCall(
		ushort WavelengthNm, byte Delta, DateTimeOffset From, DateTimeOffset To, CancellationToken Token);

	protected sealed record SpectrumCall(DateTimeOffset Moment, CancellationToken Token);
}
