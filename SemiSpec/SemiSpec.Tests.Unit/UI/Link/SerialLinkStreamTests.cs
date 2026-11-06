using AwesomeAssertions;

using SemiSpec.Core.Protocol;
using SemiSpec.UI.Link;

using Xunit;

using static SemiSpec.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SemiSpec.Tests.Unit.UI.Link;

[Trait("Component", "UI")]
[Trait("Area", "Link")]
[Trait("Category", "Unit")]
public sealed class SerialLinkStreamTests : SerialLinkTestBase
{
	[Fact(Timeout = TestTimeoutMs)]
	public async Task FramesInRandomChunksReachTheChannel()
	{
		BoardFrame[] frames =
			[SpectrumFrame(Spectrometer.Atp2000, 1), SpectrumFrame(Spectrometer.Atp8000, 2), Heartbeat(2)];
		var bytes = Concat(frames.Select(FrameEncoder.Encode));
		await ConnectAsync();

		var random = new Random(20261104);
		for (var offset = 0; offset < bytes.Length;)
		{
			var size = Math.Min(random.Next(1, 600), bytes.Length - offset);
			Port.PushBytes(bytes[offset..(offset + size)]);
			offset += size;
		}

		await Port.WaitForPendingReadAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);

		TakeFrames().Should().BeEquivalentTo(frames, options => options.WithStrictOrdering());
		Link.Status.Should().Be(new LinkStatus(LinkState.Receiving, PortName, Heartbeat(2).State, 0, null));
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task SilenceForTenSecondsReportsBoardNotResponding()
	{
		await ConnectAsync();

		await AtAsync(9.9, port => port.PushTimeout());
		Link.Status.State.Should().Be(LinkState.Waiting);
		await AtAsync(10, port => port.PushTimeout());
		await AtAsync(20, port => port.PushTimeout());

		var notResponding = Statuses().Where(status => status.State == LinkState.NotResponding).ToList();
		notResponding.Should().ContainSingle();
		notResponding[0].Fault.Should().BeOfType<BoardNotRespondingError>()
			.Which.Message.Should().Contain(PortName);
		Link.Status.State.Should().Be(LinkState.NotResponding);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task SilenceAfterAFrameNeverDrainsThePartialFrameInFlight()
	{
		var next = FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 2));
		await ConnectAsync();

		await AtAsync(1, port => port.PushBytes(Concat(FrameEncoder.Encode(Heartbeat(1)), next[..500])));
		await AtAsync(11, port => port.PushTimeout());
		await AtAsync(21, port => port.PushTimeout());
		await AtAsync(31, port => port.PushTimeout());
		await AtAsync(32, port => port.PushBytes(next[500..]));

		TakeFrames().Should().Equal(Heartbeat(1), SpectrumFrame(Spectrometer.Atp8000, 2));
		Statuses().Where(status => status.Fault is not null).Should().ContainSingle()
			.Which.Fault.Should().BeOfType<BoardNotRespondingError>();
		Link.Status.State.Should().Be(LinkState.Receiving);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task BytesWithoutAValidFrameForTenSecondsReportOtherDevice()
	{
		await ConnectAsync();

		await AtAsync(1, port => port.PushBytes(Corrupted(SpectrumFrame(Spectrometer.Atp2000, 1))));
		await AtAsync(4, port => port.PushBytes(Corrupted(SpectrumFrame(Spectrometer.Atp2000, 2))));
		await AtAsync(7, port => port.PushBytes(Corrupted(SpectrumFrame(Spectrometer.Atp2000, 3))));
		Link.Status.State.Should().Be(LinkState.Waiting);
		await AtAsync(10, port => port.PushTimeout());

		Link.Status.State.Should().Be(LinkState.OtherDevice);
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
		Link.Status.State.Should().Be(LinkState.Receiving);
		Statuses().Should().NotContain(status =>
			status.State == LinkState.NotResponding || status.State == LinkState.OtherDevice);

		await AtAsync(20, port => port.PushTimeout());
		Link.Status.State.Should().Be(LinkState.NotResponding, "the released frames carry the drain time, 10 s");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task FramesOfOneReadCarryTheSchedulerTimeOfThatRead()
	{
		var twoFrames = Concat(FrameEncoder.Encode(Heartbeat(1)), FrameEncoder.Encode(Heartbeat(2)));
		await ConnectAsync();

		await AtAsync(2, port => port.PushBytes(twoFrames));

		TakeReceivedFrames().Should().Equal(
			new ReceivedFrame(Heartbeat(1), WallClockAt(2)),
			new ReceivedFrame(Heartbeat(2), WallClockAt(2)));
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task FramesReleasedByADrainCarryTheDrainTime()
	{
		byte[] falseCandidate = [0xAA, 0x55, 0x12, 0x10, 0x00, 0x00];
		await ConnectAsync();

		await AtAsync(1, port => port.PushBytes(falseCandidate));
		await AtAsync(3, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(2))));
		await AtAsync(10, port => port.PushTimeout());

		TakeReceivedFrames().Should().Equal(new ReceivedFrame(Heartbeat(2), WallClockAt(10)));
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task ADrainDiscardsThePartialFrameInFlight()
	{
		var spectrum = FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 2));
		var half = spectrum.Length / 2;
		await ConnectAsync();

		await AtAsync(0, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));
		await AtAsync(1, port => port.PushBytes(spectrum[..half]));
		await AtAsync(10, port => port.PushTimeout());
		await AtAsync(11, port => port.PushBytes(spectrum[half..]));
		await AtAsync(12, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(2))));

		TakeFrames().Should().Equal(Heartbeat(1), Heartbeat(2));
		Link.Status.State.Should().Be(LinkState.Receiving);
		Link.Status.LostSpectra.Should().Be(1);
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
		Link.Status.State.Should().Be(LinkState.Receiving);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task TheLostTotalFollowsTheSequenceTracker()
	{
		await ConnectAsync();

		await AtAsync(0, port => port.PushBytes(FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 1))));
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 2))));
		Link.Status.LostSpectra.Should().Be(0);
		await AtAsync(2, port => port.PushBytes(FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 5))));

		Link.Status.LostSpectra.Should().Be(2);
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
}
