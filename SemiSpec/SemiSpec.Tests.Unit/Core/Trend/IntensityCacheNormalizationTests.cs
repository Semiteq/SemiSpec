using AwesomeAssertions;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Store;
using SemiSpec.Core.Trend;
using SemiSpec.Tests.Unit.Core.Store;

using Xunit;

using static SemiSpec.Tests.Unit.Core.Store.StoreSamples;

namespace SemiSpec.Tests.Unit.Core.Trend;

[Trait("Component", "Core")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class IntensityCacheNormalizationTests : IntensityCacheTestBase
{
	private readonly List<NormalizationState> _states = [];

	public IntensityCacheNormalizationTests()
	{
		Cache.NormalizationChanged.Subscribe(_states.Add);
	}

	[Fact]
	public void WithoutASpectrumTheStartFailsAndNothingChanges()
	{
		var started = Cache.StartNormalization(100);

		started.Errors.Should().ContainSingle().Which.Should().BeOfType<NormalizationUnavailableError>();
		Cache.CanNormalize.Should().BeFalse();
		Cache.Normalization.Should().Be(NormalizationState.Off);
		_states.Should().BeEmpty();
		Requests.Should().BeEmpty();
	}

	[Fact]
	public void AStartFixesTheNewestSpectrumAndTheRecentPointsReadOneHundred()
	{
		AddSpectra(0, 10);

		Cache.StartNormalization(100).IsSuccess.Should().BeTrue();

		Cache.Normalization.Should().Be(new NormalizationState(100));
		_states.Should().Equal(new NormalizationState(100));
		SecondColumns(0, 10).Should().AllSatisfy(column => column.Min.Should().BeApproximately(100.0, 1e-9));
		var request = Requests.Single();
		request.RecentStart.Should().Be(Start);
		request.Reference!.Factor.Should().Be(100);
		request.Reference.Device.Should().Be(Spectrometer.Atp8000);
	}

	[Fact]
	public void ALaterSpectrumIsTheMeanOfItsPixelsOverTheirOwnReferencePixels()
	{
		AddSpectra(0, 1);
		Cache.StartNormalization(100);
		var reference = Requests.Single().Reference!;
		var flat = Enumerable.Repeat((ushort)1000, Spectrometer.Atp8000.PixelCount).ToArray();

		Cache.Add(StoreSamples.Spectrum(At(1), Spectrometer.Atp8000, flat, seq: 1));

		var meanOfRatios = reference.Pixels.Average(pixel => 1000.0 / pixel) * 100;
		SecondColumns(1, 2)[0].Min.Should().BeApproximately(meanOfRatios, 1e-9);
		meanOfRatios.Should().NotBeApproximately(1000.0 / reference.Pixels.Average(pixel => (double)pixel) * 100, 1e-6);
	}

	[Fact]
	public void ADarkNewestSpectrumRefusesTheStart()
	{
		AddSpectra(0, 3);
		Cache.Add(Dark(At(3)));

		var started = Cache.StartNormalization(100);

		started.Errors.Should().ContainSingle().Which.Should().BeOfType<DarkReferenceError>();
		Cache.Normalization.IsOn.Should().BeFalse();
		_states.Should().BeEmpty();
	}

	[Fact]
	public void AnUncoveredNewestSpectrumCannotBeNormalized()
	{
		AddSpectra(0, 3);
		Cache.Add(StoreSamples.Spectrum(At(3), Spectrometer.Atp2000, seq: 3));

		Cache.CanNormalize.Should().BeFalse();
		Cache.StartNormalization(100).Errors.Should().ContainSingle()
			.Which.Should().BeOfType<NormalizationUnavailableError>();
	}

	[Fact]
	public void AStopReturnsTheRawPointsAndAsksForARawReRead()
	{
		AddSpectra(0, 10);
		Cache.StartNormalization(1000);

		Cache.StopNormalization();

		_states.Should().Equal(new NormalizationState(1000), NormalizationState.Off);
		var raw = RampMean(Spectrometer.Atp8000, SampleNm, SampleDelta);
		SecondColumns(0, 10).Should().AllSatisfy(column => column.Min.Should().Be(raw));
		Requests.Should().HaveCount(2);
		Requests[1].Reference.Should().BeNull();
		Requests[1].Generation.Should().Be(2);
	}

	[Fact]
	public void AStopWhileOffDoesNothing()
	{
		AddSpectra(0, 3);

		Cache.StopNormalization();

		_states.Should().BeEmpty();
		Requests.Should().BeEmpty();
	}

	[Fact]
	public void AKeyChangeEndsTheNormalizationAndSaysSo()
	{
		AddSpectra(0, 10);
		Cache.StartNormalization(100);

		Cache.Add(StoreSamples.Spectrum(At(10), nm: OtherNm, seq: 10));

		_states.Should().Equal(new NormalizationState(100), new NormalizationState(null, EndedByKeyChange: true));
		Cache.Normalization.Should().Be(NormalizationState.Off);
		Requests.Last().Reference.Should().BeNull();
		SecondColumns(0, 11).Should().AllSatisfy(column =>
			column.Min.Should().Be(RampMean(Spectrometer.Atp8000, OtherNm, SampleDelta)));
	}

	[Fact]
	public void AChunkOfAnEarlierGenerationIsIgnoredAfterAStart()
	{
		AddSpectra(200, 210);
		Cache.StartNormalization(100);
		var normalizedGeneration = Requests.Single().Generation;

		Cache.LandHistory(normalizedGeneration - 1, Chunk(0, 100, value: 5000));
		Cache.LandHistory(normalizedGeneration, Chunk(100, 200, value: 90));

		SecondColumns(0, 100).Sum(column => column.Count).Should().Be(0);
		SecondColumns(100, 200).Should().AllSatisfy(column => column.Min.Should().Be(90));
	}

	[Fact]
	public async Task APressRacingKeyChangesNeverLeavesAReferenceOfAnotherKey()
	{
		AddSpectra(0, 1);
		var lastSecond = 0;
		var adder = Task.Run(() =>
		{
			for (var i = 1; i < 2000; i++)
			{
				var nm = i % 50 < 25 ? SampleNm : OtherNm;
				Cache.Add(StoreSamples.Spectrum(At(i * 0.01), nm: nm, seq: (byte)i));
				lastSecond = i;
			}
		}, TestContext.Current.CancellationToken);
		var presser = Task.Run(() =>
		{
			for (var i = 0; i < 2000; i++)
			{
				if (i % 2 == 0)
				{
					Cache.StartNormalization(100);
				}
				else
				{
					Cache.StopNormalization();
				}
			}
		}, TestContext.Current.CancellationToken);

		await Task.WhenAll(adder, presser);
		Cache.StartNormalization(100).IsSuccess.Should().BeTrue();

		var request = Requests.Last();
		var newestKey = new TrendKey(lastSecond % 50 < 25 ? SampleNm : OtherNm, SampleDelta);
		request.Key.Should().Be(newestKey);
		var window = new QueryWindows(newestKey.WavelengthNm, newestKey.Delta).For(Spectrometer.Atp8000);
		request.Reference!.Pixels.Should().HaveCount(window.Last - window.First + 1);
	}
}
