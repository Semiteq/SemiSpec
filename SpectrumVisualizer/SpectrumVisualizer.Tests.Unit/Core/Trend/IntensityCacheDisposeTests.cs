using AwesomeAssertions;

using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.Tests.Unit.Core.Store;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Trend;

[Trait("Component", "Core")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class IntensityCacheDisposeTests : IntensityCacheTestBase
{
	[Fact]
	public void AfterTheDisposeFramesAndChunksAreIgnored()
	{
		var cache = new IntensityCache(Time);
		cache.Add(StoreSamples.Spectrum(At(0), seq: 0));
		cache.Add(StoreSamples.Spectrum(At(1), nm: OtherNm, seq: 1));
		cache.Dispose();

		var lateFrame = () => cache.Add(StoreSamples.Spectrum(At(2), nm: OtherNm, seq: 2));
		var lateChunk = () => cache.LandHistory(1, Chunk(0, 1));

		lateFrame.Should().NotThrow();
		lateChunk.Should().NotThrow();
		cache.Extent!.Value.Newest.Should().Be(At(1));
	}

	[Fact]
	public void ASubscriberThatDisposesTheCacheOnAKeyChangeStopsTheChangedSignal()
	{
		var cache = new IntensityCache(Time);
		cache.Add(StoreSamples.Spectrum(At(0), seq: 0));
		var changes = 0;
		cache.Changed.Subscribe(_ => changes++);
		cache.ReReadRequested.Subscribe(_ => cache.Dispose());

		var keyChange = () => cache.Add(StoreSamples.Spectrum(At(1), nm: OtherNm, seq: 1));

		keyChange.Should().NotThrow();
		changes.Should().Be(0);
	}

	[Fact]
	public void ASubscriberThatDisposesTheCacheOnALandedChunkStopsTheChangedSignal()
	{
		var cache = new IntensityCache(Time);
		cache.Add(StoreSamples.Spectrum(At(0), seq: 0));
		cache.Add(StoreSamples.Spectrum(At(1), nm: OtherNm, seq: 1));
		var changes = 0;
		cache.Changed.Subscribe(_ => changes++);
		cache.HistoryLanded.Subscribe(_ => cache.Dispose());

		var landing = () => cache.LandHistory(1, Chunk(0, 1));

		landing.Should().NotThrow();
		changes.Should().Be(0);
	}
}
