using System.Reactive.Concurrency;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.UI.Link;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Link;

/// <summary>
/// A real link over the fake port in virtual time; xUnit builds one instance per test, so nothing is shared.
/// </summary>
public abstract class SerialLinkTestBase : IAsyncLifetime
{
	protected const string PortName = "COM3";
	protected const int TestTimeoutMs = 10_000;

	private readonly Lock _gate = new();
	private readonly List<LinkStatus> _statuses = [];

	protected SerialLinkTestBase()
	{
		Link = new SerialLink(Provider, Scheduler, NullLogger<SerialLink>.Instance);
		Link.StatusChanged += (_, status) =>
		{
			lock (_gate)
			{
				_statuses.Add(status);
			}
		};
	}

	private protected TimerCountingScheduler Scheduler { get; } = new();

	private protected FakeSerialPortProvider Provider { get; } = new(PortName);

	private protected FakePortStream Port { get; } = new();

	protected SerialLink Link { get; }

	public ValueTask InitializeAsync()
	{
		return ValueTask.CompletedTask;
	}

	public async ValueTask DisposeAsync()
	{
		Port.ReleaseDispose();
		await Link.DisconnectAsync().WaitAsync(Waits.Limit);
		Link.Dispose();
		await Port.DisposeAsync();
		GC.SuppressFinalize(this);
	}

	protected static long Ticks(double seconds)
	{
		return TimeSpan.FromSeconds(seconds).Ticks;
	}

	private protected async Task ConnectAsync(FakePortStream? port = null)
	{
		port ??= Port;
		ClearStatuses();
		Provider.QueueOpen(port);
		Link.Connect(PortName);
		await port.WaitForPendingReadAsync().WaitAsync(Waits.Limit);
		Statuses().Select(status => status.State).Should().Equal(LinkState.Connecting, LinkState.Waiting);
	}

	private protected Task AtAsync(double seconds, Action<FakePortStream> push)
	{
		return AtAsync(Port, seconds, push);
	}

	/// <summary>Moves virtual time first, so the link stamps the step with the given second.</summary>
	private protected async Task AtAsync(FakePortStream port, double seconds, Action<FakePortStream> push)
	{
		Scheduler.AdvanceTo(Ticks(seconds));
		push(port);
		await port.WaitForPendingReadAsync().WaitAsync(Waits.Limit);
	}

	/// <summary>
	/// The link arms its reopen timer before it publishes PortUnavailable, so the clock may move next.
	/// </summary>
	private protected async Task LoseAsync(FakePortStream port, double seconds)
	{
		var losses = CountOf(LinkState.PortUnavailable);
		Scheduler.AdvanceTo(Ticks(seconds));
		port.PushZeroRead();
		await Waits.UntilAsync(() => CountOf(LinkState.PortUnavailable) > losses);
		await Waits.UntilAsync(() => port.IsDisposed);
	}

	private protected async Task ReopenAsync(FakePortStream port, double seconds)
	{
		Provider.QueueOpen(port);
		Scheduler.AdvanceTo(Ticks(seconds));
		await port.WaitForPendingReadAsync().WaitAsync(Waits.Limit);
	}

	/// <summary>A retry publishes nothing new, so the wait ends when the next reopen timer is armed.</summary>
	protected async Task FailReopenAsync(double seconds, IError failure)
	{
		var timers = Scheduler.TimerCount;
		Provider.QueueOpenFailure(failure);
		Scheduler.AdvanceTo(Ticks(seconds));
		await Waits.UntilAsync(() => Scheduler.TimerCount > timers);
	}

	protected Task WaitForStateAsync(LinkState state)
	{
		return Waits.UntilAsync(() => CountOf(state) > 0);
	}

	protected List<LinkStatus> Statuses()
	{
		lock (_gate)
		{
			return [.. _statuses];
		}
	}

	protected List<IError> Faults()
	{
		return [.. Statuses().Select(status => status.Fault).OfType<IError>()];
	}

	protected List<BoardFrame> TakeFrames()
	{
		var frames = new List<BoardFrame>();
		while (Link.Frames.TryRead(out var frame))
		{
			frames.Add(frame);
		}

		return frames;
	}

	private int CountOf(LinkState state)
	{
		return Statuses().Count(status => status.State == state);
	}

	private void ClearStatuses()
	{
		lock (_gate)
		{
			_statuses.Clear();
		}
	}

	/// <summary>Counts the timers the link arms, so a test can wait for a retry that changes no status.</summary>
	private protected sealed class TimerCountingScheduler : TestScheduler
	{
		// Incremented on the link's session thread, read on the test thread.
		private int _timerCount;

		public int TimerCount => Volatile.Read(ref _timerCount);

		public override IDisposable ScheduleAbsolute<TState>(
			TState state,
			long dueTime,
			Func<IScheduler, TState, IDisposable> action)
		{
			var timer = base.ScheduleAbsolute(state, dueTime, action);
			Interlocked.Increment(ref _timerCount);
			return timer;
		}
	}
}
