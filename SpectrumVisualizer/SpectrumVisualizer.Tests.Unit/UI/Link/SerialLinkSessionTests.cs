using AwesomeAssertions;

using FluentResults;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.UI.Link;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SpectrumVisualizer.Tests.Unit.UI.Link;

[Trait("Component", "UI")]
[Trait("Area", "Link")]
[Trait("Category", "Unit")]
public sealed class SerialLinkSessionTests : SerialLinkTestBase
{
	private static readonly LinkStatus _disconnected = new(LinkState.Disconnected, null, null, 0, null);

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AFullChannelDropsTheOldestFrame()
	{
		var frames = Enumerable.Range(1, 65).Select(seq => SpectrumFrame(Spectrometer.Atp8000, (byte)seq)).ToList();
		await ConnectAsync();

		await AtAsync(0, port => port.PushBytes(Concat(frames.Select(FrameEncoder.Encode))));

		TakeFrames().Should().Equal(frames[1..]);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AnUnexpectedExceptionEndsTheSessionWithAFault()
	{
		var thrown = new FormatException("unexpected");
		await ConnectAsync();

		Port.PushException(thrown);
		await WaitForStateAsync(LinkState.Disconnected);

		Link.Status.State.Should().Be(LinkState.Disconnected);
		Link.Status.Fault.Should().BeAssignableTo<IExceptionalError>()
			.Which.Exception.Should().BeSameAs(thrown);
		Port.IsDisposed.Should().BeTrue();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task DisconnectCancelsAPendingReadAndStopsRetrying()
	{
		await ConnectAsync();

		await Link.DisconnectAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);
		Scheduler.AdvanceBy(Ticks(5));

		Port.IsDisposed.Should().BeTrue();
		Provider.OpenCount.Should().Be(1);
		Statuses().Select(status => status.State).Should().Equal(
			LinkState.Connecting, LinkState.Waiting, LinkState.Disconnected);
		Link.Status.Should().Be(_disconnected);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task DisconnectDuringTheReopenWaitOpensNothingMore()
	{
		await ConnectAsync();
		await LoseAsync(Port, 1);

		await Link.DisconnectAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);
		Scheduler.AdvanceTo(Ticks(2));

		Provider.OpenCount.Should().Be(1);
		Link.Status.Should().Be(_disconnected);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task LostSpectraAccumulateAcrossReopensAndResetOnConnect()
	{
		await using var reopened = new FakePortStream();
		await using var reconnected = new FakePortStream();
		await ConnectAsync();
		await AtAsync(0, port => port.PushBytes(FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 1))));
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 3))));
		await LoseAsync(Port, 2);
		await ReopenAsync(reopened, 3);
		await AtAsync(
			reopened,
			4,
			port => port.PushBytes(FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 10))));
		await AtAsync(
			reopened,
			5,
			port => port.PushBytes(FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 12))));
		Link.Status.LostSpectra.Should().Be(2);

		await Link.DisconnectAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);
		await ConnectAsync(reconnected);
		await AtAsync(
			reconnected,
			6,
			port => port.PushBytes(FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 30))));

		Link.Status.State.Should().Be(LinkState.Receiving);
		Statuses().Should().OnlyContain(status => status.LostSpectra == 0);
	}
}
