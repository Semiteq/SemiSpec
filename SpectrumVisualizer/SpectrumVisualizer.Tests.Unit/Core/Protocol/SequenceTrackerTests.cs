using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SpectrumVisualizer.Tests.Unit.Core.Protocol;

[Trait("Component", "Core")]
[Trait("Area", "Protocol")]
[Trait("Category", "Unit")]
public sealed class SequenceTrackerTests
{
	private readonly SequenceTracker _tracker = new();

	[Theory]
	[InlineData(1)]
	[InlineData(57)]
	public void TheFirstSpectrumIsTheBaseline(byte seq)
	{
		_tracker.Observe(Spectrum(Spectrometer.Atp8000, seq)).Should().Be(0);
	}

	[Fact]
	public void TheFirstHeartbeatIsTheBaseline()
	{
		_tracker.Observe(Heartbeat(57)).Should().Be(0);
		_tracker.Observe(Spectrum(Spectrometer.Atp8000, 58)).Should().Be(0);
	}

	[Fact]
	public void TheNextSpectrumLosesNothing()
	{
		_tracker.Observe(Spectrum(Spectrometer.Atp8000, 10));

		_tracker.Observe(Spectrum(Spectrometer.Atp8000, 11)).Should().Be(0);
	}

	[Theory]
	[InlineData(10, 12, 1)]
	[InlineData(10, 15, 4)]
	[InlineData(250, 3, 8)]
	[InlineData(10, 9, 254)]
	public void ASpectrumThatSkipsKReportsKMinusOneLost(byte previous, byte next, int lost)
	{
		_tracker.Observe(Spectrum(Spectrometer.Atp8000, previous));

		_tracker.Observe(Spectrum(Spectrometer.Atp8000, next)).Should().Be(lost);
	}

	[Fact]
	public void ASpectrumRepeatingThePreviousSeqReports256Lost()
	{
		_tracker.Observe(Spectrum(Spectrometer.Atp8000, 10));

		_tracker.Observe(Spectrum(Spectrometer.Atp8000, 10)).Should().Be(256);
	}

	[Fact]
	public void TheSeqWrapsFrom255To0()
	{
		_tracker.Observe(Spectrum(Spectrometer.Atp8000, 255));

		_tracker.Observe(Spectrum(Spectrometer.Atp8000, 0)).Should().Be(0);
	}

	[Fact]
	public void AHeartbeatRepeatingTheLastSpectrumSeqLosesNothing()
	{
		_tracker.Observe(Spectrum(Spectrometer.Atp8000, 10));

		_tracker.Observe(Heartbeat(10)).Should().Be(0);
		_tracker.Observe(Heartbeat(10)).Should().Be(0);
		_tracker.Observe(Spectrum(Spectrometer.Atp8000, 11)).Should().Be(0);
	}

	[Fact]
	public void AHeartbeatWithADifferentSeqReportsTheLossOnce()
	{
		_tracker.Observe(Spectrum(Spectrometer.Atp8000, 10));

		_tracker.Observe(Heartbeat(13)).Should().Be(3);
		_tracker.Observe(Heartbeat(13)).Should().Be(0);
		_tracker.Observe(Spectrum(Spectrometer.Atp8000, 14)).Should().Be(0);
	}

	[Fact]
	public void ResetStartsANewBaseline()
	{
		_tracker.Observe(Spectrum(Spectrometer.Atp8000, 10));

		_tracker.Reset();

		_tracker.Observe(Spectrum(Spectrometer.Atp8000, 200)).Should().Be(0);
		_tracker.Observe(Spectrum(Spectrometer.Atp8000, 202)).Should().Be(1);
	}
}
