using Microsoft.Extensions.Time.Testing;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Store;
using SemiSpec.Core.Trend;
using SemiSpec.Tests.Unit.Core.Store;

using static SemiSpec.Tests.Unit.Core.Store.StoreSamples;

namespace SemiSpec.Tests.Unit.Core.Trend;

public abstract class IntensityCacheTestBase : SpectrumHistoryTestBase
{
	protected const ushort OtherNm = 1400;
	protected const ushort ThirdNm = 1500;

	protected static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

	protected IntensityCacheTestBase()
	{
		Cache = new IntensityCache(Time);
		Cache.ReReadRequested.Subscribe(Requests.Add);
	}

	protected FakeTimeProvider Time { get; } = new(Start);

	protected IntensityCache Cache { get; }

	protected List<ReReadRequest> Requests { get; } = [];

	protected static DateTimeOffset At(double seconds)
	{
		return Start + TimeSpan.FromSeconds(seconds);
	}

	protected static TimeSpan Width(int seconds, int columns)
	{
		return TimeSpan.FromMilliseconds(Math.Ceiling(seconds * 1000.0 / columns));
	}

	protected static BoardFrame FailedHeartbeat(byte seq)
	{
		return new BoardFrame(
			BoardState.Atp8000Connected | BoardState.Atp8000MeasurementFailed, seq, null);
	}

	/// <summary>
	/// A chunk of one point per second over [fromSecond, toSecond), valued at its second unless given.
	/// </summary>
	protected static IntensityChunk Chunk(int fromSecond, int toSecond, double? value = null)
	{
		var points = Enumerable.Range(fromSecond, toSecond - fromSecond)
			.Select(second => new TrendPoint(At(second).ToUnixTimeMilliseconds(), value ?? second))
			.ToArray();
		return new IntensityChunk(At(fromSecond), points);
	}

	/// <summary>
	/// Spectra each second at the sample key and ATP-8000, darks every seventh, ATP-2000 every fifth, a heartbeat after
	/// every third and a gap at every eleventh.
	/// </summary>
	protected static List<ReceivedFrame> MixedFrames(int seconds)
	{
		var frames = new List<ReceivedFrame>();
		for (var i = 0; i < seconds; i++)
		{
			if (i % 11 == 10)
			{
				continue;
			}

			var device = i % 5 == 4 ? Spectrometer.Atp2000 : Spectrometer.Atp8000;
			frames.Add(i % 7 == 6 ? Dark(At(i)) : StoreSamples.Spectrum(At(i), device));
			if (i % 3 == 2)
			{
				frames.Add(Heartbeat(At(i + 0.5)));
			}
		}

		return frames;
	}

	protected static List<(int Column, int Breaks, int Lost)> BreaksOf(IntensityColumn[] columns)
	{
		return [.. columns
			.Select((column, index) => (index, column.BreakCount, column.LostCount))
			.Where(entry => entry.BreakCount > 0)];
	}

	/// <summary>
	/// Spectra at each second of [fromSecond, toSecond), the seq stepping by seqStep from the second.
	/// </summary>
	protected void AddSpectra(int fromSecond, int toSecond, ushort nm = SampleNm, int seqStep = 1)
	{
		var seqOffset = seqStep - 1;
		for (var i = fromSecond; i < toSecond; i++)
		{
			Cache.Add(StoreSamples.Spectrum(At(i), nm: nm, seq: (byte)(i + seqOffset)));
		}
	}

	/// <summary>The cache folded over [fromSecond, toSecond) in one-second columns.</summary>
	protected IntensityColumn[] SecondColumns(int fromSecond, int toSecond)
	{
		return Cache.Fold(Query(SampleNm, SampleDelta, At(fromSecond), At(toSecond), Second)).Columns;
	}
}
