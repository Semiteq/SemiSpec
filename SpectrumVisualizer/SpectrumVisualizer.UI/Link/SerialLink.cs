using System.Reactive.Concurrency;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.UI.Link;

/// <summary>The board's serial link: one session task per Connect, decoded frames out through a bounded channel.</summary>
public sealed class SerialLink : IDisposable
{
	private const int FrameCapacity = 64;

	private static readonly LinkStatus _disconnected = new(LinkState.Disconnected, null, null, 0, null);

	private readonly ISerialPortProvider _serialPortProvider;
	private readonly IScheduler _scheduler;
	private readonly ILogger<SerialLink> _logger;
	private readonly Channel<BoardFrame> _frames;

	// Connect, DisconnectAsync and Dispose run on the UI thread; the session task never touches these two.
	private CancellationTokenSource? _sessionCancellation;
	private Task _session = Task.CompletedTask;

	// Written by the session task only, read from any thread.
	private LinkStatus _status = _disconnected;
	private long _droppedFrames;

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
		_frames = Channel.CreateBounded<BoardFrame>(options, OnFrameDropped);
	}

	/// <summary>Raised on the session task's thread, only when the status differs from the last one.</summary>
	public event EventHandler<LinkStatus>? StatusChanged;

	public ChannelReader<BoardFrame> Frames => _frames.Reader;

	public LinkStatus Status => Volatile.Read(ref _status);

	public long DroppedFrames => Interlocked.Read(ref _droppedFrames);

	/// <summary>Starts a session on the port; does nothing while a session runs.</summary>
	public void Connect(string portName)
	{
		if (!_session.IsCompleted)
		{
			return;
		}

		_sessionCancellation?.Dispose();
		_sessionCancellation = new CancellationTokenSource();
		var cancellationToken = _sessionCancellation.Token;
		var session = new LinkSession(portName, _serialPortProvider, _scheduler, _frames.Writer, Publish, _logger);
		_session = Task.Run(() => RunSessionAsync(session, portName, cancellationToken), CancellationToken.None);
	}

	/// <summary>Cancels the session and completes once it has published Disconnected.</summary>
	public Task DisconnectAsync()
	{
		_sessionCancellation?.Cancel();
		return _session;
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

	private void OnFrameDropped(BoardFrame frame)
	{
		Interlocked.Increment(ref _droppedFrames);
		_logger.LogDebug("The frame channel is full; dropped the frame with seq {Seq}", frame.Seq);
	}
}
