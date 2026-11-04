using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Trend;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SpectrumVisualizer.Tests.Unit.Core.Trend;

[Trait("Component", "Core")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class FrameContinuityTests
{
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

		result.Loss.Should().Be(new TrendBreak(UnixMs(1), BreakKind.Loss, 2));
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

		result.Loss.Should().Be(new TrendBreak(UnixMs(3), BreakKind.Loss, 2));
		result.NoSpectra.Should().BeNull();
	}

	[Fact]
	public void AGapOverTenSecondsWithoutAnyFrameBreaksAndIgnoresTheSeqStep()
	{
		_continuity.Observe(Spectrum(seq: 7, atSeconds: 0));

		var result = _continuity.Observe(Spectrum(seq: 1, atSeconds: 10.001));

		result.Loss.Should().BeNull("the pause ends the seq session");
		result.NoSpectra.Should().Be(new TrendBreak(UnixMs(10.001), BreakKind.NoSpectra, 0));
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
		lossAfterHeartbeats.NoSpectra.Should().Be(new TrendBreak(UnixMs(16), BreakKind.NoSpectra, 0));
		lossAfterHeartbeats.Loss.Should().Be(
			new TrendBreak(UnixMs(16), BreakKind.Loss, 1),
			"the heartbeats kept the seq session, so the skipped seq 21 counts");
	}

	[Fact]
	public void ASpectrumExactlyTenSecondsAfterTheLastBreaksNothing()
	{
		_continuity.Observe(Spectrum(seq: 7, atSeconds: 0));

		_continuity.Observe(Spectrum(seq: 8, atSeconds: 10)).Should().Be(default(Continuity));
	}

	[Theory]
	[InlineData(BoardState.Atp8000MeasurementFailed, false)]
	[InlineData(BoardState.Atp2000MeasurementFailed, true)]
	[InlineData(BoardState.Atp8000MeasurementFailed, true)]
	[InlineData(BoardState.Atp8000MeasurementFailed | BoardState.Atp2000MeasurementFailed, true)]
	public void AFailedBitMarksAFailure(BoardState failedBits, bool inHeartbeat)
	{
		_continuity.Observe(Spectrum(seq: 10, atSeconds: 0));
		var frame = inHeartbeat ? HeartbeatAt(seq: 10, atSeconds: 3) : Spectrum(seq: 11, atSeconds: 3);
		var failed = frame with { Frame = frame.Frame with { State = frame.Frame.State | failedBits } };

		var result = _continuity.Observe(failed);

		result.Should().Be(new Continuity(null, null, new TrendFailure(UnixMs(3), failedBits)));
	}

	[Fact]
	public void ABoardRestartInsideTenSecondsBreaksWithTheLinksLossCount()
	{
		_continuity.Observe(Spectrum(seq: 55, atSeconds: 0));

		var result = _continuity.Observe(Spectrum(seq: 1, atSeconds: 5));

		result.Loss.Should().Be(new TrendBreak(UnixMs(5), BreakKind.Loss, 201));
		result.NoSpectra.Should().BeNull();
	}

	private static ReceivedFrame Spectrum(byte seq, double atSeconds)
	{
		return new ReceivedFrame(SpectrumFrame(Spectrometer.Atp2000, seq), _start.AddSeconds(atSeconds));
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
