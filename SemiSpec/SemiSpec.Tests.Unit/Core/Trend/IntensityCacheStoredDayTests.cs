using AwesomeAssertions;

using Microsoft.Extensions.Time.Testing;

using SemiSpec.Core.Store;
using SemiSpec.Core.Trend;
using SemiSpec.Tests.Unit.Core.Store;

using Xunit;

using static SemiSpec.Tests.Unit.Core.Store.StoreSamples;

namespace SemiSpec.Tests.Unit.Core.Trend;

[Trait("Component", "Core")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class IntensityCacheStoredDayTests : IntensityCacheTestBase
{
	private static readonly TrendKey _sampleKey = new(SampleNm, SampleDelta);
	private static readonly TrendKey _otherKey = new(OtherNm, SampleDelta);

	[Fact]
	public void TheRunStartIsTheClockAtConstruction()
	{
		var time = new FakeTimeProvider(At(42));
		using var cache = new IntensityCache(time);

		time.Advance(TimeSpan.FromMinutes(5));

		cache.RunStart.Should().Be(At(42));
		Cache.RunStart.Should().Be(Start);
	}

	[Fact]
	public void AStoredDayRequestSeedsTheKeyAndAsksForEverythingBeforeTheRunStart()
	{
		Cache.RequestStoredDay(_otherKey);

		Requests.Should().Equal(new ReReadRequest(1, _otherKey, Start));
		Cache.LandHistory(1, Chunk(-20, -10));
		Cache.Extent.Should().Be(new TrendExtent(_otherKey, At(-20), At(-11)));
	}

	[Fact]
	public void AStoredDayRequestAfterALiveSpectrumAsksAtTheLiveKey()
	{
		Cache.Add(StoreSamples.Spectrum(At(1), seq: 1));

		Cache.RequestStoredDay(_otherKey);

		Requests.Should().Equal(new ReReadRequest(1, _sampleKey, Start));
		Cache.Extent!.Value.Key.Should().Be(_sampleKey);
	}

	[Fact]
	public void AStoredDayRequestAfterAKeyChangeAsksNothing()
	{
		AddSpectra(0, 5);
		Cache.Add(StoreSamples.Spectrum(At(5), nm: OtherNm, seq: 5));
		Requests.Should().ContainSingle();

		Cache.RequestStoredDay(_sampleKey);

		Requests.Should().ContainSingle("the key change's re-read covers the stored day");
		Cache.Extent!.Value.Key.Should().Be(_otherKey);
	}

	[Fact]
	public void FoldCurrentDropsPointsOlderThanADayWithoutNewFrames()
	{
		Cache.RequestStoredDay(_sampleKey);
		Cache.LandHistory(1, Chunk(-20, -10));
		Cache.FoldCurrent(QueryOver).Should().NotBeNull();

		Time.Advance(TimeSpan.FromHours(25));

		Cache.FoldCurrent(QueryOver).Should().BeNull("every point is older than a day");
		Cache.Extent.Should().BeNull();
	}

	private static IntensityQuery QueryOver(TrendExtent extent)
	{
		return Query(extent.Key.WavelengthNm, extent.Key.Delta, extent.First, extent.Newest.AddSeconds(1), Second);
	}
}
