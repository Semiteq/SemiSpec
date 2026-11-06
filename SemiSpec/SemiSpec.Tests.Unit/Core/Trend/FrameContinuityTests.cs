using AwesomeAssertions;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Trend;

using Xunit;

using static SemiSpec.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SemiSpec.Tests.Unit.Core.Trend;

[Trait("Component", "Core")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class FrameContinuityTests
{
	private const BoardState BothFailed = BoardState.Atp2000MeasurementFailed | BoardState.Atp8000MeasurementFailed;

	private static readonly DateTimeOffset _start = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

	private readonly FrameContinuity _continuity = new();

	[Fact]
	public void TheFirstFrameIsTheBaselineAndBreaksNothing()
	{
		var result = _continuity.Observe(Spectrum(seq: 57, atSeconds: 0));

		result.Should().Be(default(Continuity));
	}

	[Fact]
	public void ASeqStepOfOneBreaksNothing()
	{
		_continuity.Observe(Spectrum(seq: 10, atSeconds: 0));

		_continuity.Observe(Spectrum(seq: 11, atSeconds: 1)).Should().Be(default(Continuity));
	}

	[Fact]
	public void ASeqStepOfThreeBreaksWithTwoLost()
	{
		_continuity.Observe(Spectrum(seq: 10, atSeconds: 0));

		var result = _continuity.Observe(Spectrum(seq: 13, atSeconds: 1));

		result.Loss.Should().Be(new TrendBreak(UnixMs(1), 2));
		result.NoSpectra.Should().BeNull();
	}

	[Fact]
	public void AHeartbeatRepeatingTheSeqBreaksNothing()
	{
		_continuity.Observe(Spectrum(seq: 10, atSeconds: 0));

		_continuity.Observe(HeartbeatAt(seq: 10, atSeconds: 3)).Should().Be(default(Continuity));
	}

	[Fact]
	public void AHeartbeatReportingALossBreaksWithItsCount()
	{
		_continuity.Observe(Spectrum(seq: 10, atSeconds: 0));

		var result = _continuity.Observe(HeartbeatAt(seq: 12, atSeconds: 3));

		result.Loss.Should().Be(new TrendBreak(UnixMs(3), 2));
		result.NoSpectra.Should().BeNull();
	}

	[Fact]
	public void AGapOverTenSecondsWithoutAnyFrameBreaksAndIgnoresTheSeqStep()
	{
		_continuity.Observe(Spectrum(seq: 7, atSeconds: 0));

		var result = _continuity.Observe(Spectrum(seq: 1, atSeconds: 10.001));

		result.Loss.Should().BeNull("the pause ends the seq session");
		result.NoSpectra.Should().Be(new TrendBreak(UnixMs(10.001), 0));
	}

	[Fact]
	public void HeartbeatsOnlyForOverTenSecondsBreakAtTheNextSpectrum()
	{
		_continuity.Observe(Spectrum(seq: 20, atSeconds: 0));
		var heartbeats = Enumerable.Range(1, 5)
			.Select(i => _continuity.Observe(HeartbeatAt(seq: 20, atSeconds: 3 * i)))
			.ToArray();

		var lossAfterHeartbeats = _continuity.Observe(Spectrum(seq: 22, atSeconds: 16));

		heartbeats.Should().AllBeEquivalentTo(default(Continuity));
		lossAfterHeartbeats.NoSpectra.Should().Be(new TrendBreak(UnixMs(16), 0));
		lossAfterHeartbeats.Loss.Should().Be(
			new TrendBreak(UnixMs(16), 1),
			"the heartbeats kept the seq session, so the skipped seq 21 counts");
	}

	[Fact]
	public void ASpectrumExactlyTenSecondsAfterTheLastBreaksNothing()
	{
		_continuity.Observe(Spectrum(seq: 7, atSeconds: 0));

		_continuity.Observe(Spectrum(seq: 8, atSeconds: 10)).Should().Be(default(Continuity));
	}

	[Fact]
	public void ASeqGapExactlyTenSecondsAfterTheLastFrameCountsTheLoss()
	{
		_continuity.Observe(Spectrum(seq: 7, atSeconds: 0));

		var result = _continuity.Observe(Spectrum(seq: 9, atSeconds: 10));

		result.Loss.Should().Be(new TrendBreak(UnixMs(10), 1), "a pause of exactly 10 s keeps the seq session");
		result.NoSpectra.Should().BeNull();
	}

	[Theory]
	[InlineData(Spectrometer.Atp2000, false)]
	[InlineData(Spectrometer.Atp2000, true)]
	[InlineData(Spectrometer.Atp8000, false)]
	[InlineData(Spectrometer.Atp8000, true)]
	public void TheMeasuringDevicesFailedBitMarksAFailure(Spectrometer device, bool inHeartbeat)
	{
		_continuity.Observe(Spectrum(seq: 10, atSeconds: 0, device));
		var frame = inHeartbeat ? HeartbeatAt(seq: 10, atSeconds: 3) : Spectrum(seq: 11, atSeconds: 3, device);

		var result = _continuity.Observe(WithFailed(frame, BothFailed));

		result.Should().Be(new Continuity(null, null, new TrendFailure(UnixMs(3), device.FailedBit)));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void TheIdleDevicesFailedBitMarksNothing(bool inHeartbeat)
	{
		_continuity.Observe(Spectrum(seq: 10, atSeconds: 0));
		var frame = inHeartbeat ? HeartbeatAt(seq: 10, atSeconds: 3) : Spectrum(seq: 11, atSeconds: 3);

		var result = _continuity.Observe(WithFailed(frame, BoardState.Atp8000MeasurementFailed));

		result.Should().Be(default(Continuity), "the ATP-2000 measured, and the ATP-8000 bit is left from earlier");
	}

	[Fact]
	public void AFailedBitBeforeTheFirstSpectrumMarksNothing()
	{
		var result = _continuity.Observe(WithFailed(HeartbeatAt(seq: 10, atSeconds: 0), BothFailed));

		result.Should().Be(default(Continuity), "no device has measured yet");
	}

	[Fact]
	public void ASpectrumOfTheOtherDeviceMovesTheFailureToItsBit()
	{
		_continuity.Observe(Spectrum(seq: 10, atSeconds: 0));

		var switched = _continuity.Observe(WithFailed(Spectrum(seq: 11, atSeconds: 1, Spectrometer.Atp8000), BothFailed));
		var heartbeat = _continuity.Observe(
			WithFailed(HeartbeatAt(seq: 11, atSeconds: 2), BoardState.Atp2000MeasurementFailed));

		switched.Failure.Should().Be(new TrendFailure(UnixMs(1), BoardState.Atp8000MeasurementFailed));
		heartbeat.Failure.Should().BeNull("the heartbeat follows an ATP-8000 spectrum");
	}

	[Fact]
	public void ABoardRestartInsideTenSecondsBreaksWithTheLinksLossCount()
	{
		_continuity.Observe(Spectrum(seq: 55, atSeconds: 0));

		var result = _continuity.Observe(Spectrum(seq: 1, atSeconds: 5));

		result.Loss.Should().Be(new TrendBreak(UnixMs(5), 201));
		result.NoSpectra.Should().BeNull();
	}

	private static ReceivedFrame Spectrum(byte seq, double atSeconds, Spectrometer device = Spectrometer.Atp2000)
	{
		return new ReceivedFrame(SpectrumFrame(device, seq), _start.AddSeconds(atSeconds));
	}

	private static ReceivedFrame WithFailed(ReceivedFrame received, BoardState failedBits)
	{
		return received with { Frame = received.Frame with { State = received.Frame.State | failedBits } };
	}

	private static ReceivedFrame HeartbeatAt(byte seq, double atSeconds)
	{
		return new ReceivedFrame(Heartbeat(seq), _start.AddSeconds(atSeconds));
	}

	private static long UnixMs(double atSeconds)
	{
		return _start.AddSeconds(atSeconds).ToUnixTimeMilliseconds();
	}
}
