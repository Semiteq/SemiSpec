using System.Runtime.InteropServices;

using FluentResults;

using Microsoft.Data.Sqlite;

using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.Core.Store;

/// <summary>The writer connection of a spectrum store file: schema, batched frame writes, retention and the closing checkpoint.</summary>
public sealed class SpectrumDatabase : IDisposable
{
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
			intensity REAL,
			pixels BLOB NOT NULL);
		CREATE INDEX spectrum_time ON spectrum (received_at, device, wavelength_nm, delta, intensity);
		CREATE TABLE heartbeat (
			id INTEGER PRIMARY KEY,
			received_at INTEGER NOT NULL,
			seq INTEGER NOT NULL,
			state INTEGER NOT NULL);
		CREATE INDEX heartbeat_time ON heartbeat (received_at);
		""";

	private const string InsertSpectrum = """
		INSERT INTO spectrum (received_at, device, wavelength_nm, delta, amplitude, snr, q, seq, state, intensity, pixels)
		VALUES ($received_at, $device, $wavelength_nm, $delta, $amplitude, $snr, $q, $seq, $state, $intensity, $pixels)
		""";

	private const string InsertHeartbeat = """
		INSERT INTO heartbeat (received_at, seq, state) VALUES ($received_at, $seq, $state)
		""";

	private const string DeleteOlder = """
		DELETE FROM spectrum WHERE received_at < $cutoff;
		DELETE FROM heartbeat WHERE received_at < $cutoff;
		""";

	private readonly SqliteConnection _connection;
	private readonly SqliteCommand _insertSpectrum;
	private readonly SqliteCommand _insertHeartbeat;
	private readonly byte[] _atp2000Pixels = new byte[sizeof(ushort) * Spectrometer.Atp2000.PixelCount];
	private readonly byte[] _atp8000Pixels = new byte[sizeof(ushort) * Spectrometer.Atp8000.PixelCount];

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
			("$intensity", SqliteType.Real),
			("$pixels", SqliteType.Blob));
		_insertHeartbeat = CreateCommand(
			InsertHeartbeat,
			("$received_at", SqliteType.Integer),
			("$seq", SqliteType.Integer),
			("$state", SqliteType.Integer));
	}

	/// <summary>Opens a fresh store file and creates the schema in it.</summary>
	public static Result<SpectrumDatabase> Open(string path)
	{
		SqliteConnection? connection = null;
		try
		{
			connection = StoreConnection.Open(path);
			using var command = connection.CreateCommand();
			command.CommandText = Schema;
			command.ExecuteNonQuery();
			return new SpectrumDatabase(connection);
		}
		catch (Exception ex) when (ex is SqliteException or IOException)
		{
			connection?.Dispose();
			return Result.Fail(new ExceptionalError($"The spectrum store file '{path}' could not be opened", ex));
		}
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

	/// <summary>Deletes the spectra and heartbeats received before the cutoff, in one transaction.</summary>
	public Result DeleteBefore(DateTimeOffset cutoff)
	{
		try
		{
			using var transaction = _connection.BeginTransaction(deferred: false);
			using var command = _connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = DeleteOlder;
			command.Parameters.AddWithValue("$cutoff", cutoff.ToUnixTimeMilliseconds());
			command.ExecuteNonQuery();
			transaction.Commit();
			return Result.Ok();
		}
		catch (Exception ex) when (ex is SqliteException or IOException)
		{
			return Result.Fail(new ExceptionalError($"The rows received before {cutoff:O} could not be deleted", ex));
		}
	}

	/// <summary>Moves the WAL into the file and truncates it, then closes the connection whatever the outcome.</summary>
	public Result<WalCheckpoint> CheckpointAndClose()
	{
		try
		{
			using var command = _connection.CreateCommand();
			command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
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

	/// <summary>Closes the connection without a checkpoint.</summary>
	public void Dispose()
	{
		_insertSpectrum.Dispose();
		_insertHeartbeat.Dispose();
		_connection.Dispose();
	}

	private void WriteSpectrum(ReceivedFrame received, SpectrumMeasurement measurement)
	{
		var pixels = PixelBufferFor(measurement.Device);
		MemoryMarshal.AsBytes(measurement.Pixels.AsSpan()).CopyTo(pixels);
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
		parameters["$intensity"].Value = (object?)SpectrumIntensity.OfOwnWindow(measurement) ?? DBNull.Value;
		parameters["$pixels"].Value = pixels;
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

	private byte[] PixelBufferFor(Spectrometer device)
	{
		return device switch
		{
			Spectrometer.Atp2000 => _atp2000Pixels,
			Spectrometer.Atp8000 => _atp8000Pixels,
			_ => throw new ArgumentOutOfRangeException(nameof(device), device, "Unknown spectrometer."),
		};
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

/// <summary>The outcome of the closing checkpoint: Busy when a reader's snapshot kept the WAL from being truncated.</summary>
public enum WalCheckpoint
{
	Truncated,
	Busy,
}
