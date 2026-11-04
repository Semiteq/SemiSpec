using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.UI.Link;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SpectrumVisualizer.Tests.Unit.UI.Link;

[Trait("Component", "UI")]
[Trait("Area", "Link")]
[Trait("Category", "Unit")]
public sealed class SerialLinkPortLossTests : SerialLinkTestBase
{
	public static TheoryData<Exception> BusyPortFailures =>
	[
		new UnauthorizedAccessException("Access to the port is denied."),
		new IOException($"Sharing violation: {PortName}", 32)
	];

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AZeroByteReadReportsPortLost()
	{
		await ConnectAsync();

		Port.PushZeroRead();

		await ShouldReportPortLostAsync();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AClosedPortReportsPortLost()
	{
		await ConnectAsync();

		Port.PushException(new InvalidOperationException("the port is closed"));

		await ShouldReportPortLostAsync();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AnIOExceptionReportsPortLost()
	{
		await ConnectAsync();

		Port.PushException(new IOException("device monitoring thread has died"));

		await ShouldReportPortLostAsync();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task APortThatVanishesFromThePortListReportsPortLostOnTheThirdSilentTick()
	{
		await ConnectAsync();
		Provider.SetPorts("COM4");

		await AtAsync(1, port => port.PushTimeout());
		await AtAsync(2, port => port.PushTimeout());
		Link.Status.State.Should().Be(LinkState.Waiting);
		Provider.PortListReads.Should().Be(0);
		Port.PushTimeout();

		await ShouldReportPortLostAsync();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AByteRestartsTheCountOfSilentTicks()
	{
		await ConnectAsync();
		Provider.SetPorts("COM4");

		await AtAsync(1, port => port.PushTimeout());
		await AtAsync(2, port => port.PushTimeout());
		await AtAsync(3, port => port.PushBytes(0x00));
		await AtAsync(4, port => port.PushTimeout());
		await AtAsync(5, port => port.PushTimeout());

		Link.Status.State.Should().Be(LinkState.Waiting);
		Provider.PortListReads.Should().Be(0);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task APortListThatCannotBeReadKeepsThePortOpen()
	{
		await ConnectAsync();
		Provider.FailPortNames(new Error("the port registry key is gone"));

		await AtAsync(1, port => port.PushTimeout());
		await AtAsync(2, port => port.PushTimeout());
		await AtAsync(3, port => port.PushTimeout());

		Provider.PortListReads.Should().Be(1);
		Link.Status.State.Should().Be(LinkState.Waiting);
		Statuses().Should().NotContain(status => status.State == LinkState.PortUnavailable);
		Port.IsDisposed.Should().BeFalse();
		Provider.OpenCount.Should().Be(1);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task PortUnavailableIsPublishedWhileTheLostPortIsStillClosing()
	{
		await ConnectAsync();
		Port.HoldDispose();

		Port.PushZeroRead();
		await WaitForStateAsync(LinkState.PortUnavailable);

		Port.IsDisposed.Should().BeFalse();
		Port.ReleaseDispose();
		await Waits.UntilAsync(() => Port.IsDisposed);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task APortWhoseCloseHangsIsAbandonedAfterTwoSecondsAndTheLinkReopens()
	{
		await using var reopened = new FakePortStream();
		await ConnectAsync();
		Port.HoldDispose();
		var timers = Scheduler.TimerCount;

		Scheduler.AdvanceTo(Ticks(1));
		Port.PushZeroRead();
		await WaitForStateAsync(LinkState.PortUnavailable);
		await Waits.UntilAsync(() => Scheduler.TimerCount == timers + 2);
		Provider.QueueOpen(reopened);
		Scheduler.AdvanceTo(Ticks(2.9));
		Provider.OpenCount.Should().Be(1, "the reopen waits for the close or its limit");
		Scheduler.AdvanceTo(Ticks(3));
		await reopened.WaitForPendingReadAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);

		Provider.OpenCount.Should().Be(2);
		Port.IsDisposed.Should().BeFalse();
		Logger.Collector.GetSnapshot().Should().ContainSingle(record => record.Level == LogLevel.Warning)
			.Which.Message.Should().Contain(PortName);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task APortLostMidFrameDropsTheHalfFrameAndTheReopenedPortStartsClean()
	{
		var spectrum = FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 2));
		var half = spectrum.Length / 2;
		await using var reopened = new FakePortStream();
		await ConnectAsync();
		await AtAsync(0, port => port.PushBytes(Concat(FrameEncoder.Encode(Heartbeat(1)), spectrum[..half])));

		Port.PushException(new IOException("device monitoring thread has died"));
		await WaitForStateAsync(LinkState.PortUnavailable);
		await ReopenAsync(reopened, 1);
		await AtAsync(
			reopened,
			2,
			port => port.PushBytes(Concat(spectrum[half..], FrameEncoder.Encode(Heartbeat(3)))));

		TakeFrames().Should().Equal(Heartbeat(1), Heartbeat(3));
		Link.Status.State.Should().Be(LinkState.Receiving);
		Link.Status.LostSpectra.Should().Be(0, "the reopen resets the sequence baseline");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task ALostPortReopensAfterOneSecond()
	{
		await using var reopened = new FakePortStream();
		await ConnectAsync();
		await LoseAsync(Port, 2);

		Provider.QueueOpen(reopened);
		Scheduler.AdvanceTo(Ticks(2.9));
		Provider.OpenCount.Should().Be(1);
		Scheduler.AdvanceTo(Ticks(3));
		await reopened.WaitForPendingReadAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);

		Provider.OpenCount.Should().Be(2);
		Statuses().Select(status => status.State).Should().Equal(
			LinkState.Connecting, LinkState.Waiting, LinkState.PortUnavailable, LinkState.Waiting);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task ReopeningResetsTheSequenceBaseline()
	{
		await using var reopened = new FakePortStream();
		await ConnectAsync();
		await AtAsync(0, port => port.PushBytes(FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 5))));
		await LoseAsync(Port, 1);
		await ReopenAsync(reopened, 2);

		await AtAsync(
			reopened,
			3,
			port => port.PushBytes(FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 57))));

		TakeFrames().Should().Equal(SpectrumFrame(Spectrometer.Atp8000, 5), SpectrumFrame(Spectrometer.Atp8000, 57));
		Link.Status.State.Should().Be(LinkState.Receiving);
		Link.Status.LostSpectra.Should().Be(0);
	}

	[Theory(Timeout = TestTimeoutMs)]
	[MemberData(nameof(BusyPortFailures))]
	public async Task APortHeldByAnotherProgramReportsPortBusyAndRetries(Exception openFailure)
	{
		Provider.QueueOpenFailure(RjcpSerialPortProvider.MapOpenFailure(PortName, openFailure));
		Link.Connect(PortName);
		await WaitForStateAsync(LinkState.PortUnavailable);

		await FailReopenAsync(1, RjcpSerialPortProvider.MapOpenFailure(PortName, openFailure));
		await ReopenAsync(Port, 2);

		Statuses().Select(status => status.State).Should().Equal(
			LinkState.Connecting, LinkState.PortUnavailable, LinkState.Waiting);
		Faults().Should().ContainSingle().Which.Should().BeOfType<PortBusyError>()
			.Which.Message.Should().Contain(PortName);
		Provider.OpenCount.Should().Be(3);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AMissingPortReportsPortNotFoundAndRetries()
	{
		var missing = new IOException($"Port not found: {PortName}");
		Provider.QueueOpenFailure(RjcpSerialPortProvider.MapOpenFailure(PortName, missing));
		Link.Connect(PortName);
		await WaitForStateAsync(LinkState.PortUnavailable);

		Link.Status.Fault.Should().BeOfType<PortNotFoundError>()
			.Which.Message.Should().Contain(PortName);
		await ReopenAsync(Port, 1);

		Link.Status.State.Should().Be(LinkState.Waiting);
		Provider.OpenCount.Should().Be(2);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task EachFaultIsPublishedOnceAndRetriesStayQuiet()
	{
		await using var reopened = new FakePortStream();
		await using var reopenedAgain = new FakePortStream();
		await ConnectAsync();
		await AtAsync(0, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));
		await LoseAsync(Port, 1);
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
		Provider.OpenCount.Should().Be(8);
	}

	private async Task ShouldReportPortLostAsync()
	{
		await WaitForStateAsync(LinkState.PortUnavailable);

		Link.Status.State.Should().Be(LinkState.PortUnavailable);
		Link.Status.Fault.Should().BeOfType<PortLostError>()
			.Which.Message.Should().Contain(PortName);
		await Waits.UntilAsync(() => Port.IsDisposed);
		Provider.OpenCount.Should().Be(1);
	}
}
