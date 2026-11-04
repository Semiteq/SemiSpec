using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Reactive.Testing;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.Tests.Unit.Core.Store;
using SpectrumVisualizer.Tests.Unit.UI.Spectrum;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Spectrum;
using SpectrumVisualizer.UI.Trend;

using static SpectrumVisualizer.Tests.Unit.Core.Store.StoreSamples;

namespace SpectrumVisualizer.Tests.Unit.UI.Trend;

/// <summary>A trend chart over a real cache, a test scheduler as the UI scheduler and a clock stopped at Start.</summary>
public abstract class TrendChartTestBase : IDisposable
{
	protected static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

	private readonly FakeDisplayedSpectrumSource _live = new();
	private byte _seq;

	protected TrendChartTestBase()
	{
		Cache = new IntensityCache(Time);
		Switching = new SwitchingSpectrumSource(_live);
		Chart = new TrendChartViewModel(
			Cache,
			Switching,
			MessagePanel,
			Time,
			Scheduler,
			new FakeLogger<TrendChartViewModel>());
	}

	protected FakeTimeProvider Time { get; } = new(Start);

	protected TestScheduler Scheduler { get; } = new();

	protected MessagePanelViewModel MessagePanel { get; } = new(TimeProvider.System);

	protected IntensityCache Cache { get; }

	protected SwitchingSpectrumSource Switching { get; }

	protected TrendChartViewModel Chart { get; }

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		Chart.Dispose();
		Switching.Dispose();
		Cache.Dispose();
		_live.Dispose();
	}

	protected static DateTimeOffset At(double seconds)
	{
		return Start + TimeSpan.FromSeconds(seconds);
	}

	/// <summary>One ATP-8000 spectrum per second from the first second, each with the next seq.</summary>
	protected void AddSpectra(int firstSecond, int count)
	{
		for (var second = firstSecond; second < firstSecond + count; second++)
		{
			Add(StoreSamples.Spectrum(At(second), seq: NextSeq()));
		}
	}

	/// <summary>A spectrum of the device with every pixel at the value, so its window mean is the value.</summary>
	protected void AddFlat(double second, ushort value, Spectrometer device = Spectrometer.Atp8000)
	{
		var pixels = Enumerable.Repeat(value, device.PixelCount).ToArray();
		Add(StoreSamples.Spectrum(At(second), device, pixels, seq: NextSeq()));
	}

	protected void AddFailedHeartbeat(double second)
	{
		var state = BoardState.Atp8000Connected | BoardState.Atp8000MeasurementFailed;
		Add(new ReceivedFrame(new BoardFrame(state, _seq, null), At(second)));
	}

	protected void Add(ReceivedFrame frame)
	{
		Time.SetUtcNow(frame.ReceivedAt);
		Cache.Add(frame);
	}

	/// <summary>Lets the 100 ms sample of the cache's changes reach the chart.</summary>
	protected void Sample()
	{
		Scheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks);
	}

	/// <summary>An overview over the stand's cache and chart; the caller disposes it.</summary>
	protected OverviewViewModel NewOverview()
	{
		return new OverviewViewModel(Cache, Chart, MessagePanel, Scheduler, new FakeLogger<OverviewViewModel>());
	}

	protected byte NextSeq()
	{
		return ++_seq;
	}
}
