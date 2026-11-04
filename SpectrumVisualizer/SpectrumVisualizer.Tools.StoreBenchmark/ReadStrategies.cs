using System.Runtime.InteropServices;

using Microsoft.Data.Sqlite;

using SpectrumVisualizer.Core.Calibration;
using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;

namespace SpectrumVisualizer.Tools.StoreBenchmark;

/// <summary>The two ways to read the window intensity from the stored pixels, kept for measuring again.</summary>
public static class ReadStrategies
{
	private const int RowsPerCancellationCheck = 256;
	private const string InRange = "received_at >= $from AND received_at < $to";

	/// <summary>Every row's window mean from its pixels.</summary>
	public static IntensitySeries Pixels(
		SqliteConnection connection,
		IntensityQuery query,
		PixelStrategy strategy,
		CancellationToken token)
	{
		var buckets = new IntensityBuckets(query);
		var windows = new DeviceWindows(query.WavelengthNm, query.Delta);
		if (strategy == PixelStrategy.Substr)
		{
			AddSubstrPixels(connection, query, windows, buckets, token);
		}
		else
		{
			AddBlobPixels(connection, query, windows, buckets, token);
		}

		return buckets.ToSeries();
	}

	private static void AddSubstrPixels(
		SqliteConnection connection,
		IntensityQuery query,
		DeviceWindows windows,
		IntensityBuckets buckets,
		CancellationToken token)
	{
		var atp2000 = windows.For(Spectrometer.Atp2000);
		var atp8000 = windows.For(Spectrometer.Atp8000);
		using var command = Command(
			connection,
			query,
			$"""
			SELECT received_at, device, CASE device
				WHEN {(byte)Spectrometer.Atp2000} THEN substr(pixels, {ByteStart(atp2000)}, {ByteCount(atp2000)})
				WHEN {(byte)Spectrometer.Atp8000} THEN substr(pixels, {ByteStart(atp8000)}, {ByteCount(atp8000)})
				END
			FROM spectrum WHERE {InRange}
			""");
		using var reader = command.ExecuteReader();
		var buffer = new byte[windows.LargestByteCount];
		var rows = 0;
		while (reader.Read())
		{
			CheckEvery256Rows(++rows, token);
			var receivedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0));
			var device = (Spectrometer)reader.GetByte(1);
			if (!windows.Covers(device))
			{
				buckets.AddUncovered(receivedAt);
				continue;
			}

			var byteCount = (int)reader.GetBytes(2, 0, buffer, 0, buffer.Length);
			buckets.Add(receivedAt, MeanOf(buffer.AsSpan(0, byteCount)));
		}
	}

	private static void AddBlobPixels(
		SqliteConnection connection,
		IntensityQuery query,
		DeviceWindows windows,
		IntensityBuckets buckets,
		CancellationToken token)
	{
		using var command = Command(connection, query, $"SELECT id, received_at, device FROM spectrum WHERE {InRange}");
		using var reader = command.ExecuteReader();
		var buffer = new byte[windows.LargestByteCount];
		var rows = 0;
		while (reader.Read())
		{
			CheckEvery256Rows(++rows, token);
			var receivedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1));
			var device = (Spectrometer)reader.GetByte(2);
			if (!windows.Covers(device))
			{
				buckets.AddUncovered(receivedAt);
				continue;
			}

			var window = windows.For(device);
			var byteCount = ByteCount(window);
			using var blob = new SqliteBlob(connection, "spectrum", "pixels", reader.GetInt64(0), readOnly: true);
			blob.Position = ByteStart(window) - 1;
			blob.ReadExactly(buffer, 0, byteCount);
			buckets.Add(receivedAt, MeanOf(buffer.AsSpan(0, byteCount)));
		}
	}

	private static double MeanOf(ReadOnlySpan<byte> windowBytes)
	{
		var pixels = MemoryMarshal.Cast<byte, ushort>(windowBytes);
		return SpectrumIntensity.Mean(pixels, new PixelWindow(0, pixels.Length - 1));
	}

	/// <summary>The 1-based byte offset of the window's first pixel, as substr counts.</summary>
	private static int ByteStart(PixelWindow window)
	{
		return (sizeof(ushort) * window.First) + 1;
	}

	private static int ByteCount(PixelWindow window)
	{
		return sizeof(ushort) * (window.Last - window.First + 1);
	}

	private static SqliteCommand Command(SqliteConnection connection, IntensityQuery query, string sql)
	{
		var command = connection.CreateCommand();
		command.CommandText = sql;
		command.Parameters.AddWithValue("$from", query.From.ToUnixTimeMilliseconds());
		command.Parameters.AddWithValue("$to", query.To.ToUnixTimeMilliseconds());
		return command;
	}

	private static void CheckEvery256Rows(int rows, CancellationToken token)
	{
		if (rows % RowsPerCancellationCheck == 0)
		{
			token.ThrowIfCancellationRequested();
		}
	}

	/// <summary>The queried window of each device and whether the device covers the wavelength at all.</summary>
	private sealed class DeviceWindows(ushort nm, byte delta)
	{
		private readonly PixelWindow _atp2000 = SpectrometerCalibration.For(Spectrometer.Atp2000).Window(nm, delta);
		private readonly PixelWindow _atp8000 = SpectrometerCalibration.For(Spectrometer.Atp8000).Window(nm, delta);
		private readonly bool _atp2000Covers = SpectrumIntensity.Covers(Spectrometer.Atp2000, nm);
		private readonly bool _atp8000Covers = SpectrumIntensity.Covers(Spectrometer.Atp8000, nm);

		public int LargestByteCount => Math.Max(ByteCount(_atp2000), ByteCount(_atp8000));

		public bool Covers(Spectrometer device)
		{
			return device == Spectrometer.Atp2000 ? _atp2000Covers : _atp8000Covers;
		}

		public PixelWindow For(Spectrometer device)
		{
			return device == Spectrometer.Atp2000 ? _atp2000 : _atp8000;
		}
	}
}

/// <summary>How a pixel read takes the window out of the stored blob.</summary>
public enum PixelStrategy
{
	/// <summary>substr in the SQL, which loads the whole blob before it cuts the window.</summary>
	Substr,

	/// <summary>A per-row SqliteBlob handle that reads the window's bytes only.</summary>
	Blob,
}
