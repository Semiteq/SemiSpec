using System.Collections.Immutable;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Threading.Channels;

using FluentResults;

using Microsoft.Extensions.Logging;

using SpectrumVisualizer.UI.Messages;

namespace SpectrumVisualizer.UI.Link;

/// <summary>
/// The one reader of the link's frame channel: keeps the latest spectrum frame and hands every frame to each
/// subscriber on its read task.
/// </summary>
public sealed class FrameDistributor : IDisposable
{
	private readonly ChannelReader<ReceivedFrame> _frames;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly IScheduler _uiScheduler;
	private readonly ILogger<FrameDistributor> _logger;
	private readonly Lock _subscribersGate = new();

	// Replaced under _subscribersGate by subscribers on any thread; the read task takes one snapshot per frame.
	private ImmutableArray<IObserver<ReceivedFrame>> _subscribers = [];

	// Written by the read task only, read from any thread.
	private ReceivedFrame? _latestSpectrum;

	// Start, StopAsync and Dispose run on the UI thread; the read task never touches these three.
	private CancellationTokenSource? _cancellation;
	private Task _readLoop = Task.CompletedTask;
	private bool _stopped;

	public FrameDistributor(
		ChannelReader<ReceivedFrame> frames,
		MessagePanelViewModel messagePanel,
		IScheduler uiScheduler,
		ILogger<FrameDistributor> logger)
	{
		_frames = frames;
		_messagePanel = messagePanel;
		_uiScheduler = uiScheduler;
		_logger = logger;
		Frames = new FrameStream(this);
	}

	/// <summary>The newest frame that carried a measurement; a heartbeat never replaces it.</summary>
	public ReceivedFrame? LatestSpectrum => Volatile.Read(ref _latestSpectrum);

	/// <summary>
	/// Every frame read after the subscription, delivered on the read task; a subscriber that throws stays
	/// subscribed and takes the frame from no other subscriber.
	/// </summary>
	public IObservable<ReceivedFrame> Frames { get; }

	/// <summary>Starts the read task once; does nothing after a start or a stop.</summary>
	public void Start()
	{
		if (_cancellation is not null || _stopped)
		{
			return;
		}

		_cancellation = new CancellationTokenSource();
		var cancellationToken = _cancellation.Token;
		_readLoop = Task.Run(() => ReadAsync(cancellationToken), CancellationToken.None);
	}

	/// <summary>Cancels the read task and completes once it has ended; completes at once when none runs.</summary>
	public async Task StopAsync()
	{
		_stopped = true;
		if (_cancellation is not { } cancellation)
		{
			return;
		}

		_cancellation = null;
		await cancellation.CancelAsync().ConfigureAwait(false);
		await _readLoop.ConfigureAwait(false);
		cancellation.Dispose();
	}

	/// <summary>Cancels the read task without waiting, for the container's synchronous dispose.</summary>
	public void Dispose()
	{
		_stopped = true;
		_cancellation?.Cancel();
	}

	private async Task ReadAsync(CancellationToken cancellationToken)
	{
		_logger.LogDebug("The frame distributor started");
		try
		{
			await foreach (var received in _frames.ReadAllAsync(cancellationToken).ConfigureAwait(false))
			{
				if (received.Frame.Measurement is not null)
				{
					Volatile.Write(ref _latestSpectrum, received);
				}

				Distribute(received);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Stop and Dispose end the loop this way; the channel itself never completes.
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "The frame distributor stopped reading on a failure");
		}

		_logger.LogDebug("The frame distributor stopped");
	}

	private void Distribute(ReceivedFrame received)
	{
		ImmutableArray<IObserver<ReceivedFrame>> subscribers;
		lock (_subscribersGate)
		{
			subscribers = _subscribers;
		}

		foreach (var subscriber in subscribers)
		{
			try
			{
				subscriber.OnNext(received);
			}
			catch (Exception exception)
			{
				_messagePanel.TryReportFailure(
					new ExceptionalError(exception.Message, exception),
					_logger,
					_uiScheduler);
			}
		}
	}

	/// <summary>Not Observable.Create: its wrapper unsubscribes an observer whose OnNext throws.</summary>
	private sealed class FrameStream(FrameDistributor distributor) : IObservable<ReceivedFrame>
	{
		public IDisposable Subscribe(IObserver<ReceivedFrame> observer)
		{
			lock (distributor._subscribersGate)
			{
				distributor._subscribers = distributor._subscribers.Add(observer);
			}

			return Disposable.Create(() =>
			{
				lock (distributor._subscribersGate)
				{
					distributor._subscribers = distributor._subscribers.Remove(observer);
				}
			});
		}
	}
}
