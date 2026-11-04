using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Store;

/// <summary>The intensity series: window means, coverage, the range edges and cancellation.</summary>
[Trait("Component", "Core")]
[Trait("Area", "Store")]
[Trait("Category", "Unit")]
public sealed class SpectrumHistorySeriesTests : SpectrumHistoryTestBase
{
	[Fact]
	public async Task TheSeriesMeansTheWindowPerSpectrum()
	{
		Store(
			Offset(Start.AddSeconds(0.5), 0),
			Offset(Start.AddSeconds(2.5), 10),
			Offset(Start.AddSeconds(2.7), 20),
			Offset(Start.AddSeconds(10), 30));
		var mean = RampMean(Spectrometer.Atp8000, StoreSamples.SampleNm, StoreSamples.SampleDelta);

		var series = await SeriesAsync(StoreSamples.SampleNm, StoreSamples.SampleDelta, columns: 5);

		series.Columns[0].Should().Be(new IntensityColumn(mean, mean, 1, 0));
		series.Columns[1].Should().Be(new IntensityColumn(mean + 10, mean + 20, 2, 0));
		series.Columns[2].Count.Should().Be(0);
		double.IsNaN(series.Columns[2].Min).Should().BeTrue();
		series.Columns.Sum(column => column.Count).Should().Be(3, "a row at To lies outside the range");
	}

	[Fact]
	public async Task ARowInsideTheLastMillisecondBeforeToIsInTheSeries()
	{
		var stamp = Start.AddSeconds(5);
		Store(StoreSamples.Spectrum(stamp));
		var to = stamp.AddTicks(TimeSpan.TicksPerMillisecond / 2);
		var query = Query(StoreSamples.SampleNm, StoreSamples.SampleDelta, Start, to, 1);

		var series = (await History.IntensitySeriesAsync(query, Token)).Value;

		series.Columns[0].Count.Should().Be(1, "the row's stamp lies before To");
	}

	[Fact]
	public async Task TheSeriesOfAWavelengthOneDeviceDoesNotCoverLeavesItsColumnsUncovered()
	{
		Store(
			StoreSamples.Spectrum(Start.AddSeconds(1), Spectrometer.Atp2000, nm: 650),
			StoreSamples.Spectrum(Start.AddSeconds(5), Spectrometer.Atp8000));
		var mean = RampMean(Spectrometer.Atp8000, StoreSamples.SampleNm, StoreSamples.SampleDelta);

		var series = await SeriesAsync(StoreSamples.SampleNm, StoreSamples.SampleDelta, columns: 2);

		series.Columns[0].Count.Should().Be(0);
		series.Columns[0].UncoveredCount.Should().Be(1);
		double.IsNaN(series.Columns[0].Max).Should().BeTrue();
		series.Columns[1].Should().Be(new IntensityColumn(mean, mean, 1, 0));
	}

	[Fact]
	public async Task TheSeriesOfAWavelengthNeitherDeviceCoversLeavesEveryColumnUncovered()
	{
		const ushort BeyondBothTables = 3000;
		Store(
			StoreSamples.Spectrum(Start.AddSeconds(1), Spectrometer.Atp2000, nm: 650),
			StoreSamples.Spectrum(Start.AddSeconds(5), Spectrometer.Atp8000));

		var series = await SeriesAsync(BeyondBothTables, StoreSamples.SampleDelta, columns: 2);

		series.Columns.Should().AllSatisfy(column =>
		{
			column.Count.Should().Be(0);
			column.UncoveredCount.Should().Be(1);
			double.IsNaN(column.Min).Should().BeTrue();
			double.IsNaN(column.Max).Should().BeTrue();
		});
	}

	[Fact]
	public async Task ASeriesOverARangeWithNoRowsIsEmptyInEveryColumn()
	{
		Store(StoreSamples.Spectrum(Start.AddSeconds(-1)), StoreSamples.Spectrum(Start.AddSeconds(10)));

		var series = await SeriesAsync(StoreSamples.SampleNm, StoreSamples.SampleDelta, columns: 4);

		series.Columns.Should().HaveCount(4).And.AllSatisfy(column =>
		{
			column.Count.Should().Be(0);
			column.UncoveredCount.Should().Be(0);
			double.IsNaN(column.Min).Should().BeTrue();
			double.IsNaN(column.Max).Should().BeTrue();
		});
	}

	[Fact]
	public async Task ARowWithADifferentWavelengthReadsItsPixels()
	{
		Store(StoreSamples.Spectrum(Start.AddSeconds(1), Spectrometer.Atp2000, nm: 650, delta: 10));
		var queriedMean = RampMean(Spectrometer.Atp2000, 700, 3);

		var series = await SeriesAsync(700, 3, columns: 1);

		queriedMean.Should().NotBe(RampMean(Spectrometer.Atp2000, 650, 10));
		series.Columns[0].Should().Be(new IntensityColumn(queriedMean, queriedMean, 1, 0));
	}

	[Fact]
	public async Task DarkFramesPullTheColumnMinimumToZero()
	{
		Store(StoreSamples.Spectrum(Start.AddSeconds(1)), StoreSamples.Dark(Start.AddSeconds(2)));
		var mean = RampMean(Spectrometer.Atp8000, StoreSamples.SampleNm, StoreSamples.SampleDelta);

		var series = await SeriesAsync(StoreSamples.SampleNm, StoreSamples.SampleDelta, columns: 1);

		series.Columns[0].Should().Be(new IntensityColumn(0, mean, 2, 0));
	}

	[Fact]
	public async Task ACancelledSeriesThrowsOperationCanceled()
	{
		Store(StoreSamples.Spectrum(Start.AddSeconds(1)));
		var query = Query(StoreSamples.SampleNm, StoreSamples.SampleDelta, Start, Start.AddSeconds(10), 1);

		var read = () => History.IntensitySeriesAsync(query, new CancellationToken(canceled: true));

		await read.Should().ThrowAsync<OperationCanceledException>();
	}

	[Fact]
	public void ASeriesCancelledMidReadThrowsAtTheNextCheck()
	{
		const int Rows = 600;
		var interval = TimeSpan.FromHours(2) / Rows;
		Store([.. Enumerable.Range(0, Rows).Select(i => StoreSamples.Spectrum(Start + (interval * i)))]);
		var query = Query(StoreSamples.SampleNm, StoreSamples.SampleDelta, Start, Start.AddHours(2), 100);
		using var cancellation = new CancellationTokenSource();
		using var connection = StoreConnection.OpenReader(StorePath);
		var substrCalls = 0;
		connection.CreateFunction("substr", (byte[] blob, long start, long count) =>
		{
			if (++substrCalls == 300)
			{
				cancellation.Cancel();
			}

			return blob.AsSpan((int)start - 1, (int)count).ToArray();
		});

		var read = () => SpectrumHistory.ReadSeries(connection, query, cancellation.Token);

		read.Should().Throw<OperationCanceledException>();
		substrCalls.Should().Be(512, "the token is checked every 256 rows");
	}
}
