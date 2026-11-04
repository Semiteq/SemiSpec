using AwesomeAssertions;

using FluentResults;

using Microsoft.Data.Sqlite;

using SpectrumVisualizer.Core.Calibration;
using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Tests.Unit.Core.Protocol;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Store;

[Trait("Component", "Core")]
[Trait("Area", "Store")]
[Trait("Category", "Unit")]
public sealed class SpectrumHistoryTests : IDisposable
{
	private static readonly DateTimeOffset _start = StoreSamples.Start;

	private readonly TempFolder _folder = new();
	private readonly string _path;
	private readonly SpectrumHistory _history;

	public SpectrumHistoryTests()
	{
		_path = _folder.FileIn("spectra-1.db");
		_history = new SpectrumHistory(_path);
	}

	public void Dispose()
	{
		_folder.Dispose();
	}

	[Fact]
	public async Task SpectrumAtReturnsTheNearestRowAtOrBefore()
	{
		Store(
			StoreSamples.Spectrum(_start, seq: 1),
			StoreSamples.Spectrum(_start.AddSeconds(1), seq: 2),
			StoreSamples.Spectrum(_start.AddSeconds(1), seq: 3),
			StoreSamples.Spectrum(_start.AddSeconds(2), seq: 4));

		(await SeqAtAsync(_start)).Should().Be(1);
		(await SeqAtAsync(_start.AddSeconds(1))).Should().Be(3, "equal stamps resolve to the later row");
		(await SeqAtAsync(_start.AddSeconds(1.5))).Should().Be(3);
		(await SeqAtAsync(_start.AddHours(1))).Should().Be(4);
	}

	[Fact]
	public async Task SpectrumAtBeforeTheFirstRowReturnsNone()
	{
		Store(StoreSamples.Heartbeat(_start.AddSeconds(-5)), StoreSamples.Spectrum(_start));

		var result = await _history.SpectrumAtAsync(_start.AddMilliseconds(-1), Token);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeNull();
	}

	[Fact]
	public async Task TheStoredSpectrumRebuildsTheMeasurement()
	{
		var frame = StoreSamples.Spectrum(_start, Spectrometer.Atp2000, nm: 650, delta: 10, seq: 42);
		Store(frame);

		var first = (await _history.SpectrumAtAsync(_start, Token)).Value!;
		var second = (await _history.SpectrumAtAsync(_start, Token)).Value!;

		first.Measurement.Should().Be(frame.Frame.Measurement);
		first.ReceivedAt.Should().Be(_start);
		first.Seq.Should().Be(42);
		first.State.Should().Be(frame.Frame.State);
		second.Measurement.Pixels.Should().NotBeSameAs(first.Measurement.Pixels, "each call rebuilds a new measurement");
	}

	[Fact]
	public async Task TheSeriesMeansTheWindowPerSpectrum()
	{
		Store(
			Offset(_start.AddSeconds(0.5), 0),
			Offset(_start.AddSeconds(2.5), 10),
			Offset(_start.AddSeconds(2.7), 20),
			Offset(_start.AddSeconds(10), 30));
		var mean = RampMean(Spectrometer.Atp8000, StoreSamples.SampleNm, StoreSamples.SampleDelta);

		var series = await SeriesAsync(StoreSamples.SampleNm, StoreSamples.SampleDelta, columns: 5);

		series.Columns[0].Should().Be(new IntensityColumn(mean, mean, 1, 0));
		series.Columns[1].Should().Be(new IntensityColumn(mean + 10, mean + 20, 2, 0));
		series.Columns[2].Count.Should().Be(0);
		double.IsNaN(series.Columns[2].Min).Should().BeTrue();
		series.Columns.Sum(column => column.Count).Should().Be(3, "a row at To lies outside the range");
	}

	[Fact]
	public async Task TheSeriesOfAWavelengthOneDeviceDoesNotCoverLeavesItsColumnsUncovered()
	{
		Store(
			StoreSamples.Spectrum(_start.AddSeconds(1), Spectrometer.Atp2000, nm: 650),
			StoreSamples.Spectrum(_start.AddSeconds(5), Spectrometer.Atp8000));
		var mean = RampMean(Spectrometer.Atp8000, StoreSamples.SampleNm, StoreSamples.SampleDelta);

		var series = await SeriesAsync(StoreSamples.SampleNm, StoreSamples.SampleDelta, columns: 2);

		series.Columns[0].Count.Should().Be(0);
		series.Columns[0].UncoveredCount.Should().Be(1);
		double.IsNaN(series.Columns[0].Max).Should().BeTrue();
		series.Columns[1].Should().Be(new IntensityColumn(mean, mean, 1, 0));
	}

	[Fact]
	public async Task ARowWithADifferentWavelengthReadsItsPixels()
	{
		Store(StoreSamples.Spectrum(_start.AddSeconds(1), Spectrometer.Atp2000, nm: 650, delta: 10));
		var queriedMean = RampMean(Spectrometer.Atp2000, 700, 3);

		var series = await SeriesAsync(700, 3, columns: 1);

		queriedMean.Should().NotBe(RampMean(Spectrometer.Atp2000, 650, 10));
		series.Columns[0].Should().Be(new IntensityColumn(queriedMean, queriedMean, 1, 0));
	}

	[Fact]
	public async Task DarkFramesPullTheColumnMinimumToZero()
	{
		Store(StoreSamples.Spectrum(_start.AddSeconds(1)), StoreSamples.Dark(_start.AddSeconds(2)));
		var mean = RampMean(Spectrometer.Atp8000, StoreSamples.SampleNm, StoreSamples.SampleDelta);

		var series = await SeriesAsync(StoreSamples.SampleNm, StoreSamples.SampleDelta, columns: 1);

		series.Columns[0].Should().Be(new IntensityColumn(0, mean, 2, 0));
	}

	[Fact]
	public async Task ACancelledSeriesThrowsOperationCanceled()
	{
		Store(StoreSamples.Spectrum(_start.AddSeconds(1)));
		var query = Query(StoreSamples.SampleNm, StoreSamples.SampleDelta, _start, _start.AddSeconds(10), 1);

		var read = () => _history.IntensitySeriesAsync(query, new CancellationToken(canceled: true));

		await read.Should().ThrowAsync<OperationCanceledException>();
	}

	[Fact]
	public void ASeriesCancelledMidReadThrowsAtTheNextCheck()
	{
		const int Rows = 600;
		var interval = TimeSpan.FromHours(2) / Rows;
		Store([.. Enumerable.Range(0, Rows).Select(i => StoreSamples.Spectrum(_start + (interval * i)))]);
		var query = Query(StoreSamples.SampleNm, StoreSamples.SampleDelta, _start, _start.AddHours(2), 100);
		using var cancellation = new CancellationTokenSource();
		using var connection = StoreConnection.OpenReader(_path);
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

	[Fact]
	public async Task TheStoredRangeSpansTheFirstAndLastSpectrum()
	{
		using var database = SpectrumDatabase.Open(_path).Value;
		database.Write([StoreSamples.Heartbeat(_start.AddSeconds(-5))]).IsSuccess.Should().BeTrue();
		(await _history.StoredRangeAsync(Token)).Value.Should().BeNull();
		database.Write(
		[
			StoreSamples.Spectrum(_start),
			StoreSamples.Spectrum(_start.AddMinutes(30)),
			StoreSamples.Heartbeat(_start.AddHours(1)),
		]).IsSuccess.Should().BeTrue();

		var range = await _history.StoredRangeAsync(Token);

		range.Value.Should().Be(new StoredRange(_start, _start.AddMinutes(30)));
	}

	[Fact]
	public async Task AReadOfAFileWithoutTheSchemaFails()
	{
		var result = await _history.StoredRangeAsync(Token);

		result.IsFailed.Should().BeTrue();
		result.Errors.Should().ContainSingle().Which.Should().BeOfType<ExceptionalError>()
			.Which.Exception.Should().BeOfType<SqliteException>();
	}

	[Fact]
	public async Task TheHistoryReadsWhileTheWriterWrites()
	{
		using var database = SpectrumDatabase.Open(_path).Value;
		using var stop = new CancellationTokenSource();
		var batches = 0;
		var writer = Task.Run(
			() =>
			{
				while (!stop.IsCancellationRequested)
				{
					var at = _start.AddSeconds(batches);
					var written = database.Write([StoreSamples.Spectrum(at), StoreSamples.Spectrum(at.AddMilliseconds(500))]);
					written.IsSuccess.Should().BeTrue();
					Interlocked.Increment(ref batches);
				}
			},
			Token);
		var query = Query(StoreSamples.SampleNm, StoreSamples.SampleDelta, _start, _start.AddDays(1), 100);
		var reads = new List<Result<IntensitySeries>>();
		try
		{
			while (reads.Count < 20 || Volatile.Read(ref batches) < 3)
			{
				reads.Add(await _history.IntensitySeriesAsync(query, Token));
			}
		}
		finally
		{
			await stop.CancelAsync();
			await writer;
		}

		reads.Should().AllSatisfy(read => read.IsSuccess.Should().BeTrue());
		reads[^1].Value.Columns.Sum(column => column.Count).Should().BePositive();
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private void Store(params ReceivedFrame[] frames)
	{
		using var database = SpectrumDatabase.Open(_path).Value;
		database.Write(frames).IsSuccess.Should().BeTrue();
	}

	private async Task<byte?> SeqAtAsync(DateTimeOffset moment)
	{
		var result = await _history.SpectrumAtAsync(moment, Token);
		result.IsSuccess.Should().BeTrue();
		return result.Value?.Seq;
	}

	private async Task<IntensitySeries> SeriesAsync(ushort nm, byte delta, int columns)
	{
		var result = await _history.IntensitySeriesAsync(Query(nm, delta, _start, _start.AddSeconds(10), columns), Token);
		result.IsSuccess.Should().BeTrue();
		return result.Value;
	}

	private static IntensityQuery Query(ushort nm, byte delta, DateTimeOffset from, DateTimeOffset to, int columns)
	{
		return IntensityQuery.Create(nm, delta, from, to, columns).Value;
	}

	/// <summary>An ATP-8000 sample spectrum whose every pixel is the ramp plus the offset.</summary>
	private static ReceivedFrame Offset(DateTimeOffset receivedAt, ushort offset)
	{
		var pixels = ProtocolSamples.Ramp(Spectrometer.Atp8000);
		for (var i = 0; i < pixels.Length; i++)
		{
			pixels[i] += offset;
		}

		return StoreSamples.Spectrum(receivedAt, Spectrometer.Atp8000, pixels);
	}

	private static double RampMean(Spectrometer device, ushort nm, byte delta)
	{
		var window = SpectrometerCalibration.For(device).Window(nm, delta);
		return SpectrumIntensity.Mean(ProtocolSamples.Ramp(device), window);
	}
}
