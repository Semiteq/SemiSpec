using AwesomeAssertions;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Store;

using Xunit;

namespace SemiSpec.Tests.Unit.Core.Store;

/// <summary>The intensity points: one window mean per stored spectrum, NaN where the window cannot be read.</summary>
[Trait("Component", "Core")]
[Trait("Area", "Store")]
[Trait("Category", "Unit")]
public sealed class SpectrumHistoryPointsTests : SpectrumHistoryTestBase
{
	[Fact]
	public async Task PointsCarryTheWindowMeanPerSpectrum()
	{
		Store(
			Offset(Start.AddSeconds(1), 0),
			StoreSamples.Heartbeat(Start.AddSeconds(2)),
			Offset(Start.AddSeconds(3), 10),
			Offset(Start.AddSeconds(10), 20));
		var mean = RampMean(Spectrometer.Atp8000, StoreSamples.SampleNm, StoreSamples.SampleDelta);

		var chunk = await PointsAsync(StoreSamples.SampleNm, StoreSamples.SampleDelta);

		chunk.From.Should().Be(Start);
		chunk.Points.Should().Equal(Point(Start.AddSeconds(1), mean), Point(Start.AddSeconds(3), mean + 10));
	}

	[Fact]
	public async Task PointsOfAnUncoveredDeviceAreNaN()
	{
		Store(
			StoreSamples.Spectrum(Start.AddSeconds(1), Spectrometer.Atp2000, nm: 650),
			StoreSamples.Spectrum(Start.AddSeconds(2), Spectrometer.Atp8000));
		var mean = RampMean(Spectrometer.Atp8000, StoreSamples.SampleNm, StoreSamples.SampleDelta);

		var chunk = await PointsAsync(StoreSamples.SampleNm, StoreSamples.SampleDelta);

		chunk.Points.Should().Equal(Point(Start.AddSeconds(1), double.NaN), Point(Start.AddSeconds(2), mean));
	}

	[Theory]
	[InlineData("device = 7")]
	[InlineData("pixels = zeroblob(100)")]
	public async Task ACorruptRowIsNaN(string corruption)
	{
		Store(StoreSamples.Spectrum(Start.AddSeconds(1)), StoreSamples.Spectrum(Start.AddSeconds(2)));
		Corrupt($"UPDATE spectrum SET {corruption} WHERE id = 1");
		var mean = RampMean(Spectrometer.Atp8000, StoreSamples.SampleNm, StoreSamples.SampleDelta);

		var chunk = await PointsAsync(StoreSamples.SampleNm, StoreSamples.SampleDelta);

		chunk.Points.Should().Equal(Point(Start.AddSeconds(1), double.NaN), Point(Start.AddSeconds(2), mean));
	}

	[Fact]
	public async Task PointsComeInReceiveOrder()
	{
		Store(
			Offset(Start.AddSeconds(5), 30),
			Offset(Start.AddSeconds(2), 10),
			Offset(Start.AddSeconds(2), 20),
			Offset(Start.AddSeconds(1), 0));
		var mean = RampMean(Spectrometer.Atp8000, StoreSamples.SampleNm, StoreSamples.SampleDelta);

		var chunk = await PointsAsync(StoreSamples.SampleNm, StoreSamples.SampleDelta);

		chunk.Points.Should().Equal(
			Point(Start.AddSeconds(1), mean),
			Point(Start.AddSeconds(2), mean + 10),
			Point(Start.AddSeconds(2), mean + 20),
			Point(Start.AddSeconds(5), mean + 30));
	}

	[Fact]
	public async Task AReferenceNormalizesEachPixelBeforeTheMean()
	{
		var referenceFrame = StoreSamples.Spectrum(Start);
		var windows = new QueryWindows(StoreSamples.SampleNm, StoreSamples.SampleDelta);
		var reference = NormalizationReference.From(windows, referenceFrame.Frame.Measurement!, factor: 100).Value;
		var flat = Enumerable.Repeat((ushort)1000, Spectrometer.Atp8000.PixelCount).ToArray();
		Store(referenceFrame, StoreSamples.Spectrum(Start.AddSeconds(1), Spectrometer.Atp8000, flat));
		var meanOfRatios = reference.Pixels.Average(pixel => 1000.0 / pixel) * 100;
		var ratioOfMeans = 1000.0 / reference.Pixels.Average(pixel => (double)pixel) * 100;

		var result = await History.IntensityPointsAsync(
			StoreSamples.SampleNm, StoreSamples.SampleDelta, Start, Start.AddSeconds(10), reference, Token);

		result.Value.Points[0].Value.Should().BeApproximately(100.0, 1e-9, "the reference spectrum itself");
		result.Value.Points[1].Value.Should().BeApproximately(meanOfRatios, 1e-9);
		meanOfRatios.Should().NotBeApproximately(ratioOfMeans, 1e-6, "the ramp window is not flat");
	}

	[Fact]
	public async Task WithAReferenceARowOfTheOtherDeviceIsNaN()
	{
		// Both firmware tables cover 950 nm, so only the reference's device tells the rows apart.
		const ushort OverlapNm = 950;
		var referenceFrame = StoreSamples.Spectrum(Start, Spectrometer.Atp8000, nm: OverlapNm);
		var windows = new QueryWindows(OverlapNm, StoreSamples.SampleDelta);
		var reference = NormalizationReference.From(windows, referenceFrame.Frame.Measurement!, factor: 100).Value;
		Store(referenceFrame, StoreSamples.Spectrum(Start.AddSeconds(1), Spectrometer.Atp2000, nm: OverlapNm));

		var normalized = await History.IntensityPointsAsync(
			OverlapNm, StoreSamples.SampleDelta, Start, Start.AddSeconds(10), reference, Token);
		var raw = await PointsAsync(OverlapNm, StoreSamples.SampleDelta);

		double.IsNaN(normalized.Value.Points[1].Value).Should().BeTrue();
		double.IsNaN(raw.Points[1].Value).Should().BeFalse("without a reference the ATP-2000 row is a plain mean");
	}

	[Fact]
	public async Task ACancelledReadThrowsOperationCanceled()
	{
		Store(StoreSamples.Spectrum(Start.AddSeconds(1)));

		var read = () => History.IntensityPointsAsync(
			StoreSamples.SampleNm,
			StoreSamples.SampleDelta,
			Start,
			Start.AddSeconds(10),
			reference: null,
			new CancellationToken(canceled: true));

		await read.Should().ThrowAsync<OperationCanceledException>();
	}

	/// <summary>The points of the 10 s from Start.</summary>
	private async Task<IntensityChunk> PointsAsync(ushort nm, byte delta)
	{
		var result = await History.IntensityPointsAsync(nm, delta, Start, Start.AddSeconds(10), reference: null, Token);
		result.IsSuccess.Should().BeTrue();
		return result.Value;
	}

	private static TrendPoint Point(DateTimeOffset receivedAt, double value)
	{
		return new TrendPoint(receivedAt.ToUnixTimeMilliseconds(), value);
	}
}
