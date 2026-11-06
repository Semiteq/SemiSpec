using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using SemiSpec.Core.Protocol;
using SemiSpec.UI.Link;
using SemiSpec.UI.Messages;

namespace SemiSpec.UI.Store;

/// <summary>The spectrum store file of this running instance.</summary>
public sealed record StoreFile(string Path);

/// <summary>
/// Stores every frame the distributor hands out: the subscriber only enqueues, one <see cref="StoreWriter"/> task
/// drains the queue into the store file, and every frame lost on the way is counted.
/// </summary>
public sealed class SpectrumStore : IDisposable
{
	private const int QueueCapacity = 1024;

	private readonly FrameDistributor _distributor;
	private readonly StoreFile _storeFile;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly IScheduler _uiScheduler;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<SpectrumStore> _logger;
	private readonly Channel<ReceivedFrame> _queue = Channel.CreateBounded<ReceivedFrame>(
		new BoundedChannelOptions(QueueCapacity)
		{
			FullMode = BoundedChannelFullMode.Wait,
			SingleWriter = true,
			SingleReader = true
		});

	// Counted on the distributor task and the writer task; add and OnNext share the gate, so counts arrive in order.
	private readonly Lock _droppedGate = new();
	private readonly Subject<long> _droppedFramesChanged = new();
	private long _droppedFrames;

	// Start, StopAsync and Dispose run on the UI thread.
	private IDisposable? _subscription;
	private StoreWriter? _storeWriter;
	private Task _writer = Task.CompletedTask;
	private bool _started;
	private bool _stopped;

	// Created by Start on the UI thread; cancelled by Dispose and the drain token, disposed by the writer task when it
	// ends. Each touch holds the gate, so no cancel meets a disposed source.
	private readonly Lock _cancellationGate = new();
	private CancellationTokenSource? _cancellation;
	private CancellationTokenSource? _drainCancellation;

	public SpectrumStore(
		FrameDistributor distributor,
		StoreFile storeFile,
		MessagePanelViewModel messagePanel,
		IScheduler uiScheduler,
		TimeProvider timeProvider,
		ILogger<SpectrumStore> logger)
	{
		_distributor = distributor;
		_storeFile = storeFile;
		_messagePanel = messagePanel;
		_uiScheduler = uiScheduler;
		_timeProvider = timeProvider;
		_logger = logger;
		DroppedFramesChanged = _droppedFramesChanged.AsObservable();
	}

	/// <summary>
	/// Frames lost to a full queue, a failed batch, a failed store or a stop past its budget, since the start.
	/// </summary>
	public long DroppedFrames => Interlocked.Read(ref _droppedFrames);

	/// <summary>The new DroppedFrames after each change, on the thread that counted.</summary>
	public IObservable<long> DroppedFramesChanged { get; }

	/// <summary>
	/// Subscribes to the distributor and starts the writer task once; does nothing after a start or a stop.
	/// </summary>
	public void Start()
	{
		if (_started || _stopped)
		{
			return;
		}

		_started = true;
		_cancellation = new CancellationTokenSource();
		_drainCancellation = new CancellationTokenSource();
		var cancellationToken = _cancellation.Token;
		var drainToken = _drainCancellation.Token;
		var writer = new StoreWriter(
			_storeFile, _queue.Reader, CountDropped, _messagePanel, _uiScheduler, _timeProvider, _logger);
		_storeWriter = writer;
		_subscription = _distributor.Frames.Subscribe(Enqueue);
		_writer = Task.Run(() => RunWriterAsync(writer, cancellationToken, drainToken), CancellationToken.None);
	}

	/// <summary>
	/// Stops taking frames and completes once the writer has written the queue and closed the file; from the stop on no
	/// retention pass starts, a held lock fails a batch after 1 s, and when drainToken fires the frames still queued
	/// count as dropped. Completes at once when nothing started; a later call completes with the writer of the first.
	/// </summary>
	public async Task StopAsync(CancellationToken drainToken = default)
	{
		_stopped = true;
		_storeWriter?.Stop();
		if (_subscription is not { } subscription)
		{
			await _writer.ConfigureAwait(false);
			return;
		}

		_subscription = null;
		subscription.Dispose();
		_queue.Writer.TryComplete();
		await using (drainToken.Register(CancelDrain))
		{
			await _writer.ConfigureAwait(false);
		}
	}

	private void CancelDrain()
	{
		lock (_cancellationGate)
		{
			_drainCancellation?.Cancel();
		}
	}

	/// <summary>
	/// Cancels the writer without waiting, for the container's synchronous dispose; the writer closes the file.
	/// </summary>
	public void Dispose()
	{
		_stopped = true;
		_storeWriter?.Stop();
		_subscription?.Dispose();
		_subscription = null;
		lock (_cancellationGate)
		{
			_cancellation?.Cancel();
		}
	}

	private void Enqueue(ReceivedFrame received)
	{
		if (!_queue.Writer.TryWrite(received))
		{
			CountDropped(1);
		}
	}

	private void CountDropped(long count)
	{
		if (count == 0)
		{
			return;
		}

		lock (_droppedGate)
		{
			_droppedFramesChanged.OnNext(Interlocked.Add(ref _droppedFrames, count));
		}
	}

	private async Task RunWriterAsync(
		StoreWriter writer, CancellationToken cancellationToken, CancellationToken drainToken)
	{
		try
		{
			await writer.RunAsync(cancellationToken, drainToken).ConfigureAwait(false);
		}
		finally
		{
			ReleaseCancellation();
			_logger.LogDebug(
				"The spectrum store stopped: {Stored} frames stored, {Dropped} frames dropped, checkpoint {Checkpoint}",
				writer.StoredFrames,
				DroppedFrames,
				writer.CheckpointOutcome);
		}
	}

	private void ReleaseCancellation()
	{
		lock (_cancellationGate)
		{
			_cancellation?.Dispose();
			_cancellation = null;
			_drainCancellation?.Dispose();
			_drainCancellation = null;
		}
	}
}
