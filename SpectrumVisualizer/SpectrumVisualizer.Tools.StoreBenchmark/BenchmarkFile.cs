using System.Diagnostics;

using FluentResults;

using Microsoft.Data.Sqlite;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;

namespace SpectrumVisualizer.Tools.StoreBenchmark;

/// <summary>
/// One benchmark store file and its writer: row position p holds row p mod Rows at FirstRowAt + p x 500 ms, so a
/// churn round re-inserts the oldest rows one span later with their own pixels.
/// </summary>
internal sealed class BenchmarkFile : IDisposable
{
	public const int BatchSize = 256;
	public const byte Delta = 10;

	private const int PixelPoolSize = 16;
	private const int PixelSeed = 20261104;
	private const int ChurnParts = 24;

	private static readonly TimeSpan _rowInterval = TimeSpan.FromMilliseconds(500);
	private static readonly TimeSpan _writerRowsBeforeTheFirstRow = TimeSpan.FromHours(1);

	private readonly SpectrumDatabase _database;
	private readonly BenchmarkOptions _options;
	private readonly DateTimeOffset _firstRowAt;
	private readonly ushort[][] _pixelPool;
	private long _windowStart;

	private BenchmarkFile(
		string path,
		SpectrumDatabase database,
		BenchmarkOptions options,
		DateTimeOffset lastRowAt)
	{
		Path = path;
		_database = database;
		_options = options;
		_firstRowAt = lastRowAt - (_rowInterval * (options.Rows - 1));
		_pixelPool = PixelPool(options.Device);
	}

	public string Path { get; }

	/// <summary>The receive time of the first row the file holds now; the churn moves it forward.</summary>
	public DateTimeOffset WindowFrom => TimeAt(_windowStart);

	public DateTimeOffset WindowTo => TimeAt(_windowStart + _options.Rows);

	public long FileBytes => new FileInfo(Path).Length;

	public long WalBytes => new FileInfo(Path + "-wal") is { Exists: true } wal ? wal.Length : 0;

	/// <summary>Deletes what an earlier run left at the path and opens a fresh store file there.</summary>
	public static BenchmarkFile Create(string path, BenchmarkOptions options, DateTimeOffset lastRowAt)
	{
		StoreFiles.DeleteWithSidecars(path);
		var database = SpectrumDatabase.Open(path);
		if (database.IsFailed)
		{
			throw new IOException($"The benchmark file '{path}' could not be opened: {database.Errors[0].Message}");
		}

		return new BenchmarkFile(path, database.Value, options, lastRowAt);
	}

	/// <summary>Writes every row once and returns the insert throughput in spectra per second.</summary>
	public double Fill()
	{
		return _options.Rows / WritePositions(0, _options.Rows).TotalSeconds;
	}

	/// <summary>Runs the churn rounds and returns the throughput of their inserts, NaN when there is none.</summary>
	public double Churn(int rounds)
	{
		var written = TimeSpan.Zero;
		for (var round = 0; round < rounds; round++)
		{
			var nextBoundary = Boundary(round + 1);
			ThrowIfFailed(_database.DeleteBefore(TimeAt(nextBoundary)));
			written += WritePositions(_options.Rows + Boundary(round), _options.Rows + nextBoundary);
		}

		_windowStart = Boundary(rounds);
		return rounds == 0 ? double.NaN : Boundary(rounds) / written.TotalSeconds;
	}

	/// <summary>Times retention passes over the oldest rows, retentionRows each, then writes those rows back.</summary>
	public double[] TimeRetentionPasses(int passes, int retentionRows)
	{
		var milliseconds = new double[passes];
		for (var pass = 0; pass < passes; pass++)
		{
			var cutoff = TimeAt(_windowStart + ((pass + 1L) * retentionRows));
			var stopwatch = Stopwatch.StartNew();
			ThrowIfFailed(_database.DeleteBefore(cutoff));
			milliseconds[pass] = stopwatch.Elapsed.TotalMilliseconds;
		}

		WritePositions(_windowStart, _windowStart + ((long)passes * retentionRows));
		return milliseconds;
	}

	/// <summary>
	/// Runs the read while a writer thread adds one spectrum every 500 ms; returns the WAL size after it.
	/// </summary>
	public async Task<long> WalBytesAfterReadBesideWriterAsync(Action read)
	{
		Checkpoint();
		using var stop = new CancellationTokenSource();
		var writer = Task.Run(() => WriteEvery500MillisecondsAsync(stop.Token));
		try
		{
			read();
		}
		finally
		{
			await stop.CancelAsync();
			await writer;
		}

		return WalBytes;
	}

	/// <summary>
	/// Moves the WAL into the file and truncates it, from a connection of its own while the writer idles.
	/// </summary>
	public void Checkpoint()
	{
		using var connection = StoreConnection.Open(Path);
		using var command = connection.CreateCommand();
		command.CommandText = StoreConnection.CheckpointTruncate;
		using var reader = command.ExecuteReader();
		reader.Read();
		if (reader.GetInt32(0) != 0)
		{
			throw new InvalidOperationException($"The checkpoint of {Path} was busy.");
		}
	}

	public SqliteConnection OpenReader()
	{
		return StoreConnection.OpenReader(Path);
	}

	public void Dispose()
	{
		_database.CheckpointAndClose();
	}

	private async Task WriteEvery500MillisecondsAsync(CancellationToken token)
	{
		using var timer = new PeriodicTimer(_rowInterval);
		var at = _firstRowAt - _writerRowsBeforeTheFirstRow;
		try
		{
			do
			{
				ThrowIfFailed(_database.Write([FrameAt(0, at)]));
				at += _rowInterval;
			}
			while (await timer.WaitForNextTickAsync(token));
		}
		catch (OperationCanceledException)
		{
		}
	}

	/// <summary>Writes the positions [start, end) in batches and returns the time spent inside Write.</summary>
	private TimeSpan WritePositions(long start, long end)
	{
		var batch = new List<ReceivedFrame>(BatchSize);
		var elapsed = TimeSpan.Zero;
		for (var position = start; position < end; position += BatchSize)
		{
			batch.Clear();
			var batchEnd = Math.Min(position + BatchSize, end);
			for (var p = position; p < batchEnd; p++)
			{
				batch.Add(FrameAt(p % _options.Rows, TimeAt(p)));
			}

			var stopwatch = Stopwatch.StartNew();
			ThrowIfFailed(_database.Write(batch));
			elapsed += stopwatch.Elapsed;
		}

		return elapsed;
	}

	private ReceivedFrame FrameAt(long row, DateTimeOffset receivedAt)
	{
		var measurement = new SpectrumMeasurement(
			_options.Device,
			_options.WavelengthNm,
			Delta,
			1000f + (row % 100),
			50f,
			0.5f,
			_pixelPool[row % PixelPoolSize]);
		return new ReceivedFrame(new BoardFrame(_options.Device.ConnectedBit, (byte)row, measurement), receivedAt);
	}

	private DateTimeOffset TimeAt(long position)
	{
		return _firstRowAt + (_rowInterval * position);
	}

	private long Boundary(int part)
	{
		return (long)_options.Rows * part / ChurnParts;
	}

	/// <summary>Throws the failure's messages: the benchmark has no way to go on past a failed store call.</summary>
	public static void ThrowIfFailed(IResultBase result)
	{
		if (result.IsFailed)
		{
			throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Message)));
		}
	}

	private static ushort[][] PixelPool(Spectrometer device)
	{
		var random = new Random(PixelSeed);
		var pool = new ushort[PixelPoolSize][];
		for (var i = 0; i < pool.Length; i++)
		{
			pool[i] = new ushort[device.PixelCount];
			for (var pixel = 0; pixel < pool[i].Length; pixel++)
			{
				pool[i][pixel] = (ushort)random.Next(ushort.MaxValue + 1);
			}
		}

		return pool;
	}
}
