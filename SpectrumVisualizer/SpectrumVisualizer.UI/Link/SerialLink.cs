using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

namespace SpectrumVisualizer.UI.Link;

/// <summary>
/// The board's serial link: one session task per Connect, decoded frames out through a bounded channel.
/// </summary>
public sealed class SerialLink : IDisposable
{
	private const int FrameCapacity = 64;

	private static readonly LinkStatus _disconnected = new(LinkState.Disconnected, null, null, 0, null);

	private readonly ISerialPortProvider _serialPortProvider;
	private readonly IScheduler _scheduler;
	private readonly ILogger<SerialLink> _logger;
	private readonly Channel<ReceivedFrame> _frames;

	// Connect, DisconnectAsync and Dispose run on the UI thread; the session task never touches these two.
	private CancellationTokenSource? _sessionCancellation;
	private Task _session = Task.CompletedTask;

	// Written by the session task only, read from any thread.
	private LinkStatus _status = _disconnected;

	// Reset by Connect while no session runs, then written by the session task only.
	private bool _frameDropLogged;

	public SerialLink(ISerialPortProvider serialPortProvider, IScheduler scheduler, ILogger<SerialLink> logger)
	{
		_serialPortProvider = serialPortProvider;
		_scheduler = scheduler;
		_logger = logger;
		var options = new BoundedChannelOptions(FrameCapacity)
		{
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleWriter = true,
			SingleReader = true
		};
		_frames = Channel.CreateBounded<ReceivedFrame>(options, OnFrameDropped);
	}

	/// <summary>Raised on a thread-pool thread of the session task, never inside Connect or DisconnectAsync.</summary>
	public event EventHandler<LinkStatus>? StatusChanged;

	public ChannelReader<ReceivedFrame> Frames => _frames.Reader;

	public LinkStatus Status => Volatile.Read(ref _status);

	/// <summary>The status at subscription, then every change, delivered on the given scheduler.</summary>
	public IObservable<LinkStatus> StatusUpdates(IScheduler scheduler)
	{
		return Observable
			.FromEventPattern<LinkStatus>(
				handler => StatusChanged += handler,
				handler => StatusChanged -= handler)
			.Select(pattern => pattern.EventArgs)
			.ObserveOn(scheduler)
			.StartWith(Status);
	}

	/// <summary>Starts a session on the port; does nothing while a session runs.</summary>
	public void Connect(string portName)
	{
		if (!_session.IsCompleted)
		{
			_logger.LogDebug("Connect to port {Port} ignored: the previous session is still stopping", portName);
			return;
		}

		_sessionCancellation?.Dispose();
		_sessionCancellation = new CancellationTokenSource();
		_frameDropLogged = false;
		var cancellationToken = _sessionCancellation.Token;
		var session = new LinkSession(portName, _serialPortProvider, _scheduler, _frames.Writer, Publish, _logger);
		_session = Task.Run(() => RunSessionAsync(session, portName, cancellationToken), CancellationToken.None);
	}

	/// <summary>
	/// Cancels the session and completes once it has published Disconnected; an idle link completes at once.
	/// </summary>
	public async Task DisconnectAsync()
	{
		var session = _session;
		if (session.IsCompleted || _sessionCancellation is null)
		{
			return;
		}

		await _sessionCancellation.CancelAsync().ConfigureAwait(false);
		await session.ConfigureAwait(false);
	}

	/// <summary>Cancels the session without waiting, for the container's synchronous dispose.</summary>
	public void Dispose()
	{
		_sessionCancellation?.Cancel();
	}

	private async Task RunSessionAsync(LinkSession session, string portName, CancellationToken cancellationToken)
	{
		var fault = await session.RunAsync(cancellationToken).ConfigureAwait(false);
		_logger.LogInformation("The link on port {Port} stopped", portName);
		try
		{
			Publish(_disconnected with { Fault = fault });
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "A link status subscriber failed on Disconnected");
		}
	}

	/// <summary>A fault rides a state change, so the comparison ignores it.</summary>
	private void Publish(LinkStatus status)
	{
		if (status with { Fault = null } == _status with { Fault = null })
		{
			return;
		}

		Volatile.Write(ref _status, status);
		StatusChanged?.Invoke(this, status);
	}

	/// <summary>
	/// A reader that falls behind drops many frames in a row, so only the first drop of a session is logged.
	/// </summary>
	private void OnFrameDropped(ReceivedFrame dropped)
	{
		if (_frameDropLogged)
		{
			return;
		}

		_frameDropLogged = true;
		_logger.LogDebug("The frame channel is full; dropping the oldest frames from seq {Seq} on", dropped.Frame.Seq);
	}
}
