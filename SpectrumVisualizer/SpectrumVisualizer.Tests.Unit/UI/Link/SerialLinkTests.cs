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

	private readonly TestScheduler _scheduler = new();
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
			.Which.Metadata[LinkError.PortKey].Should().Be(PortName);
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
		_link.DroppedFrames.Should().Be(1);
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

	private static byte[] CorruptedSpectrum(byte seq)
	{
		var bytes = FrameEncoder.Encode(Spectrum(Spectrometer.Atp2000, seq));
		bytes[^1] ^= 0xFF;
		return bytes;
	}

	private async Task ConnectAsync()
	{
		_provider.QueueOpen(_port);
		_link.Connect(PortName);
		await ReadPendingAsync();
		Statuses().Select(status => status.State).Should().Equal(LinkState.Connecting, LinkState.Waiting);
	}

	/// <summary>Moves virtual time first, so the link stamps the step with the given second.</summary>
	private async Task AtAsync(double seconds, Action<FakePortStream> push)
	{
		_scheduler.AdvanceTo(TimeSpan.FromSeconds(seconds).Ticks);
		push(_port);
		await ReadPendingAsync();
	}

	private Task ReadPendingAsync()
	{
		return _port.WaitForPendingReadAsync().WaitAsync(_waitLimit);
	}

	private async Task WaitForStateAsync(LinkState state)
	{
		using var limit = new CancellationTokenSource(_waitLimit);
		while (!Statuses().Any(status => status.State == state))
		{
			await Task.Delay(10, limit.Token);
		}
	}

	private async Task ShouldReportPortLostAsync()
	{
		await WaitForStateAsync(LinkState.Disconnected);

		var statuses = Statuses();
		statuses[^2].State.Should().Be(LinkState.PortUnavailable);
		statuses[^2].Fault.Should().BeOfType<PortLostError>()
			.Which.Metadata[LinkError.PortKey].Should().Be(PortName);
		statuses[^1].Should().Be(new LinkStatus(LinkState.Disconnected, null, null, 0, null));
		_port.IsDisposed.Should().BeTrue();
	}

	private List<LinkStatus> Statuses()
	{
		lock (_gate)
		{
			return [.. _statuses];
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
}
