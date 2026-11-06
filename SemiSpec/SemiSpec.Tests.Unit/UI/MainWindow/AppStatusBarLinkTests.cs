using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Extensions.Logging;

using SemiSpec.Core.Protocol;
using SemiSpec.Tests.Unit.UI.Link;
using SemiSpec.Tests.Unit.UI.Messages;
using SemiSpec.UI.Link;
using SemiSpec.UI.Localization;
using SemiSpec.UI.MainWindow;
using SemiSpec.UI.Messages;

using Xunit;

using static SemiSpec.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SemiSpec.Tests.Unit.UI.MainWindow;

/// <summary>The link half of the bar: state text and tone, port, lost count, spectrometers and fault entries.</summary>
[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class AppStatusBarLinkTests : AppStatusBarTestBase
{
	public static TheoryData<BoardState, string, string> SpectrometerStates => new()
	{
		{ BoardState.None, Resources.StatusSpectrometerNotConnected, Resources.StatusSpectrometerNotConnected },
		{
			BoardState.Atp8000Connected,
			Resources.StatusSpectrometerNotConnected,
			Resources.StatusSpectrometerConnected
		},
		{
			BoardState.Atp2000Connected | BoardState.Atp8000Connected | BoardState.Atp2000MeasurementFailed,
			Resources.StatusSpectrometerFailed,
			Resources.StatusSpectrometerConnected
		},
		{
			BoardState.Atp8000Connected | BoardState.Atp8000MeasurementFailed,
			Resources.StatusSpectrometerNotConnected,
			Resources.StatusSpectrometerFailed
		},
		{
			BoardState.Atp2000MeasurementFailed,
			Resources.StatusSpectrometerNotConnected,
			Resources.StatusSpectrometerNotConnected
		},
	};

	[AvaloniaFact]
	public async Task EachLinkStateShowsItsTextAndTone()
	{
		await ConnectAsync();
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));
		await AtAsync(11, port => port.PushTimeout());
		await LoseAsync(12);
		await DisconnectAsync();

		ShownStates.Should().Equal(
			(Resources.StatusConnecting, LinkTone.Neutral),
			(Resources.StatusWaiting, LinkTone.Neutral),
			(Resources.StatusReceiving, LinkTone.Ok),
			(Resources.StatusNotResponding, LinkTone.Fault),
			(Resources.StatusPortUnavailable, LinkTone.Fault),
			(Resources.StatusNotConnected, LinkTone.Neutral));
	}

	[AvaloniaFact]
	public async Task BytesWithoutAValidFrameShowOtherDevice()
	{
		await ConnectAsync();
		await AtAsync(1, port => port.PushBytes(Corrupted(SpectrumFrame(Spectrometer.Atp2000, 1))));
		await AtAsync(4, port => port.PushBytes(Corrupted(SpectrumFrame(Spectrometer.Atp2000, 2))));
		await AtAsync(10, port => port.PushTimeout());

		ViewModel.LinkText.Should().Be(Resources.StatusOtherDevice);
		ViewModel.LinkTone.Should().Be(LinkTone.Fault);
		MessagePanel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ErrorMessageMapper.Map(new UnrecognisedDataError(PortName)));
	}

	[AvaloniaFact]
	public void TheInitialTextComesFromTheLinkStatus()
	{
		ViewModel.Status.Should().BeSameAs(SerialLink.Status);
		ViewModel.LinkText.Should().Be(Resources.StatusNotConnected);
		ViewModel.LinkTone.Should().Be(LinkTone.Neutral);
	}

	[AvaloniaFact]
	public async Task PortAndLostCountReadNoValueOnlyWhileDisconnected()
	{
		ViewModel.PortText.Should().Be(Resources.StatusNoValue);
		ViewModel.LostSpectraText.Should().Be(Resources.FormatStatusLostSpectraFormat(Resources.StatusNoValue));

		await ConnectAsync();
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 1))));
		await AtAsync(2, port => port.PushBytes(FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 4))));

		ViewModel.PortText.Should().Be(PortName);
		ViewModel.LostSpectraText.Should().Be(Resources.FormatStatusLostSpectraFormat(2));

		await DisconnectAsync();

		ViewModel.PortText.Should().Be(Resources.StatusNoValue);
		ViewModel.LostSpectraText.Should().Be(Resources.FormatStatusLostSpectraFormat(Resources.StatusNoValue));
	}

	[AvaloniaFact]
	public async Task TheBoardTextsReadNoDataOnceTheLinkStopsReceiving()
	{
		await ConnectAsync();
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 1))));
		ViewModel.Atp8000Text.Should().Be(SpectrumText(Resources.StatusSpectrometerConnected));

		await AtAsync(11, port => port.PushTimeout());

		ViewModel.Atp8000Text.Should().Be(SpectrumText(Resources.StatusSpectrometerNoData), "the link keeps the byte");
		ViewModel.LostSpectraText.Should().Be(Resources.FormatStatusLostSpectraFormat(Resources.StatusNoValue));
	}

	[AvaloniaFact]
	public async Task BothSpectrometersReadNoDataBeforeTheFirstFrame()
	{
		await ConnectAsync();

		ViewModel.Atp2000Text.Should().Be(SpectrometerText("ATP-2000", Resources.StatusSpectrometerNoData));
		ViewModel.Atp8000Text.Should().Be(SpectrometerText("ATP-8000", Resources.StatusSpectrometerNoData));
	}

	[AvaloniaTheory]
	[MemberData(nameof(SpectrometerStates))]
	public async Task EachSpectrometerTextFollowsItsStateBits(BoardState state, string atp2000, string atp8000)
	{
		await ConnectAsync();
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(new BoardFrame(state, 1, null))));

		ViewModel.Atp2000Text.Should().Be(SpectrometerText("ATP-2000", atp2000));
		ViewModel.Atp8000Text.Should().Be(SpectrometerText("ATP-8000", atp8000));
	}

	[AvaloniaFact]
	public async Task AFaultIsReportedOnceForItsFaultedStatus()
	{
		await ConnectAsync();
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));
		await AtAsync(11, port => port.PushTimeout());
		await AtAsync(21, port => port.PushTimeout());
		await AtAsync(31, port => port.PushTimeout());

		MessagePanel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ErrorMessageMapper.Map(new BoardNotRespondingError(PortName)));
		Logger.Collector.GetSnapshot().Select(record => record.Level).Should().Equal(LogLevel.Warning);
	}

	[AvaloniaFact]
	public async Task ReceivingAfterAFaultAddsOneInfoEntry()
	{
		await ConnectAsync();
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));
		await AtAsync(11, port => port.PushTimeout());
		await AtAsync(12, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(2))));
		await AtAsync(13, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(3))));

		MessagePanel.Entries.Select(entry => entry.View).Should().Equal(
			new MessageView(
				Resources.LinkRestoredTitle,
				Resources.LinkRestoredDetail,
				string.Empty,
				MessageSeverity.Info),
			ErrorMessageMapper.Map(new BoardNotRespondingError(PortName)));
		Logger.Collector.GetSnapshot().Select(record => record.Level)
			.Should().Equal(LogLevel.Warning, LogLevel.Information);
	}

	[AvaloniaFact]
	public async Task TheFirstReceivingAfterConnectAddsNoEntry()
	{
		await ConnectAsync();
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));

		ViewModel.LinkText.Should().Be(Resources.StatusReceiving);
		MessagePanel.Entries.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task AConnectAfterAFaultStartsCleanAndAddsNoInfoEntry()
	{
		await using var reconnected = new FakePortStream();
		await ConnectAsync();
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));
		await AtAsync(11, port => port.PushTimeout());
		await DisconnectAsync();

		await ConnectAsync(reconnected);
		await AtAsync(reconnected, 12, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(2))));

		ViewModel.LinkText.Should().Be(Resources.StatusReceiving);
		MessagePanel.Entries.Select(entry => entry.View.Severity).Should().Equal(MessageSeverity.Warning);
	}

	[AvaloniaFact]
	public async Task AThrowingPanelGoesToTheLog()
	{
		using var poison = ReportingTestDoubles.PoisonEntries(MessagePanel);
		await ConnectAsync();
		await AtAsync(1, port => port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));
		await AtAsync(11, port => port.PushTimeout());
		Dispatcher.UIThread.RunJobs();

		ViewModel.LinkText.Should().Be(
			Resources.StatusNotResponding,
			"a failed report leaves the shown state in place");
		var last = Logger.LatestRecord;
		last.Level.Should().Be(LogLevel.Error);
		last.Message.Should().Be("The failure could not reach the message panel");
		last.Exception.Should().BeOfType<PoisonedPanelException>();
	}

	private static string SpectrumText(string state)
	{
		return SpectrometerText("ATP-8000", state);
	}
}
