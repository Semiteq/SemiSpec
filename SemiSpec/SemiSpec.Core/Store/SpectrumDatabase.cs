using System.Globalization;
using System.Runtime.InteropServices;

using FluentResults;

using Microsoft.Data.Sqlite;

using SemiSpec.Core.Protocol;

namespace SemiSpec.Core.Store;

/// <summary>
/// The writer connection of a spectrum store file: schema, batched frame writes, retention and the closing checkpoint.
/// </summary>
public sealed class SpectrumDatabase : IDisposable
{
	/// <summary>The schema version this build creates and uses, kept in the file's PRAGMA user_version.</summary>
	public const int SchemaVersion = 1;

	private const string SchemaState = """
		SELECT (SELECT user_version FROM pragma_user_version), (SELECT count(*) FROM sqlite_master WHERE type = 'table')
		""";

	private const string Schema = """
		CREATE TABLE spectrum (
			id INTEGER PRIMARY KEY,
			received_at INTEGER NOT NULL,
			device INTEGER NOT NULL,
			wavelength_nm INTEGER NOT NULL,
			delta INTEGER NOT NULL,
			amplitude REAL,
			snr REAL,
			q REAL,
			seq INTEGER NOT NULL,
			state INTEGER NOT NULL,
			pixels BLOB NOT NULL);
		CREATE INDEX spectrum_time ON spectrum (received_at);
		CREATE TABLE heartbeat (
			id INTEGER PRIMARY KEY,
			received_at INTEGER NOT NULL,
			seq INTEGER NOT NULL,
			state INTEGER NOT NULL);
		CREATE INDEX heartbeat_time ON heartbeat (received_at);
		""";

	private const string InsertSpectrum = """
		INSERT INTO spectrum (received_at, device, wavelength_nm, delta, amplitude, snr, q, seq, state, pixels)
		VALUES ($received_at, $device, $wavelength_nm, $delta, $amplitude, $snr, $q, $seq, $state, $pixels)
		""";

	private const string InsertHeartbeat = """
		INSERT INTO heartbeat (received_at, seq, state) VALUES ($received_at, $seq, $state)
		""";

	private const string NewestReceivedAt = """
		SELECT (SELECT max(received_at) FROM spectrum), (SELECT max(received_at) FROM heartbeat)
		""";

	private const string DeleteOlder = """
		DELETE FROM spectrum WHERE received_at < $cutoff;
		DELETE FROM heartbeat WHERE received_at < $cutoff;
		""";

	// A DELETE without WHERE on a table without triggers takes SQLite's truncate optimisation and frees whole pages.
	private const string DeleteAll = """
		DELETE FROM spectrum;
		DELETE FROM heartbeat;
		""";

	// Microsoft.Data.Sqlite counts the busy timeout in whole seconds and waits forever on 0, so 1 s is the shortest.
	private const int ShortBusyTimeoutSeconds = 1;

	private readonly SqliteConnection _connection;
	private readonly SqliteCommand _insertSpectrum;
	private readonly SqliteCommand _insertHeartbeat;

	private SpectrumDatabase(SqliteConnection connection)
	{
		_connection = connection;
		_insertSpectrum = CreateCommand(
			InsertSpectrum,
			("$received_at", SqliteType.Integer),
			("$device", SqliteType.Integer),
			("$wavelength_nm", SqliteType.Integer),
			("$delta", SqliteType.Integer),
			("$amplitude", SqliteType.Real),
			("$snr", SqliteType.Real),
			("$q", SqliteType.Real),
			("$seq", SqliteType.Integer),
			("$state", SqliteType.Integer),
			("$pixels", SqliteType.Blob));
		_insertHeartbeat = CreateCommand(
			InsertHeartbeat,
			("$received_at", SqliteType.Integer),
			("$seq", SqliteType.Integer),
			("$state", SqliteType.Integer));
	}

	/// <summary>
	/// Opens or creates the store file; a file of another version fails with StoreSchemaMismatchError, its version and
	/// tables untouched, and one SQLite calls not a database or corrupt with StoreFileUnreadableError; both close it.
	/// </summary>
	public static Result<SpectrumDatabase> Open(string path)
	{
		SqliteConnection? connection = null;
		try
		{
			connection = StoreConnection.Open(path);
			var schema = UseOrCreateSchema(connection);
			if (schema.IsFailed)
			{
				connection.Dispose();
				return schema;
			}

			return new SpectrumDatabase(connection);
		}
		catch (SqliteException ex) when (StoreDamage.IsDamage(ex))
		{
			connection?.Dispose();
			return Result.Fail(new StoreFileUnreadableError(ex));
		}
		catch (Exception ex) when (ex is SqliteException or IOException)
		{
			connection?.Dispose();
			return Result.Fail(new ExceptionalError("SQLite refused the file or the schema", ex));
		}
	}

	private static Result UseOrCreateSchema(SqliteConnection connection)
	{
		using var transaction = connection.BeginTransaction(deferred: false);
		using var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = SchemaState;
		var (version, tables) = ReadSchemaState(command);
		if (version == 0 && tables == 0)
		{
			command.CommandText = Schema
				+ string.Create(CultureInfo.InvariantCulture, $"PRAGMA user_version = {SchemaVersion};");
			command.ExecuteNonQuery();
		}
		else if (version != SchemaVersion)
		{
			return Result.Fail(new StoreSchemaMismatchError(version));
		}

		transaction.Commit();
		return Result.Ok();
	}

	private static (int Version, long Tables) ReadSchemaState(SqliteCommand schemaState)
	{
		using var reader = schemaState.ExecuteReader();
		reader.Read();
		return (reader.GetInt32(0), reader.GetInt64(1));
	}

	/// <summary>Writes the frames in one transaction: all of them or, on failure, none.</summary>
	public Result Write(IReadOnlyList<ReceivedFrame> frames)
	{
		try
		{
			using var transaction = _connection.BeginTransaction(deferred: false);
			_insertSpectrum.Transaction = transaction;
			_insertHeartbeat.Transaction = transaction;
			foreach (var received in frames)
			{
				if (received.Frame.Measurement is { } measurement)
				{
					WriteSpectrum(received, measurement);
				}
				else
				{
					WriteHeartbeat(received);
				}
			}

			transaction.Commit();
			return Result.Ok();
		}
		catch (Exception ex) when (ex is SqliteException or IOException)
		{
			return Result.Fail(new ExceptionalError($"A batch of {frames.Count} frames could not be written", ex));
		}
	}

	/// <summary>
	/// Deletes the spectra and heartbeats received before the cutoff in one transaction; empties both tables when every
	/// row has expired.
	/// </summary>
	public Result DeleteBefore(DateTimeOffset cutoff)
	{
		try
		{
			var cutoffMs = cutoff.ToUnixTimeMilliseconds();
			using var transaction = _connection.BeginTransaction(deferred: false);
			using var command = _connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = NewestReceivedAt;
			if (EveryRowIsBefore(command, cutoffMs))
			{
				command.CommandText = DeleteAll;
			}
			else
			{
				command.CommandText = DeleteOlder;
				command.Parameters.AddWithValue("$cutoff", cutoffMs);
			}

			command.ExecuteNonQuery();
			transaction.Commit();
			return Result.Ok();
		}
		catch (Exception ex) when (ex is SqliteException or IOException)
		{
			return Result.Fail(new ExceptionalError($"The rows received before {cutoff:O} could not be deleted", ex));
		}
	}

	/// <summary>
	/// Moves the WAL into the file and truncates it, then closes the connection whatever the outcome.
	/// </summary>
	public Result<WalCheckpoint> CheckpointAndClose()
	{
		try
		{
			using var command = _connection.CreateCommand();
			command.CommandText = StoreConnection.CheckpointTruncate;
			using var reader = command.ExecuteReader();
			reader.Read();
			var busy = reader.GetInt32(0) != 0;
			return busy ? WalCheckpoint.Busy : WalCheckpoint.Truncated;
		}
		catch (Exception ex) when (ex is SqliteException or IOException)
		{
			return Result.Fail(new ExceptionalError("The spectrum store WAL could not be checkpointed", ex));
		}
		finally
		{
			Dispose();
		}
	}

	/// <summary>Makes every later statement give up on a held lock after 1 s instead of the default 2 s.</summary>
	public void ShortenBusyTimeout()
	{
		_connection.DefaultTimeout = ShortBusyTimeoutSeconds;
		_insertSpectrum.CommandTimeout = ShortBusyTimeoutSeconds;
		_insertHeartbeat.CommandTimeout = ShortBusyTimeoutSeconds;
	}

	/// <summary>Closes the connection without a checkpoint.</summary>
	public void Dispose()
	{
		_insertSpectrum.Dispose();
		_insertHeartbeat.Dispose();
		_connection.Dispose();
	}

	private void WriteSpectrum(ReceivedFrame received, SpectrumMeasurement measurement)
	{
		var parameters = _insertSpectrum.Parameters;
		parameters["$received_at"].Value = received.ReceivedAt.ToUnixTimeMilliseconds();
		parameters["$device"].Value = (byte)measurement.Device;
		parameters["$wavelength_nm"].Value = measurement.WavelengthNm;
		parameters["$delta"].Value = measurement.Delta;
		parameters["$amplitude"].Value = NullForNaN(measurement.Amplitude);
		parameters["$snr"].Value = NullForNaN(measurement.Snr);
		parameters["$q"].Value = NullForNaN(measurement.Q);
		parameters["$seq"].Value = received.Frame.Seq;
		parameters["$state"].Value = (byte)received.Frame.State;
		parameters["$pixels"].Value = MemoryMarshal.AsBytes(measurement.Pixels.AsSpan()).ToArray();
		_insertSpectrum.ExecuteNonQuery();
	}

	private void WriteHeartbeat(ReceivedFrame received)
	{
		var parameters = _insertHeartbeat.Parameters;
		parameters["$received_at"].Value = received.ReceivedAt.ToUnixTimeMilliseconds();
		parameters["$seq"].Value = received.Frame.Seq;
		parameters["$state"].Value = (byte)received.Frame.State;
		_insertHeartbeat.ExecuteNonQuery();
	}

	private static bool EveryRowIsBefore(SqliteCommand newestReceivedAt, long cutoffMs)
	{
		using var reader = newestReceivedAt.ExecuteReader();
		reader.Read();
		return IsEmptyOrBefore(reader, 0, cutoffMs) && IsEmptyOrBefore(reader, 1, cutoffMs);
	}

	private static bool IsEmptyOrBefore(SqliteDataReader newest, int table, long cutoffMs)
	{
		return newest.IsDBNull(table) || newest.GetInt64(table) < cutoffMs;
	}

	private static object NullForNaN(float value)
	{
		return float.IsNaN(value) ? DBNull.Value : (double)value;
	}

	private SqliteCommand CreateCommand(string sql, params (string Name, SqliteType Type)[] parameters)
	{
		var command = _connection.CreateCommand();
		command.CommandText = sql;
		foreach (var (name, type) in parameters)
		{
			command.Parameters.Add(name, type);
		}

		return command;
	}
}

/// <summary>
/// The outcome of the closing checkpoint: Busy when a reader's snapshot kept the WAL from being truncated.
/// </summary>
public enum WalCheckpoint
{
	Truncated,
	Busy,
}
