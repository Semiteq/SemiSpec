using System.Diagnostics;
using System.Runtime.InteropServices;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Data.Sqlite;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Store;
using SemiSpec.Tests.Unit.Core.Protocol;

using Xunit;

namespace SemiSpec.Tests.Unit.Core.Store;

[Trait("Component", "Core")]
[Trait("Area", "Store")]
[Trait("Category", "Unit")]
public sealed class SpectrumDatabaseTests : IDisposable
{
	private readonly TempFolder _folder = new();
	private readonly string _path;

	public SpectrumDatabaseTests()
	{
		_path = _folder.FileIn("spectra-1.db");
	}

	public void Dispose()
	{
		_folder.Dispose();
	}

	[Fact]
	public void OpenSetsWalNormalSyncAndTheJournalLimit()
	{
		using (OpenDatabase())
		{
			using var plain = OpenPlain();
			Scalar(plain, "PRAGMA journal_mode").Should().Be("wal");
		}

		using var connection = StoreConnection.Open(_path);
		Scalar(connection, "PRAGMA journal_mode").Should().Be("wal");
		Scalar(connection, "PRAGMA synchronous").Should().Be(1L);
		Scalar(connection, "PRAGMA journal_size_limit").Should().Be(67108864L);
	}

	[Fact]
	public void ASpectrumRoundTripsEveryField()
	{
		var receivedAt = StoreSamples.Start.AddTicks(1234567);
		var frame = StoreSamples.Spectrum(receivedAt, Spectrometer.Atp2000, nm: 650, delta: 10, seq: 42);
		var measurement = frame.Frame.Measurement!;
		using var database = OpenDatabase();

		database.Write([frame]).IsSuccess.Should().BeTrue();

		using var plain = OpenPlain();
		using var reader = Query(
			plain,
			"SELECT received_at, device, wavelength_nm, delta, amplitude, snr, q, seq, state, pixels FROM spectrum");
		reader.Read().Should().BeTrue();
		reader.GetInt64(0).Should().Be(receivedAt.ToUnixTimeMilliseconds());
		DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0))
			.Should().Be(StoreSamples.Start.AddMilliseconds(123));
		reader.GetByte(1).Should().Be((byte)Spectrometer.Atp2000);
		reader.GetInt32(2).Should().Be(650);
		reader.GetByte(3).Should().Be(10);
		reader.GetFloat(4).Should().Be(measurement.Amplitude);
		reader.GetFloat(5).Should().Be(measurement.Snr);
		reader.GetFloat(6).Should().Be(measurement.Q);
		reader.GetByte(7).Should().Be(42);
		((BoardState)reader.GetByte(8)).Should().Be(frame.Frame.State);
		MemoryMarshal.Cast<byte, ushort>((byte[])reader.GetValue(9)).ToArray().Should().Equal(measurement.Pixels);
		reader.Read().Should().BeFalse();
	}

	[Fact]
	public void NaNFloatsAreStoredAsNull()
	{
		var pixels = ProtocolSamples.Ramp(Spectrometer.Atp8000);
		var measurement = new SpectrumMeasurement(Spectrometer.Atp8000, 1310, 5, float.NaN, 1f, float.NaN, pixels);
		var frame = new ReceivedFrame(new BoardFrame(BoardState.Atp8000Connected, 1, measurement), StoreSamples.Start);
		using var database = OpenDatabase();

		database.Write([frame]).IsSuccess.Should().BeTrue();

		using var plain = OpenPlain();
		using var reader = Query(plain, "SELECT amplitude, snr, q FROM spectrum");
		reader.Read().Should().BeTrue();
		reader.IsDBNull(0).Should().BeTrue();
		reader.GetFloat(1).Should().Be(1f);
		reader.IsDBNull(2).Should().BeTrue();
	}

	[Fact]
	public void AHeartbeatGoesToTheHeartbeatTable()
	{
		var frame = StoreSamples.Heartbeat(StoreSamples.Start, seq: 7);
		using var database = OpenDatabase();

		database.Write([frame]).IsSuccess.Should().BeTrue();

		using var plain = OpenPlain();
		Scalar(plain, "SELECT count(*) FROM spectrum").Should().Be(0L);
		using var reader = Query(plain, "SELECT received_at, seq, state FROM heartbeat");
		reader.Read().Should().BeTrue();
		reader.GetInt64(0).Should().Be(StoreSamples.Start.ToUnixTimeMilliseconds());
		reader.GetByte(1).Should().Be(7);
		((BoardState)reader.GetByte(2)).Should().Be(frame.Frame.State);
		reader.Read().Should().BeFalse();
	}

	[Fact]
	public void ABatchIsOneTransaction()
	{
		using var database = OpenDatabase();
		using (var plain = OpenPlain())
		{
			Execute(
				plain,
				"CREATE TRIGGER fail_seq_99 BEFORE INSERT ON spectrum WHEN NEW.seq = 99 "
				+ "BEGIN SELECT RAISE(ABORT, 'test'); END");
		}

		var result = database.Write(
		[
			StoreSamples.Spectrum(StoreSamples.Start, seq: 1),
			StoreSamples.Heartbeat(StoreSamples.Start, seq: 2),
			StoreSamples.Spectrum(StoreSamples.Start, seq: 99),
		]);

		result.IsFailed.Should().BeTrue();
		result.Errors.Should().ContainSingle().Which.Should().BeOfType<ExceptionalError>();
		using var check = OpenPlain();
		Scalar(check, "SELECT count(*) FROM spectrum").Should().Be(0L);
		Scalar(check, "SELECT count(*) FROM heartbeat").Should().Be(0L);
	}

	[Fact]
	public void RetentionDeletesRowsOlderThanTheCutoffFromBothTables()
	{
		var cutoff = StoreSamples.Start;
		using var database = OpenDatabase();
		database.Write(
		[
			StoreSamples.Spectrum(cutoff.AddHours(-1), seq: 1),
			StoreSamples.Heartbeat(cutoff.AddMilliseconds(-1), seq: 2),
			StoreSamples.Spectrum(cutoff, seq: 3),
			StoreSamples.Heartbeat(cutoff.AddHours(1), seq: 4),
		]).IsSuccess.Should().BeTrue();

		database.DeleteBefore(cutoff).IsSuccess.Should().BeTrue();

		using var plain = OpenPlain();
		Scalar(plain, "SELECT group_concat(seq) FROM spectrum").Should().Be("3");
		Scalar(plain, "SELECT group_concat(seq) FROM heartbeat").Should().Be("4");
	}

	[Fact]
	public void DeleteBeforeACutoffPastEveryRowEmptiesBothTables()
	{
		var cutoff = StoreSamples.Start;
		using var database = OpenDatabase();
		database.Write(
		[
			StoreSamples.Spectrum(cutoff.AddHours(-2), seq: 1),
			StoreSamples.Heartbeat(cutoff.AddHours(-1), seq: 2),
			StoreSamples.Spectrum(cutoff.AddMilliseconds(-1), seq: 3),
		]).IsSuccess.Should().BeTrue();

		database.DeleteBefore(cutoff).IsSuccess.Should().BeTrue();

		using var plain = OpenPlain();
		Scalar(plain, "SELECT count(*) FROM spectrum").Should().Be(0L);
		Scalar(plain, "SELECT count(*) FROM heartbeat").Should().Be(0L);
	}

	[Fact]
	public void DeleteBeforeACutoffInsideTheRowsKeepsTheNewerRows()
	{
		var cutoff = StoreSamples.Start;
		using var database = OpenDatabase();
		database.Write(
		[
			StoreSamples.Spectrum(cutoff.AddHours(-2), seq: 1),
			StoreSamples.Spectrum(cutoff.AddMilliseconds(-1), seq: 2),
			StoreSamples.Heartbeat(cutoff.AddHours(-1), seq: 3),
			StoreSamples.Heartbeat(cutoff, seq: 4),
		]).IsSuccess.Should().BeTrue();

		database.DeleteBefore(cutoff).IsSuccess.Should().BeTrue();

		using var plain = OpenPlain();
		Scalar(plain, "SELECT count(*) FROM spectrum").Should().Be(0L, "every spectrum is older than the cutoff");
		Scalar(plain, "SELECT group_concat(seq) FROM heartbeat").Should().Be("4",
			"one newer heartbeat keeps the row-wise delete for both tables");
	}

	[Fact]
	public void DeleteBeforeOnAnEmptyFileSucceeds()
	{
		using var database = OpenDatabase();

		database.DeleteBefore(StoreSamples.Start).IsSuccess.Should().BeTrue();

		database.Write([StoreSamples.Spectrum(StoreSamples.Start)]).IsSuccess.Should().BeTrue();
		using var plain = OpenPlain();
		Scalar(plain, "SELECT count(*) FROM spectrum").Should().Be(1L);
	}

	[Fact]
	public void CheckpointTruncatesTheWal()
	{
		using var database = OpenDatabase();
		database.Write([StoreSamples.Spectrum(StoreSamples.Start)]).IsSuccess.Should().BeTrue();
		using var idle = OpenPlain();
		Scalar(idle, "SELECT count(*) FROM spectrum").Should().Be(1L);
		new FileInfo(_path + "-wal").Length.Should().BePositive();

		var result = database.CheckpointAndClose();

		result.Value.Should().Be(WalCheckpoint.Truncated);
		new FileInfo(_path + "-wal").Length.Should().Be(0);
	}

	[Fact]
	public void AWriteAgainstAHeldLockFailsAfterTheDefaultTimeout()
	{
		using var database = OpenDatabase();
		using var holder = OpenPlain();
		Execute(holder, "BEGIN IMMEDIATE");
		var stopwatch = Stopwatch.StartNew();

		var result = database.Write([StoreSamples.Spectrum(StoreSamples.Start)]);

		stopwatch.Stop();
		result.IsFailed.Should().BeTrue();
		stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1.9))
			.And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(2.5));
		Execute(holder, "ROLLBACK");
	}

	[Fact]
	public void AfterShortenBusyTimeoutAWriteAgainstAHeldLockFailsAfterOneSecond()
	{
		using var database = OpenDatabase();
		using var holder = OpenPlain();
		Execute(holder, "BEGIN IMMEDIATE");
		database.ShortenBusyTimeout();
		var stopwatch = Stopwatch.StartNew();

		var result = database.Write([StoreSamples.Spectrum(StoreSamples.Start)]);

		stopwatch.Stop();
		result.IsFailed.Should().BeTrue();
		stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(0.9))
			.And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(1.5));
		Execute(holder, "ROLLBACK");
	}

	[Fact]
	public void ACheckpointBesideAnOpenReaderReturnsBusyAtOnce()
	{
		using var database = OpenDatabase();
		database.Write([StoreSamples.Spectrum(StoreSamples.Start)]).IsSuccess.Should().BeTrue();
		using (var reader = OpenPlain())
		{
			Execute(reader, "BEGIN");
			Scalar(reader, "SELECT count(*) FROM spectrum").Should().Be(1L);
			database.Write([StoreSamples.Spectrum(StoreSamples.Start.AddSeconds(1))]).IsSuccess.Should().BeTrue();
			var stopwatch = Stopwatch.StartNew();

			var result = database.CheckpointAndClose();

			stopwatch.Stop();
			result.Value.Should().Be(WalCheckpoint.Busy);
			stopwatch.Elapsed.Should().BeLessThan(
				TimeSpan.FromSeconds(1), "the checkpoint does not wait out the 2 s busy timeout");
			Execute(reader, "COMMIT");
		}

		var deleteTheFile = () => File.Delete(_path);
		deleteTheFile.Should().NotThrow("the checkpoint closed the writer connection");
	}

	private SpectrumDatabase OpenDatabase()
	{
		var result = SpectrumDatabase.Open(_path);
		result.IsSuccess.Should().BeTrue();
		return result.Value;
	}

	private SqliteConnection OpenPlain()
	{
		var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
		connection.Open();
		return connection;
	}

	private static object? Scalar(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return command.ExecuteScalar();
	}

	private static SqliteDataReader Query(SqliteConnection connection, string sql)
	{
		var command = connection.CreateCommand();
		command.CommandText = sql;
		return command.ExecuteReader();
	}

	private static void Execute(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}
}
