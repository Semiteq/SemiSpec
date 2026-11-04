using System.Runtime.InteropServices;

using FluentResults;

using Microsoft.Data.Sqlite;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Trend;

namespace SpectrumVisualizer.Core.Store;

/// <summary>
/// Reads a spectrum store file: a stored spectrum, the intensity series and points and the stored range; each call
/// opens its own reader connection on the thread pool.
/// </summary>
public sealed class SpectrumHistory(string path)
{
	/// <summary>How many rows a window-mean read takes between two checks of its token.</summary>
	private const int RowsPerCancellationCheck = 256;

	private const string NearestAtOrBefore = """
		SELECT id, received_at, device, wavelength_nm, delta, amplitude, snr, q, seq, state, pixels
		FROM spectrum WHERE received_at <= $moment
		ORDER BY received_at DESC, id DESC LIMIT 1
		""";

	private const string Range = "SELECT min(received_at), max(received_at) FROM spectrum";

	/// <summary>
	/// The spectrum with the latest receive time at or before the moment, ties to the later row; null before the first,
	/// and a failure naming the row when that row is corrupt.
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
				return reader.Read() ? ToStoredSpectrum(reader) : Result.Ok<StoredSpectrum?>(null);
			},
			"The stored spectrum could not be read",
			token);
	}

	public Task<Result<IntensitySeries>> IntensitySeriesAsync(IntensityQuery query, CancellationToken token)
	{
		return ReadAsync(
			connection => Result.Ok(ReadSeries(connection, query, token)),
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
				return Result.Ok(reader.IsDBNull(0)
					? null
					: new StoredRange(
						DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
						DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1))));
			},
			"The stored range could not be read",
			token);
	}

	/// <summary>The window mean of every stored spectrum in [from, to), in receive order; NaN for an uncovered device.</summary>
	public Task<Result<IntensityChunk>> IntensityPointsAsync(
		ushort wavelengthNm, byte delta, DateTimeOffset from, DateTimeOffset to, CancellationToken token)
	{
		return ReadAsync(
			connection =>
			{
				var windows = new QueryWindows(wavelengthNm, delta);
				var points = WindowMeans(connection, windows, from, to, token).ToArray();
				return Result.Ok(new IntensityChunk(from, points));
			},
			"The intensity points could not be read",
			token);
	}

	/// <summary>The series folded from the window means of the range; a corrupt row counts as uncovered.</summary>
	public static IntensitySeries ReadSeries(SqliteConnection connection, IntensityQuery query, CancellationToken token)
	{
		var buckets = new IntensityBuckets(query);
		var windows = new QueryWindows(query.WavelengthNm, query.Delta);
		foreach (var point in WindowMeans(connection, windows, query.From, query.To, token))
		{
			var receivedAt = DateTimeOffset.FromUnixTimeMilliseconds(point.UnixMs);
			if (double.IsNaN(point.Value))
			{
				buckets.AddUncovered(receivedAt);
			}
			else
			{
				buckets.Add(receivedAt, point.Value);
			}
		}

		return buckets.ToSeries();
	}

	/// <summary>
	/// Streams one statement over the range: every row's window cut from its pixels with substr and averaged, NaN for
	/// an uncovered device or a corrupt row.
	/// </summary>
	private static IEnumerable<TrendPoint> WindowMeans(
		SqliteConnection connection,
		QueryWindows windows,
		DateTimeOffset from,
		DateTimeOffset to,
		CancellationToken token)
	{
		using var command = connection.CreateCommand();
		command.CommandText = SeriesStatement(windows);
		command.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
		command.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());
		using var reader = command.ExecuteReader();
		var buffer = new byte[windows.LargestByteCount];
		var rows = 0;
		while (reader.Read())
		{
			if (++rows % RowsPerCancellationCheck == 0)
			{
				token.ThrowIfCancellationRequested();
			}

			var unixMs = reader.GetInt64(0);
			if (reader.IsDBNull(2) || !windows.Covers((Spectrometer)reader.GetInt64(1)))
			{
				yield return new TrendPoint(unixMs, double.NaN);
				continue;
			}

			var byteCount = (int)reader.GetBytes(2, 0, buffer, 0, buffer.Length);
			yield return new TrendPoint(unixMs, QueryWindows.MeanOf(buffer.AsSpan(0, byteCount)));
		}
	}

	/// <summary>
	/// Selects each row's time, device and window bytes, NULL for a corrupt row: an unknown device or a pixel blob of
	/// the wrong length.
	/// </summary>
	private static string SeriesStatement(QueryWindows windows)
	{
		// The length check stays inside the CASE: Microsoft.Data.Sqlite's GetBytes on an expression column crashes the
		// process in sqlite3_table_column_metadata when the result holds a second expression column.
		return $"""
			SELECT received_at, device, CASE
				{WindowWhen(windows, Spectrometer.Atp2000)}
				{WindowWhen(windows, Spectrometer.Atp8000)}
				END
			FROM spectrum WHERE received_at >= $from AND received_at < $to
			ORDER BY received_at, id
			""";
	}

	private static string WindowWhen(QueryWindows windows, Spectrometer device)
	{
		var window = windows.For(device);
		return $"WHEN device = {(byte)device} AND length(pixels) = {PixelBytes(device)} "
			+ $"THEN substr(pixels, {QueryWindows.ByteStart(window)}, {QueryWindows.ByteCount(window)})";
	}

	private Task<Result<T>> ReadAsync<T>(
		Func<SqliteConnection, Result<T>> read, string failure, CancellationToken token)
	{
		return Task.Run(
			() =>
			{
				try
				{
					using var connection = StoreConnection.OpenReader(path);
					return read(connection);
				}
				catch (SqliteException ex)
				{
					return Result.Fail<T>(new ExceptionalError(failure, ex));
				}
			},
			token);
	}

	private static Result<StoredSpectrum?> ToStoredSpectrum(SqliteDataReader reader)
	{
		var storedDevice = reader.GetInt64(2);
		var pixelBytes = (byte[])reader.GetValue(10);
		if (IntactDevice(storedDevice, pixelBytes.Length) is not { } device)
		{
			return Result.Fail(
				$"The stored spectrum row {reader.GetInt64(0)} is corrupt: device {storedDevice}, "
				+ $"{pixelBytes.Length} pixel bytes");
		}

		var measurement = new SpectrumMeasurement(
			device,
			(ushort)reader.GetInt32(3),
			reader.GetByte(4),
			FloatOrNaN(reader, 5),
			FloatOrNaN(reader, 6),
			FloatOrNaN(reader, 7),
			MemoryMarshal.Cast<byte, ushort>(pixelBytes).ToArray());
		return new StoredSpectrum(
			measurement,
			DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
			reader.GetByte(8),
			(BoardState)reader.GetByte(9));
	}

	/// <summary>A row's device when it is a known spectrometer and the pixel blob has its length; null otherwise.</summary>
	private static Spectrometer? IntactDevice(long device, long pixelBytes)
	{
		if (device is not ((long)Spectrometer.Atp2000 or (long)Spectrometer.Atp8000))
		{
			return null;
		}

		var spectrometer = (Spectrometer)device;
		return pixelBytes == PixelBytes(spectrometer) ? spectrometer : null;
	}

	private static int PixelBytes(Spectrometer device)
	{
		return sizeof(ushort) * device.PixelCount;
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
