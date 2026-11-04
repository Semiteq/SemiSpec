using System.Diagnostics;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Data.Sqlite;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Tests.Unit.Core.Protocol;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Store;

/// <summary>The stored spectrum, the stored range, failed reads and reads beside the writer.</summary>
[Trait("Component", "Core")]
[Trait("Area", "Store")]
[Trait("Category", "Unit")]
public sealed class SpectrumHistorySpectrumTests : SpectrumHistoryTestBase
{
	[Fact]
	public async Task SpectrumAtReturnsTheNearestRowAtOrBefore()
	{
		Store(
			StoreSamples.Spectrum(Start, seq: 1),
			StoreSamples.Spectrum(Start.AddSeconds(1), seq: 2),
			StoreSamples.Spectrum(Start.AddSeconds(1), seq: 3),
			StoreSamples.Spectrum(Start.AddSeconds(2), seq: 4));

		(await SeqAtAsync(Start)).Should().Be(1);
		(await SeqAtAsync(Start.AddSeconds(1))).Should().Be(3, "equal stamps resolve to the later row");
		(await SeqAtAsync(Start.AddSeconds(1.5))).Should().Be(3);
		(await SeqAtAsync(Start.AddHours(1))).Should().Be(4);
	}

	[Fact]
	public async Task SpectrumAtBeforeTheFirstRowReturnsNone()
	{
		Store(StoreSamples.Heartbeat(Start.AddSeconds(-5)), StoreSamples.Spectrum(Start));

		var result = await History.SpectrumAtAsync(Start.AddMilliseconds(-1), Token);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeNull();
	}

	[Fact]
	public async Task TheStoredSpectrumRebuildsTheMeasurement()
	{
		var frame = StoreSamples.Spectrum(Start, Spectrometer.Atp2000, nm: 650, delta: 10, seq: 42);
		Store(frame);

		var first = (await History.SpectrumAtAsync(Start, Token)).Value!;
		var second = (await History.SpectrumAtAsync(Start, Token)).Value!;

		first.Measurement.Should().Be(frame.Frame.Measurement);
		first.ReceivedAt.Should().Be(Start);
		first.Seq.Should().Be(42);
		first.State.Should().Be(frame.Frame.State);
		second.Measurement.Pixels.Should().NotBeSameAs(
			first.Measurement.Pixels, "each call rebuilds a new measurement");
	}

	[Fact]
	public async Task ANaNAmplitudeAndQReadBackAsNaN()
	{
		var measurement = new SpectrumMeasurement(
			Spectrometer.Atp8000, 1310, 5, float.NaN, 1f, float.NaN, ProtocolSamples.Ramp(Spectrometer.Atp8000));
		Store(new ReceivedFrame(new BoardFrame(BoardState.Atp8000Connected, 1, measurement), Start));

		var stored = (await History.SpectrumAtAsync(Start, Token)).Value!;

		float.IsNaN(stored.Measurement.Amplitude).Should().BeTrue();
		stored.Measurement.Snr.Should().Be(1f);
		float.IsNaN(stored.Measurement.Q).Should().BeTrue();
	}

	[Fact]
	public async Task TheStoredRangeSpansTheFirstAndLastSpectrum()
	{
		using var database = SpectrumDatabase.Open(StorePath).Value;
		database.Write([StoreSamples.Heartbeat(Start.AddSeconds(-5))]).IsSuccess.Should().BeTrue();
		(await History.StoredRangeAsync(Token)).Value.Should().BeNull();
		database.Write(
		[
			StoreSamples.Spectrum(Start),
			StoreSamples.Spectrum(Start.AddMinutes(30)),
			StoreSamples.Heartbeat(Start.AddHours(1)),
		]).IsSuccess.Should().BeTrue();

		var range = await History.StoredRangeAsync(Token);

		range.Value.Should().Be(new StoredRange(Start, Start.AddMinutes(30)));
	}

	[Fact]
	public async Task AReadOfAFileWithoutTheSchemaFails()
	{
		await File.WriteAllBytesAsync(StorePath, [], Token);

		var result = await History.StoredRangeAsync(Token);

		result.IsFailed.Should().BeTrue();
		result.Errors.Should().ContainSingle().Which.Should().BeOfType<ExceptionalError>()
			.Which.Exception.Should().BeOfType<SqliteException>();
	}

	[Fact]
	public async Task AReadOfAMissingFileFailsAndCreatesNoFile()
	{
		var result = await History.StoredRangeAsync(Token);

		result.IsFailed.Should().BeTrue();
		result.Errors.Should().ContainSingle().Which.Should().BeOfType<ExceptionalError>()
			.Which.Exception.Should().BeOfType<SqliteException>();
		File.Exists(StorePath).Should().BeFalse();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task TheHistoryReadsBesideAnOpenWriteTransaction()
	{
		Store(StoreSamples.Spectrum(Start.AddSeconds(1)));
		using var writer = StoreConnection.Open(StorePath);
		Execute(writer, "BEGIN IMMEDIATE");
		Execute(writer, "DELETE FROM spectrum");
		var stopwatch = Stopwatch.StartNew();

		var series = await SeriesAsync(StoreSamples.SampleNm, StoreSamples.SampleDelta, columns: 1);

		stopwatch.Elapsed.Should().BeLessThan(WellUnderTheBusyTimeout);
		series.Columns[0].Count.Should().Be(1, "the read sees the last commit, not the open transaction");
		Execute(writer, "ROLLBACK");
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task TheWriterCommitsBesideAnOpenReadSnapshot()
	{
		using var database = SpectrumDatabase.Open(StorePath).Value;
		database.Write([StoreSamples.Spectrum(Start)]).IsSuccess.Should().BeTrue();
		using var reader = StoreConnection.OpenReader(StorePath);
		Execute(reader, "BEGIN");
		Execute(reader, "SELECT count(*) FROM spectrum");
		var stopwatch = Stopwatch.StartNew();

		var written = await Task.Run(() => database.Write([StoreSamples.Spectrum(Start.AddSeconds(1))]), Token);

		stopwatch.Elapsed.Should().BeLessThan(WellUnderTheBusyTimeout);
		written.IsSuccess.Should().BeTrue("WAL lets the writer commit while a reader holds its snapshot");
		Execute(reader, "COMMIT");
		(await History.StoredRangeAsync(Token)).Value.Should().Be(new StoredRange(Start, Start.AddSeconds(1)));
	}
}
