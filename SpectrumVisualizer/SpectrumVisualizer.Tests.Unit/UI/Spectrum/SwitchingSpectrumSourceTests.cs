using AwesomeAssertions;

using SpectrumVisualizer.UI.Spectrum;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.UI.Spectrum.SpectrumSamples;

namespace SpectrumVisualizer.Tests.Unit.UI.Spectrum;

[Trait("Component", "UI")]
[Trait("Area", "Spectrum")]
[Trait("Category", "Unit")]
public sealed class SwitchingSpectrumSourceTests : IDisposable
{
	private static readonly DateTimeOffset _moment = new(2026, 11, 4, 8, 30, 15, TimeSpan.Zero);

	private readonly FakeDisplayedSpectrumSource _live = new();
	private readonly SwitchingSpectrumSource _source;
	private int _signals;

	public SwitchingSpectrumSourceTests()
	{
		_source = new SwitchingSpectrumSource(_live);
		_source.CurrentChanged.Subscribe(_ => _signals++);
	}

	[Fact]
	public void LiveByDefault()
	{
		var live = Atp2000Peak;
		_live.Current = live;

		_live.Signal();

		_source.Current.Should().BeSameAs(live);
		_source.ShownMoment.Should().BeNull();
		_signals.Should().Be(1, "a live signal passes through");
	}

	[Fact]
	public void ShowMomentSwitchesCurrentAndSignals()
	{
		_live.Current = Atp2000Peak;
		var stored = Atp8000Peak.Measurement;

		_source.ShowMoment(stored, _moment);

		_source.Current.Should().Be(new DisplayedSpectrum(stored, _moment, IsHistorical: true));
		_source.ShownMoment.Should().Be(_moment);
		_signals.Should().Be(1);
	}

	[Fact]
	public void LiveFramesWhileHistoricalKeepTheMoment()
	{
		_source.ShowMoment(Atp8000Peak.Measurement, _moment);
		var shown = _source.Current;

		_live.Current = Atp2000Peak;
		_live.Signal();

		_source.Current.Should().BeSameAs(shown, "a reader that compares by reference drops the live signal");
		_source.ShownMoment.Should().Be(_moment);
	}

	[Fact]
	public void ReturnToLiveShowsTheLatestLiveSpectrum()
	{
		_source.ShowMoment(Atp8000Peak.Measurement, _moment);
		var latest = Atp2000Peak;
		_live.Current = latest;

		_source.ReturnToLive();
		_source.ReturnToLive();

		_source.Current.Should().BeSameAs(latest);
		_source.ShownMoment.Should().BeNull();
		_signals.Should().Be(2, "the second return while live changes nothing");
	}

	[Fact]
	public void EveryShownMomentCarriesANewMeasurement()
	{
		var stored = Atp8000Peak.Measurement;

		_source.ShowMoment(stored, _moment);
		var first = _source.Current!.Measurement;
		_source.ShowMoment(stored, _moment);
		var second = _source.Current!.Measurement;

		first.Should().NotBeSameAs(stored).And.NotBeSameAs(second);
		second.Should().NotBeSameAs(stored);
		first.Pixels.Should().BeSameAs(stored.Pixels, "the pixel array is immutable and shared");
		second.Pixels.Should().BeSameAs(stored.Pixels);
	}

	public void Dispose()
	{
		_source.Dispose();
		_live.Dispose();
	}
}
