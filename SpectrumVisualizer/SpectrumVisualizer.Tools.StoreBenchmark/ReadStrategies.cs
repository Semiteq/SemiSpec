using Microsoft.Data.Sqlite;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;

namespace SpectrumVisualizer.Tools.StoreBenchmark;

/// <summary>The two ways to read the window intensity from the stored pixels, kept for measuring again.</summary>
public static class ReadStrategies
{
	private const int RowsPerCancellationCheck = 256;

	/// <summary>Every row's window mean from its pixels.</summary>
	public static IntensitySeries Pixels(
		SqliteConnection connection,
		IntensityQuery query,
		PixelStrategy strategy,
		CancellationToken token)
	{
		return strategy == PixelStrategy.Substr
			? SpectrumHistory.ReadSeries(connection, query, token)
			: BlobPixels(connection, query, token);
	}

	private static IntensitySeries BlobPixels(SqliteConnection connection, IntensityQuery query, CancellationToken token)
	{
		var buckets = new IntensityBuckets(query);
		var windows = new QueryWindows(query.WavelengthNm, query.Delta);
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT id, received_at, device FROM spectrum WHERE received_at >= $from AND received_at < $to";
		command.Parameters.AddWithValue("$from", query.From.ToUnixTimeMilliseconds());
		command.Parameters.AddWithValue("$to", query.To.ToUnixTimeMilliseconds());
		using var reader = command.ExecuteReader();
		var buffer = new byte[windows.LargestByteCount];
		var rows = 0;
		while (reader.Read())
		{
			if (++rows % RowsPerCancellationCheck == 0)
			{
				token.ThrowIfCancellationRequested();
			}

			var receivedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1));
			var device = (Spectrometer)reader.GetByte(2);
			if (!windows.Covers(device))
			{
				buckets.AddUncovered(receivedAt);
				continue;
			}

			var window = windows.For(device);
			var byteCount = QueryWindows.ByteCount(window);
			using var blob = new SqliteBlob(connection, "spectrum", "pixels", reader.GetInt64(0), readOnly: true);
			blob.Position = QueryWindows.ByteStart(window) - 1;
			blob.ReadExactly(buffer, 0, byteCount);
			buckets.Add(receivedAt, QueryWindows.MeanOf(buffer.AsSpan(0, byteCount)));
		}

		return buckets.ToSeries();
	}
}

/// <summary>How a pixel read takes the window out of the stored blob.</summary>
public enum PixelStrategy
{
	/// <summary>substr in the SQL, which loads the whole blob before it cuts the window; the read SpectrumHistory runs.</summary>
	Substr,

	/// <summary>A per-row SqliteBlob handle that reads the window's bytes only.</summary>
	Blob,
}
