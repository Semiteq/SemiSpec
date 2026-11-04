using System.Buffers;

using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Tools.BoardEmulator;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SpectrumVisualizer.Tests.Unit.Tools;

[Trait("Component", "Tools")]
[Trait("Area", "Emulator")]
[Trait("Category", "Unit")]
public sealed class FrameScheduleTests
{
	private readonly ManualTimeProvider _time = new();

	[Fact]
	public void TheFirstSpectrumIsSentAtOnceWithSeqOne()
	{
		var schedule = new FrameSchedule(Options(rate: 1), _time);

		var frame = Decode(schedule.Next());

		frame.Seq.Should().Be(1);
		frame.Measurement.Should().NotBeNull();
		frame.State.Should().Be(BoardState.Atp8000Connected);
	}

	[Fact]
	public void SpectraFollowTheRate()
	{
		var schedule = new FrameSchedule(Options(rate: 2), _time);
		schedule.Next().Should().NotBeNull();

		_time.Advance(TimeSpan.FromMilliseconds(499));
		schedule.Next().Should().BeNull();

		_time.Advance(TimeSpan.FromMilliseconds(1));
		Decode(schedule.Next()).Seq.Should().Be(2);
	}

	[Fact]
	public void ALateTickSendsOneSpectrumAndDropsTheBacklog()
	{
		var schedule = new FrameSchedule(Options(rate: 10), _time);
		schedule.Next();

		_time.Advance(TimeSpan.FromSeconds(1));
		Decode(schedule.Next()).Seq.Should().Be(2);
		schedule.Next().Should().BeNull();
	}

	[Fact]
	public void RateZeroSendsAHeartbeatEveryThreeSecondsWithSeqZero()
	{
		var schedule = new FrameSchedule(Options(rate: 0), _time);
		schedule.Next().Should().BeNull();

		_time.Advance(TimeSpan.FromMilliseconds(2999));
		schedule.Next().Should().BeNull();

		_time.Advance(TimeSpan.FromMilliseconds(1));
		Decode(schedule.Next()).Should().Be(new BoardFrame(BoardState.Atp8000Connected, 0, null));

		_time.Advance(TimeSpan.FromSeconds(1));
		schedule.Next().Should().BeNull();

		_time.Advance(TimeSpan.FromSeconds(2));
		Decode(schedule.Next()).Measurement.Should().BeNull();
	}

	[Fact]
	public void ARateAboveOneFrameInThreeSecondsSendsNoHeartbeat()
	{
		var schedule = new FrameSchedule(Options(rate: 0.5), _time);
		var frames = new List<BoardFrame>();

		for (var tick = 0; tick < 100; tick++)
		{
			if (schedule.Next() is { } bytes)
			{
				frames.Add(Decode(bytes));
			}

			_time.Advance(TimeSpan.FromMilliseconds(100));
		}

		frames.Should().HaveCount(5).And.OnlyContain(frame => frame.Measurement != null);
	}

	[Fact]
	public void AHeartbeatRepeatsTheLastSpectrumSeq()
	{
		var schedule = new FrameSchedule(Options(rate: 0.25), _time);
		Decode(schedule.Next()).Seq.Should().Be(1);

		_time.Advance(TimeSpan.FromSeconds(3));

		Decode(schedule.Next()).Should().Be(new BoardFrame(BoardState.Atp8000Connected, 1, null));
	}

	[Fact]
	public void SeqWrapsFrom255ToZero()
	{
		var schedule = new FrameSchedule(Options(rate: 1), _time);
		var seqs = new List<byte>();
		for (var second = 0; second < 257; second++)
		{
			seqs.Add(Decode(schedule.Next()).Seq);
			_time.Advance(TimeSpan.FromSeconds(1));
		}

		seqs[254].Should().Be(255);
		seqs[255].Should().Be(0);
		seqs[256].Should().Be(1);
	}

	[Fact]
	public void EveryNthFrameHasABadCrcAndStillConsumesItsSeq()
	{
		var schedule = new FrameSchedule(Options(rate: 1, badCrcEvery: 3), _time);
		var frames = new List<byte[]>();
		for (var second = 0; second < 6; second++)
		{
			frames.Add(schedule.Next()!);
			_time.Advance(TimeSpan.FromSeconds(1));
		}

		frames.Select(HasValidCrc).Should().Equal(true, true, false, true, true, false);
		frames.Select(bytes => bytes[5]).Should().Equal(1, 2, 3, 4, 5, 6);
	}

	[Fact]
	public void BadCrcCountsHeartbeatsToo()
	{
		var schedule = new FrameSchedule(Options(rate: 0, badCrcEvery: 2), _time);

		_time.Advance(TimeSpan.FromSeconds(3));
		HasValidCrc(schedule.Next()!).Should().BeTrue();

		_time.Advance(TimeSpan.FromSeconds(3));
		HasValidCrc(schedule.Next()!).Should().BeFalse();
	}

	[Fact]
	public void TheAtp2000FrameCarriesItsDeviceAndConnectedBit()
	{
		var options = Options(rate: 1) with { Device = Spectrometer.Atp2000, WavelengthNm = 650 };
		var schedule = new FrameSchedule(options, _time);

		var frame = Decode(schedule.Next());

		frame.State.Should().Be(BoardState.Atp2000Connected);
		frame.Measurement!.Device.Should().Be(Spectrometer.Atp2000);
		frame.Measurement.WavelengthNm.Should().Be(650);
	}

	private static EmulatorOptions Options(double rate, int badCrcEvery = 0)
	{
		return new EmulatorOptions("COM_TEST", Spectrometer.Atp8000, rate, 1550, 10, badCrcEvery);
	}

	private static BoardFrame Decode(byte[]? bytes)
	{
		bytes.Should().NotBeNull();
		var buffer = new ReadOnlySequence<byte>(bytes);
		var found = FrameDecoder.TryReadFrame(ref buffer, out var frame, out var discarded);
		discarded.Should().Be(0);
		return found ? frame! : throw new InvalidOperationException("The bytes hold no whole valid frame.");
	}

	private sealed class ManualTimeProvider : TimeProvider
	{
		private long _ticks;

		public override long TimestampFrequency => TimeSpan.TicksPerSecond;

		public override long GetTimestamp()
		{
			return _ticks;
		}

		public void Advance(TimeSpan by)
		{
			_ticks += by.Ticks;
		}
	}
}
