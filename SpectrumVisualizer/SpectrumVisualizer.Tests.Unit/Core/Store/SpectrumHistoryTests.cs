using System.Diagnostics;
using System.Runtime.InteropServices;

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
	private const int TestTimeoutMs = 30_000;

	private static readonly DateTimeOffset _start = StoreSamples.Start;

	// The busy timeout is 2 s; a read or a commit that waited on a lock would take at least that long.
	private static readonly TimeSpan _wellUnderTheBusyTimeout = TimeSpan.FromSeconds(1);

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
	public async Task ANaNAmplitudeAndQReadBackAsNaN()
	{
		var measurement = new SpectrumMeasurement(
			Spectrometer.Atp8000, 1310, 5, float.NaN, 1f, float.NaN, ProtocolSamples.Ramp(Spectrometer.Atp8000));
		Store(new ReceivedFrame(new BoardFrame(BoardState.Atp8000Connected, 1, measurement), _start));

		var stored = (await _history.SpectrumAtAsync(_start, Token)).Value!;

		float.IsNaN(stored.Measurement.Amplitude).Should().BeTrue();
		stored.Measurement.Snr.Should().Be(1f);
		float.IsNaN(stored.Measurement.Q).Should().BeTrue();
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
	public async Task ARowInsideTheLastMillisecondBeforeToIsInTheSeries()
	{
		var stamp = _start.AddSeconds(5);
		Store(StoreSamples.Spectrum(stamp));
		var query = Query(StoreSamples.SampleNm, StoreSamples.SampleDelta, _start, stamp.AddTicks(TimeSpan.TicksPerMillisecond / 2), 1);

		var series = (await _history.IntensitySeriesAsync(query, Token)).Value;

		series.Columns[0].Count.Should().Be(1, "the row's stamp lies before To");
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
	public async Task TheSeriesOfAWavelengthNeitherDeviceCoversLeavesEveryColumnUncovered()
	{
		const ushort BeyondBothTables = 3000;
		Store(
			StoreSamples.Spectrum(_start.AddSeconds(1), Spectrometer.Atp2000, nm: 650),
			StoreSamples.Spectrum(_start.AddSeconds(5), Spectrometer.Atp8000));

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
		Store(StoreSamples.Spectrum(_start.AddSeconds(-1)), StoreSamples.Spectrum(_start.AddSeconds(10)));

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
		await File.WriteAllBytesAsync(_path, [], Token);

		var result = await _history.StoredRangeAsync(Token);

		result.IsFailed.Should().BeTrue();
		result.Errors.Should().ContainSingle().Which.Should().BeOfType<ExceptionalError>()
			.Which.Exception.Should().BeOfType<SqliteException>();
	}

	[Fact]
	public async Task AReadOfAMissingFileFailsAndCreatesNoFile()
	{
		var result = await _history.StoredRangeAsync(Token);

		result.IsFailed.Should().BeTrue();
		result.Errors.Should().ContainSingle().Which.Should().BeOfType<ExceptionalError>()
			.Which.Exception.Should().BeOfType<SqliteException>();
		File.Exists(_path).Should().BeFalse();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task TheHistoryReadsBesideAnOpenWriteTransaction()
	{
		Store(StoreSamples.Spectrum(_start.AddSeconds(1)));
		using var writer = StoreConnection.Open(_path);
		Execute(writer, "BEGIN IMMEDIATE");
		Execute(writer, "DELETE FROM spectrum");
		var stopwatch = Stopwatch.StartNew();

		var series = await SeriesAsync(StoreSamples.SampleNm, StoreSamples.SampleDelta, columns: 1);

		stopwatch.Elapsed.Should().BeLessThan(_wellUnderTheBusyTimeout);
		series.Columns[0].Count.Should().Be(1, "the read sees the last commit, not the open transaction");
		Execute(writer, "ROLLBACK");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task TheWriterCommitsBesideAnOpenReadSnapshot()
	{
		using var database = SpectrumDatabase.Open(_path).Value;
		database.Write([StoreSamples.Spectrum(_start)]).IsSuccess.Should().BeTrue();
		using var reader = StoreConnection.OpenReader(_path);
		Execute(reader, "BEGIN");
		Execute(reader, "SELECT count(*) FROM spectrum");
		var stopwatch = Stopwatch.StartNew();

		var written = await Task.Run(() => database.Write([StoreSamples.Spectrum(_start.AddSeconds(1))]), Token);

		stopwatch.Elapsed.Should().BeLessThan(_wellUnderTheBusyTimeout);
		written.IsSuccess.Should().BeTrue("WAL lets the writer commit while a reader holds its snapshot");
		Execute(reader, "COMMIT");
		(await _history.StoredRangeAsync(Token)).Value.Should().Be(new StoredRange(_start, _start.AddSeconds(1)));
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
		var pixels = ProtocolSamples.Ramp(device).AsSpan(window.First, window.Last - window.First + 1);
		return QueryWindows.MeanOf(MemoryMarshal.AsBytes(pixels));
	}

	private static void Execute(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}
}
