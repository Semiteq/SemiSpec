using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.UI.Link;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SpectrumVisualizer.Tests.Unit.UI.Link;

/// <summary>Nothing reads the link's frames here, so the channel fills and drops.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Link")]
[Trait("Category", "Unit")]
public sealed class SerialLinkDropTests : SerialLinkTestBase
{
	private const int ChannelCapacity = 64;
	private const int Overflow = 3;

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AChannelDropCountsInTheStatus()
	{
		await ConnectAsync();

		await OverflowTheChannelAsync(0);

		Link.Status.DroppedFrames.Should().Be(Overflow);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task TheDropCountSurvivesAReconnect()
	{
		await using var reconnected = new FakePortStream();
		await ConnectAsync();
		await OverflowTheChannelAsync(0);
		await Link.DisconnectAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);

		await ConnectAsync(reconnected);
		Statuses().Should().OnlyContain(status => status.DroppedFrames == Overflow);
		await AtAsync(reconnected, 1, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));

		Link.Status.DroppedFrames.Should().Be(Overflow + 1);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task TheDisconnectedStatusCarriesTheDropCount()
	{
		await ConnectAsync();
		await OverflowTheChannelAsync(0);

		await Link.DisconnectAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);

		Link.Status.State.Should().Be(LinkState.Disconnected);
		Link.Status.DroppedFrames.Should().Be(Overflow);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AChannelDropDoesNotCountAsLost()
	{
		await ConnectAsync();

		await OverflowTheChannelAsync(0);

		Link.Status.LostSpectra.Should().Be(0);
		Link.Status.DroppedFrames.Should().Be(Overflow);
	}

	/// <summary>Consecutive seq values, so the sequence tracker sees no gap.</summary>
	private Task OverflowTheChannelAsync(double seconds)
	{
		var frames = Enumerable.Range(1, ChannelCapacity + Overflow)
			.Select(seq => FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, (byte)seq)));
		return AtAsync(seconds, port => port.PushBytes(Concat(frames)));
	}
}
