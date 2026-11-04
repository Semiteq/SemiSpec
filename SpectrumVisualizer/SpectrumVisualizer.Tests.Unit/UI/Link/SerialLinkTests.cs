using System.Reactive.Concurrency;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.UI.Link;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SpectrumVisualizer.Tests.Unit.UI.Link;

[Trait("Component", "UI")]
[Trait("Area", "Link")]
[Trait("Category", "Unit")]
public sealed class SerialLinkTests : IAsyncLifetime
{
	private const string PortName = "COM3";
	private const int TestTimeoutMs = 10_000;

	private static readonly TimeSpan _waitLimit = TimeSpan.FromSeconds(5);

	private readonly TimerCountingScheduler _scheduler = new();
	private readonly FakeSerialPortProvider _provider = new(PortName);
	private readonly FakePortStream _port = new();
	private readonly SerialLink _link;
	private readonly Lock _gate = new();
	private readonly List<LinkStatus> _statuses = [];

	public SerialLinkTests()
	{
		_link = new SerialLink(_provider, _scheduler, NullLogger<SerialLink>.Instance);
		_link.StatusChanged += (_, status) =>
		{
			lock (_gate)
			{
				_statuses.Add(status);
			}
		};
	}

	public static TheoryData<Exception> BusyPortFailures =>
	[
		new UnauthorizedAccessException("Access to the port is denied."),
		new IOException($"Sharing violation: {PortName}", 32)
	];

	public ValueTask InitializeAsync()
	{
		return ValueTask.CompletedTask;
	}

	public async ValueTask DisposeAsync()
	{
		await _link.DisconnectAsync().WaitAsync(_waitLimit);
		_link.Dispose();
		await _port.DisposeAsync();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task FramesInRandomChunksReachTheChannel()
	{
		BoardFrame[] frames = [Spectrum(Spectrometer.Atp2000, 1), Spectrum(Spectrometer.Atp8000, 2), Heartbeat(2)];
		var bytes = Concat(frames.Select(FrameEncoder.Encode));
		await ConnectAsync();

		var random = new Random(20261104);
		for (var offset = 0; offset < bytes.Length;)
		{
			var size = Math.Min(random.Next(1, 600), bytes.Length - offset);
			_port.PushBytes(bytes[offset..(offset + size)]);
			offset += size;
		}

		await ReadPendingAsync();

		TakeFrames().Should().BeEquivalentTo(frames, options => options.WithStrictOrdering());
		_link.Status.Should().Be(new LinkStatus(LinkState.Receiving, PortName, Heartbeat(2).State, 0, null));
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task SilenceForTenSecondsReportsBoardNotResponding()
	{
		await ConnectAsync();

		await AtAsync(9.9, port => port.PushTimeout());
		_link.Status.State.Should().Be(LinkState.Waiting);
		await AtAsync(10, port => port.PushTimeout());
		await AtAsync(20, port => port.PushTimeout());

		var notResponding = Statuses().Where(status => status.State == LinkState.NotResponding).ToList();
		notResponding.Should().ContainSingle();
		notResponding[0].Fault.Should().BeOfType<BoardNotRespondingError>()
			.Which.Message.Should().Contain(PortName);
		_link.Status.State.Should().Be(LinkState.NotResponding);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task SilenceAfterAFrameNeverDrainsThePartialFrameInFlight()
	{
		var next = FrameEncoder.Encode(Spectrum(Spectrometer.Atp8000, 2));
		await ConnectAsync();

		await AtAsync(1, port => port.PushBytes(Concat(FrameEncoder.Encode(Heartbeat(1)), next[..500])));
		await AtAsync(11, port => port.PushTimeout());
		await AtAsync(21, port => port.PushTimeout());
		await AtAsync(31, port => port.PushTimeout());
		await AtAsync(32, port => port.PushBytes(next[500..]));

		TakeFrames().Should().Equal(Heartbeat(1), Spectrum(Spectrometer.Atp8000, 2));
		Statuses().Where(status => status.Fault is not null).Should().ContainSingle()
			.Which.Fault.Should().BeOfType<BoardNotRespondingError>();
		_link.Status.State.Should().Be(LinkState.Receiving);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task BytesWithoutAValidFrameForTenSecondsReportOtherDevice()
	{
		await ConnectAsync();

		await AtAsync(1, port => port.PushBytes(CorruptedSpectrum(1)));
		await AtAsync(4, port => port.PushBytes(CorruptedSpectrum(2)));
		await AtAsync(7, port => port.PushBytes(CorruptedSpectrum(3)));
		_link.Status.State.Should().Be(LinkState.Waiting);
		await AtAsync(10, port => port.PushTimeout());

		_link.Status.State.Should().Be(LinkState.OtherDevice);
		Statuses().Should().NotContain(status => status.State == LinkState.NotResponding);
		Statuses().Single(status => status.State == LinkState.OtherDevice).Fault
			.Should().BeOfType<UnrecognisedDataError>();
		TakeFrames().Should().BeEmpty();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AStalledCandidateIsDrainedAndTheFramesBehindItArrive()
	{
		byte[] falseCandidate = [0xAA, 0x55, 0x12, 0x10, 0x00, 0x00];
		await ConnectAsync();

		await AtAsync(0, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));
		await AtAsync(1, port => port.PushBytes(falseCandidate));
		await AtAsync(3, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(2))));
		await AtAsync(6, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(3))));
		await AtAsync(9, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(4))));
		TakeFrames().Should().Equal(Heartbeat(1));

		await AtAsync(10, port => port.PushTimeout());
		TakeFrames().Should().Equal(Heartbeat(2), Heartbeat(3), Heartbeat(4));
		await AtAsync(19.9, port => port.PushTimeout());
		_link.Status.State.Should().Be(LinkState.Receiving);
		Statuses().Should().NotContain(status =>
			status.State == LinkState.NotResponding || status.State == LinkState.OtherDevice);

		await AtAsync(20, port => port.PushTimeout());
		_link.Status.State.Should().Be(LinkState.NotResponding, "the released frames carry the drain time, 10 s");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task ADrainDiscardsThePartialFrameInFlight()
	{
		var spectrum = FrameEncoder.Encode(Spectrum(Spectrometer.Atp8000, 2));
		var half = spectrum.Length / 2;
		await ConnectAsync();

		await AtAsync(0, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));
		await AtAsync(1, port => port.PushBytes(spectrum[..half]));
		await AtAsync(10, port => port.PushTimeout());
		await AtAsync(11, port => port.PushBytes(spectrum[half..]));
		await AtAsync(12, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(2))));

		TakeFrames().Should().Equal(Heartbeat(1), Heartbeat(2));
		_link.Status.State.Should().Be(LinkState.Receiving);
		_link.Status.LostSpectra.Should().Be(1);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AFullChannelDropsTheOldestFrame()
	{
		var frames = Enumerable.Range(1, 65).Select(seq => Spectrum(Spectrometer.Atp8000, (byte)seq)).ToList();
		await ConnectAsync();

		await AtAsync(0, port => port.PushBytes(Concat(frames.Select(FrameEncoder.Encode))));

		TakeFrames().Should().Equal(frames[1..]);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AnUnexpectedExceptionEndsTheSessionWithAFault()
	{
		var thrown = new FormatException("unexpected");
		await ConnectAsync();

		_port.PushException(thrown);
		await WaitForStateAsync(LinkState.Disconnected);

		_link.Status.State.Should().Be(LinkState.Disconnected);
		_link.Status.Fault.Should().BeAssignableTo<IExceptionalError>()
			.Which.Exception.Should().BeSameAs(thrown);
		_port.IsDisposed.Should().BeTrue();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AZeroByteReadReportsPortLost()
	{
		await ConnectAsync();

		_port.PushZeroRead();

		await ShouldReportPortLostAsync();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AClosedPortReportsPortLost()
	{
		await ConnectAsync();

		_port.PushException(new InvalidOperationException("the port is closed"));

		await ShouldReportPortLostAsync();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AnIOExceptionReportsPortLost()
	{
		await ConnectAsync();

		_port.PushException(new IOException("device monitoring thread has died"));

		await ShouldReportPortLostAsync();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task APortThatVanishesFromThePortListReportsPortLost()
	{
		await ConnectAsync();
		await AtAsync(1, port => port.PushTimeout());
		_link.Status.State.Should().Be(LinkState.Waiting);

		_provider.SetPorts("COM4");
		_port.PushTimeout();

		await ShouldReportPortLostAsync();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task APortListThatCannotBeReadKeepsThePortOpen()
	{
		await ConnectAsync();
		_provider.FailPortNames(new IOException("the port registry key is gone"));

		await AtAsync(1, port => port.PushTimeout());

		_link.Status.State.Should().Be(LinkState.Waiting);
		Statuses().Should().NotContain(status => status.State == LinkState.PortUnavailable);
		_port.IsDisposed.Should().BeFalse();
		_provider.OpenCount.Should().Be(1);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task PortUnavailableIsPublishedWhileTheLostPortIsStillClosing()
	{
		await ConnectAsync();
		_port.HoldDispose();

		_port.PushZeroRead();
		await WaitForStateAsync(LinkState.PortUnavailable);

		_port.IsDisposed.Should().BeFalse();
		_port.ReleaseDispose();
		await WaitUntilAsync(() => _port.IsDisposed);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task APortLostMidFrameDropsTheHalfFrameAndTheReopenedPortStartsClean()
	{
		var spectrum = FrameEncoder.Encode(Spectrum(Spectrometer.Atp8000, 2));
		var half = spectrum.Length / 2;
		await using var reopened = new FakePortStream();
		await ConnectAsync();
		await AtAsync(0, port => port.PushBytes(Concat(FrameEncoder.Encode(Heartbeat(1)), spectrum[..half])));

		_port.PushException(new IOException("device monitoring thread has died"));
		await WaitForStateAsync(LinkState.PortUnavailable);
		await ReopenAsync(reopened, 1);
		await AtAsync(reopened, 2, port => port.PushBytes(Concat(spectrum[half..], FrameEncoder.Encode(Heartbeat(3)))));

		TakeFrames().Should().Equal(Heartbeat(1), Heartbeat(3));
		_link.Status.State.Should().Be(LinkState.Receiving);
		_link.Status.LostSpectra.Should().Be(0, "the reopen resets the sequence baseline");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AHeartbeatOnlyStreamStaysReceiving()
	{
		await ConnectAsync();

		for (var second = 0; second <= 30; second += 3)
		{
			await AtAsync(second, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(7))));
		}

		Statuses().Select(status => status.State)
			.Should().Equal(LinkState.Connecting, LinkState.Waiting, LinkState.Receiving);
		_link.Status.State.Should().Be(LinkState.Receiving);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task TheLostTotalFollowsTheSequenceTracker()
	{
		await ConnectAsync();

		await AtAsync(0, port => port.PushBytes(FrameEncoder.Encode(Spectrum(Spectrometer.Atp8000, 1))));
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(Spectrum(Spectrometer.Atp8000, 2))));
		_link.Status.LostSpectra.Should().Be(0);
		await AtAsync(2, port => port.PushBytes(FrameEncoder.Encode(Spectrum(Spectrometer.Atp8000, 5))));

		_link.Status.LostSpectra.Should().Be(2);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AStatusEqualToTheLastOneIsNotRaisedAgain()
	{
		await ConnectAsync();

		await AtAsync(0, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));
		await AtAsync(2, port => port.PushTimeout());

		Statuses().Should().HaveCount(3);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task ALostPortReopensAfterOneSecond()
	{
		await using var reopened = new FakePortStream();
		await ConnectAsync();
		await LoseAsync(_port, 2);

		_provider.QueueOpen(reopened);
		_scheduler.AdvanceTo(Ticks(2.9));
		_provider.OpenCount.Should().Be(1);
		_scheduler.AdvanceTo(Ticks(3));
		await reopened.WaitForPendingReadAsync().WaitAsync(_waitLimit, TestContext.Current.CancellationToken);

		_provider.OpenCount.Should().Be(2);
		Statuses().Select(status => status.State).Should().Equal(
			LinkState.Connecting, LinkState.Waiting, LinkState.PortUnavailable, LinkState.Waiting);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task ReopeningResetsTheSequenceBaseline()
	{
		await using var reopened = new FakePortStream();
		await ConnectAsync();
		await AtAsync(0, port => port.PushBytes(FrameEncoder.Encode(Spectrum(Spectrometer.Atp8000, 5))));
		await LoseAsync(_port, 1);
		await ReopenAsync(reopened, 2);

		await AtAsync(reopened, 3, port => port.PushBytes(FrameEncoder.Encode(Spectrum(Spectrometer.Atp8000, 57))));

		TakeFrames().Should().Equal(Spectrum(Spectrometer.Atp8000, 5), Spectrum(Spectrometer.Atp8000, 57));
		_link.Status.State.Should().Be(LinkState.Receiving);
		_link.Status.LostSpectra.Should().Be(0);
	}

	[Theory(Timeout = TestTimeoutMs)]
	[MemberData(nameof(BusyPortFailures))]
	public async Task APortHeldByAnotherProgramReportsPortBusyAndRetries(Exception openFailure)
	{
		_provider.QueueOpenFailure(RjcpSerialPortProvider.MapOpenFailure(PortName, openFailure));
		_link.Connect(PortName);
		await WaitForStateAsync(LinkState.PortUnavailable);

		await FailReopenAsync(1, RjcpSerialPortProvider.MapOpenFailure(PortName, openFailure));
		await ReopenAsync(_port, 2);

		Statuses().Select(status => status.State).Should().Equal(
			LinkState.Connecting, LinkState.PortUnavailable, LinkState.Waiting);
		Faults().Should().ContainSingle().Which.Should().BeOfType<PortBusyError>()
			.Which.Message.Should().Contain(PortName);
		_provider.OpenCount.Should().Be(3);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AMissingPortReportsPortNotFoundAndRetries()
	{
		var missing = new IOException($"Port not found: {PortName}");
		_provider.QueueOpenFailure(RjcpSerialPortProvider.MapOpenFailure(PortName, missing));
		_link.Connect(PortName);
		await WaitForStateAsync(LinkState.PortUnavailable);

		_link.Status.Fault.Should().BeOfType<PortNotFoundError>()
			.Which.Message.Should().Contain(PortName);
		await ReopenAsync(_port, 1);

		_link.Status.State.Should().Be(LinkState.Waiting);
		_provider.OpenCount.Should().Be(2);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task DisconnectCancelsAPendingReadAndStopsRetrying()
	{
		await ConnectAsync();

		await _link.DisconnectAsync().WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
		_scheduler.AdvanceBy(Ticks(5));

		_port.IsDisposed.Should().BeTrue();
		_provider.OpenCount.Should().Be(1);
		Statuses().Select(status => status.State).Should().Equal(
			LinkState.Connecting, LinkState.Waiting, LinkState.Disconnected);
		_link.Status.Should().Be(new LinkStatus(LinkState.Disconnected, null, null, 0, null));
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task DisconnectDuringTheReopenWaitOpensNothingMore()
	{
		await ConnectAsync();
		await LoseAsync(_port, 1);

		await _link.DisconnectAsync().WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
		_scheduler.AdvanceTo(Ticks(2));

		_provider.OpenCount.Should().Be(1);
		_link.Status.Should().Be(new LinkStatus(LinkState.Disconnected, null, null, 0, null));
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task LostSpectraAccumulateAcrossReopensAndResetOnConnect()
	{
		await using var reopened = new FakePortStream();
		await using var reconnected = new FakePortStream();
		await ConnectAsync();
		await AtAsync(0, port => port.PushBytes(FrameEncoder.Encode(Spectrum(Spectrometer.Atp8000, 1))));
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(Spectrum(Spectrometer.Atp8000, 3))));
		await LoseAsync(_port, 2);
		await ReopenAsync(reopened, 3);
		await AtAsync(reopened, 4, port => port.PushBytes(FrameEncoder.Encode(Spectrum(Spectrometer.Atp8000, 10))));
		await AtAsync(reopened, 5, port => port.PushBytes(FrameEncoder.Encode(Spectrum(Spectrometer.Atp8000, 12))));
		_link.Status.LostSpectra.Should().Be(2);

		await _link.DisconnectAsync().WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
		await ConnectAsync(reconnected);
		await AtAsync(reconnected, 6, port => port.PushBytes(FrameEncoder.Encode(Spectrum(Spectrometer.Atp8000, 30))));

		_link.Status.State.Should().Be(LinkState.Receiving);
		Statuses().Should().OnlyContain(status => status.LostSpectra == 0);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task EachFaultIsPublishedOnceAndRetriesStayQuiet()
	{
		await using var reopened = new FakePortStream();
		await using var reopenedAgain = new FakePortStream();
		await ConnectAsync();
		await AtAsync(0, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));
		await LoseAsync(_port, 1);
		for (var second = 2; second <= 6; second++)
		{
			await FailReopenAsync(second, new PortNotFoundError(PortName));
		}

		Faults().Should().ContainSingle().Which.Should().BeOfType<PortLostError>();
		await ReopenAsync(reopened, 7);
		await LoseAsync(reopened, 8);
		Faults().Should().ContainSingle("a reopen without a frame stays in the same run");

		await ReopenAsync(reopenedAgain, 9);
		await AtAsync(reopenedAgain, 9.5, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(2))));
		await LoseAsync(reopenedAgain, 10);

		Faults().Should().HaveCount(2).And.AllBeOfType<PortLostError>();
		_provider.OpenCount.Should().Be(8);
	}

	private static byte[] CorruptedSpectrum(byte seq)
	{
		var bytes = FrameEncoder.Encode(Spectrum(Spectrometer.Atp2000, seq));
		bytes[^1] ^= 0xFF;
		return bytes;
	}

	private static long Ticks(double seconds)
	{
		return TimeSpan.FromSeconds(seconds).Ticks;
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		using var limit = new CancellationTokenSource(_waitLimit);
		while (!condition())
		{
			await Task.Delay(10, limit.Token);
		}
	}

	private async Task ConnectAsync(FakePortStream? port = null)
	{
		port ??= _port;
		ClearStatuses();
		_provider.QueueOpen(port);
		_link.Connect(PortName);
		await port.WaitForPendingReadAsync().WaitAsync(_waitLimit);
		Statuses().Select(status => status.State).Should().Equal(LinkState.Connecting, LinkState.Waiting);
	}

	private Task AtAsync(double seconds, Action<FakePortStream> push)
	{
		return AtAsync(_port, seconds, push);
	}

	/// <summary>Moves virtual time first, so the link stamps the step with the given second.</summary>
	private async Task AtAsync(FakePortStream port, double seconds, Action<FakePortStream> push)
	{
		_scheduler.AdvanceTo(Ticks(seconds));
		push(port);
		await port.WaitForPendingReadAsync().WaitAsync(_waitLimit);
	}

	/// <summary>The link arms its reopen timer before it publishes PortUnavailable, so the clock may move next.</summary>
	private async Task LoseAsync(FakePortStream port, double seconds)
	{
		var losses = CountOf(LinkState.PortUnavailable);
		_scheduler.AdvanceTo(Ticks(seconds));
		port.PushZeroRead();
		await WaitUntilAsync(() => CountOf(LinkState.PortUnavailable) > losses);
		await WaitUntilAsync(() => port.IsDisposed);
	}

	private Task ReadPendingAsync()
	{
		return _port.WaitForPendingReadAsync().WaitAsync(_waitLimit);
	}

	private async Task ReopenAsync(FakePortStream port, double seconds)
	{
		_provider.QueueOpen(port);
		_scheduler.AdvanceTo(Ticks(seconds));
		await port.WaitForPendingReadAsync().WaitAsync(_waitLimit);
	}

	/// <summary>A retry publishes nothing new, so the wait ends when the next reopen timer is armed.</summary>
	private async Task FailReopenAsync(double seconds, IError failure)
	{
		var timers = _scheduler.TimerCount;
		_provider.QueueOpenFailure(failure);
		_scheduler.AdvanceTo(Ticks(seconds));
		await WaitUntilAsync(() => _scheduler.TimerCount > timers);
	}

	private Task WaitForStateAsync(LinkState state)
	{
		return WaitUntilAsync(() => CountOf(state) > 0);
	}

	private async Task ShouldReportPortLostAsync()
	{
		await WaitForStateAsync(LinkState.PortUnavailable);

		_link.Status.State.Should().Be(LinkState.PortUnavailable);
		_link.Status.Fault.Should().BeOfType<PortLostError>()
			.Which.Message.Should().Contain(PortName);
		await WaitUntilAsync(() => _port.IsDisposed);
		_provider.OpenCount.Should().Be(1);
	}

	private List<LinkStatus> Statuses()
	{
		lock (_gate)
		{
			return [.. _statuses];
		}
	}

	private int CountOf(LinkState state)
	{
		return Statuses().Count(status => status.State == state);
	}

	private List<IError> Faults()
	{
		return [.. Statuses().Select(status => status.Fault).OfType<IError>()];
	}

	private void ClearStatuses()
	{
		lock (_gate)
		{
			_statuses.Clear();
		}
	}

	private List<BoardFrame> TakeFrames()
	{
		var frames = new List<BoardFrame>();
		while (_link.Frames.TryRead(out var frame))
		{
			frames.Add(frame);
		}

		return frames;
	}

	/// <summary>Counts the timers the link arms, so a test can wait for a retry that changes no status.</summary>
	private sealed class TimerCountingScheduler : TestScheduler
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
