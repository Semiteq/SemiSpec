using System.Runtime.InteropServices;

using FluentResults;

using Microsoft.Data.Sqlite;

using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.Core.Store;

/// <summary>
/// Reads a spectrum store file: a stored spectrum, the intensity series and the stored range; each call opens its own
/// reader connection on the thread pool.
/// </summary>
public sealed class SpectrumHistory(string path)
{
	/// <summary>How many rows a series read takes between two checks of its token.</summary>
	public const int RowsPerCancellationCheck = 256;

	/// <summary>The rows of a series: received inside [$from, $to) of an <see cref="IntensityQuery"/>.</summary>
	public const string InQueryRange = "received_at >= $from AND received_at < $to";

	private const string NearestAtOrBefore = """
		SELECT received_at, device, wavelength_nm, delta, amplitude, snr, q, seq, state, pixels
		FROM spectrum WHERE received_at <= $moment
		ORDER BY received_at DESC, id DESC LIMIT 1
		""";

	private const string Range = "SELECT min(received_at), max(received_at) FROM spectrum";

	/// <summary>
	/// The spectrum with the latest receive time at or before the moment, ties to the later row; null before the first.
	/// </summary>
	public Task<Result<StoredSpectrum?>> SpectrumAtAsync(DateTimeOffset moment, CancellationToken token)
	{
		return ReadAsync(
			connection =>
			{
				using var command = connection.CreateCommand();
				command.CommandText = NearestAtOrBefore;
				command.Parameters.AddWithValue("$moment", moment.ToUnixTimeMilliseconds());
				using var reader = command.ExecuteReader();
				return reader.Read() ? ToStoredSpectrum(reader) : null;
			},
			"The stored spectrum could not be read",
			token);
	}

	public Task<Result<IntensitySeries>> IntensitySeriesAsync(IntensityQuery query, CancellationToken token)
	{
		return ReadAsync(
			connection => ReadSeries(connection, query, token),
			"The intensity series could not be read",
			token);
	}

	/// <summary>The receive times of the first and last stored spectrum; null when none is stored.</summary>
	public Task<Result<StoredRange?>> StoredRangeAsync(CancellationToken token)
	{
		return ReadAsync(
			connection =>
			{
				using var command = connection.CreateCommand();
				command.CommandText = Range;
				using var reader = command.ExecuteReader();
				reader.Read();
				return reader.IsDBNull(0)
					? null
					: new StoredRange(
						DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
						DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)));
			},
			"The stored range could not be read",
			token);
	}

	/// <summary>
	/// The series in one statement over the range: every row's window cut from its pixels with substr.
	/// </summary>
	public static IntensitySeries ReadSeries(SqliteConnection connection, IntensityQuery query, CancellationToken token)
	{
		var buckets = new IntensityBuckets(query);
		var windows = new QueryWindows(query.WavelengthNm, query.Delta);
		var atp2000 = windows.For(Spectrometer.Atp2000);
		var atp8000 = windows.For(Spectrometer.Atp8000);
		var (atp2000Start, atp2000Count) = (QueryWindows.ByteStart(atp2000), QueryWindows.ByteCount(atp2000));
		var (atp8000Start, atp8000Count) = (QueryWindows.ByteStart(atp8000), QueryWindows.ByteCount(atp8000));
		using var command = connection.CreateCommand();
		command.CommandText = $"""
			SELECT received_at, device, CASE device
				WHEN {(byte)Spectrometer.Atp2000} THEN substr(pixels, {atp2000Start}, {atp2000Count})
				WHEN {(byte)Spectrometer.Atp8000} THEN substr(pixels, {atp8000Start}, {atp8000Count})
				END
			FROM spectrum WHERE {InQueryRange}
			""";
		command.Parameters.AddWithValue("$from", query.FromMilliseconds);
		command.Parameters.AddWithValue("$to", query.ToMilliseconds);
		using var reader = command.ExecuteReader();
		var buffer = new byte[windows.LargestByteCount];
		var rows = 0;
		while (reader.Read())
		{
			if (++rows % RowsPerCancellationCheck == 0)
			{
				token.ThrowIfCancellationRequested();
			}

			var receivedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0));
			if (!windows.Covers((Spectrometer)reader.GetByte(1)))
			{
				buckets.AddUncovered(receivedAt);
				continue;
			}

			var byteCount = (int)reader.GetBytes(2, 0, buffer, 0, buffer.Length);
			buckets.Add(receivedAt, QueryWindows.MeanOf(buffer.AsSpan(0, byteCount)));
		}

		return buckets.ToSeries();
	}

	private Task<Result<T>> ReadAsync<T>(Func<SqliteConnection, T> read, string failure, CancellationToken token)
	{
		return Task.Run(
			() =>
			{
				try
				{
					using var connection = StoreConnection.OpenReader(path);
					return Result.Ok(read(connection));
				}
				catch (SqliteException ex)
				{
					return Result.Fail<T>(new ExceptionalError(failure, ex));
				}
			},
			token);
	}

	private static StoredSpectrum ToStoredSpectrum(SqliteDataReader reader)
	{
		var pixels = MemoryMarshal.Cast<byte, ushort>((byte[])reader.GetValue(9)).ToArray();
		var measurement = new SpectrumMeasurement(
			(Spectrometer)reader.GetByte(1),
			(ushort)reader.GetInt32(2),
			reader.GetByte(3),
			FloatOrNaN(reader, 4),
			FloatOrNaN(reader, 5),
			FloatOrNaN(reader, 6),
			pixels);
		return new StoredSpectrum(
			measurement,
			DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
			reader.GetByte(7),
			(BoardState)reader.GetByte(8));
	}

	private static float FloatOrNaN(SqliteDataReader reader, int ordinal)
	{
		return reader.IsDBNull(ordinal) ? float.NaN : reader.GetFloat(ordinal);
	}
}

/// <summary>A stored spectrum rebuilt as a new measurement, with the frame fields stored beside it.</summary>
public sealed record StoredSpectrum(
	SpectrumMeasurement Measurement, DateTimeOffset ReceivedAt, byte Seq, BoardState State);

/// <summary>The receive times of the first and last stored spectrum.</summary>
public sealed record StoredRange(DateTimeOffset First, DateTimeOffset Last);
