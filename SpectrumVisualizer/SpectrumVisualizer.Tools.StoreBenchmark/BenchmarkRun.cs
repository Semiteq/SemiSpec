using System.Diagnostics;
using System.Globalization;

using FluentResults;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Core.Trend;

namespace SpectrumVisualizer.Tools.StoreBenchmark;

/// <summary>Fills a store file, measures it fresh and churned, and prints one table.</summary>
public static class BenchmarkRun
{
	private const int Columns = 1000;
	private const int TrendColumns = 2048;
	private const int OtherWavelengthStepNm = 50;
	private const int WarmRuns = 5;
	private const int RetentionRows = 120;
	private const double BytesPerMegabyte = 1024 * 1024;

	private static readonly TimeSpan _tenMinutes = TimeSpan.FromMinutes(10);

	public static async Task<int> RunAsync(BenchmarkOptions options, TextWriter output, CancellationToken token)
	{
		var folder = options.Folder
			?? Path.Combine(Path.GetTempPath(), $"spectrum-store-benchmark-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		var file = BenchmarkFile.Create(Path.Combine(folder, "spectra.db"), options, DateTimeOffset.UtcNow);
		try
		{
			WriteHeader(output, options, folder);
			var throughput = file.Fill();
			await MeasureAsync(new MeasuredState(output, "fresh", options, file, token), throughput);
			throughput = file.Churn(options.ChurnRounds);
			await MeasureAsync(new MeasuredState(output, "churned", options, file, token), throughput);
		}
		finally
		{
			file.Dispose();
			SqliteConnection.ClearAllPools();
			if (!options.Keep)
			{
				StoreFiles.DeleteWithSidecars(file.Path);
				if (options.Folder is null)
				{
					Directory.Delete(folder);
				}
			}
		}

		return 0;
	}

	private static void WriteHeader(TextWriter output, BenchmarkOptions options, string folder)
	{
		output.Write(Invariant($"Store benchmark: {options.Rows} {options.Device.DisplayName} spectra 500 ms apart "));
		output.Write(Invariant($"({SpanHours(options):0.##} h), {options.WavelengthNm} nm, "));
		output.Write(Invariant($"delta {BenchmarkFile.Delta}, "));
		output.Write(Invariant($"{options.ChurnRounds} churn rounds, {Columns} columns, "));
		output.WriteLine(Invariant($"batches of {BenchmarkFile.BatchSize}. Folder {folder}."));
		output.Write(Invariant(
			$"Series query the {options.WavelengthNm} nm window. First is the first run on a fresh connection; "));
		output.WriteLine(
			$"Median, Min and Max cover the {WarmRuns} runs after it. The OS file cache is not dropped (that needs "
			+ "administrator rights), so a first run can be warm too.");
		output.Write(Invariant($"Trend rows fold a cache of the file's frames to {TrendColumns} columns and re-read at "));
		output.WriteLine(Invariant(
			$"{OtherWavelengthNm(options, 0)} and {OtherWavelengthNm(options, 1)} nm in turn, one key change per run."));
		output.WriteLine();
		output.WriteLine("| File | Metric | First | Median | Min | Max |");
		output.WriteLine("| --- | --- | --- | --- | --- | --- |");
	}

	private static async Task MeasureAsync(MeasuredState measured, double throughput)
	{
		var (options, file, token) = (measured.Options, measured.File, measured.Token);
		file.Checkpoint();
		var span = Invariant($"{SpanHours(options):0.##} h");
		WriteValue(measured, $"Insert throughput, batches of {BenchmarkFile.BatchSize} (spectra/s)", throughput, "0");
		WriteValue(measured, "File size per 1000 spectra, after checkpoint (MB)", PerThousand(file, options), "0.000");
		WriteValue(measured, $"File size, {span} (MB)", file.FileBytes / BytesPerMegabyte, "0.0");

		var recent = Query(options.WavelengthNm, file.WindowTo - _tenMinutes, file.WindowTo);
		Time(measured, $"Series 10 min to {Columns} columns, substr (ms)",
			c => SpectrumHistory.ReadSeries(c, recent, token));
		var whole = Query(options.WavelengthNm, file.WindowFrom, file.WindowTo);
		Time(measured, $"Series {span} to {Columns} columns, substr (ms)",
			c => SpectrumHistory.ReadSeries(c, whole, token));
		await MeasureHistoryAsync(measured, span, recent, whole);
		await MeasureTrendAsync(measured, span);

		WriteTiming(measured, $"One retention pass, {RetentionRows} rows (ms)",
			file.TimeRetentionPasses(WarmRuns + 1, RetentionRows));
		var walBytes = await file.WalBytesAfterReadBesideWriterAsync(() =>
		{
			using var connection = file.OpenReader();
			SpectrumHistory.ReadSeries(connection, whole, token);
		});
		WriteValue(measured, $"WAL after a {span} substr series read beside a writer at 2 spectra/s (MB)",
			walBytes / BytesPerMegabyte, "0.000");
	}

	/// <summary>Times the SpectrumHistory calls, connection open and thread pool hop included.</summary>
	private static async Task MeasureHistoryAsync(
		MeasuredState measured, string span, IntensityQuery recent, IntensityQuery whole)
	{
		var history = new SpectrumHistory(measured.File.Path);
		var token = measured.Token;
		await TimeAsync(measured, $"Series 10 min to {Columns} columns, SpectrumHistory (ms)",
			() => history.IntensitySeriesAsync(recent, token));
		await TimeAsync(measured, $"Series {span} to {Columns} columns, SpectrumHistory (ms)",
			() => history.IntensitySeriesAsync(whole, token));
		var last = await TimeAsync(measured, "SpectrumAt the end of the range, SpectrumHistory (ms)",
			() => history.SpectrumAtAsync(measured.File.WindowTo, token)) ?? throw new InvalidOperationException($"{measured.State}: SpectrumAt the end of the range found no spectrum");
	}

	/// <summary>Times the fold and the re-read of an IntensityCache filled from the frames of the file's rows.</summary>
	private static async Task MeasureTrendAsync(MeasuredState measured, string span)
	{
		var (options, file) = (measured.Options, measured.File);
		using var cache = new IntensityCache(new FixedTimeProvider(file.WindowTo), NullLogger<IntensityCache>.Instance);
		foreach (var frame in file.WindowFrames())
		{
			cache.Add(frame);
		}

		var whole = TrendQuery(options.WavelengthNm, file.WindowFrom, file.WindowTo);
		var points = cache.Fold(whole).Columns.Sum(column => column.Count + column.UncoveredCount);
		TimeInMemory(measured, Invariant($"Trend cache fold, {span} ({points} points) to {TrendColumns} columns (ms)"),
			() => cache.Fold(whole));
		var recent = TrendQuery(options.WavelengthNm, file.WindowTo - _tenMinutes, file.WindowTo);
		TimeInMemory(measured, $"Trend cache fold, 10 min to {TrendColumns} columns (ms)", () => cache.Fold(recent));
		await TimeReReadsAsync(measured, cache);
	}

	/// <summary>
	/// Changes the key once per run and lands TrendHistoryPlan's ranges read through IntensityPointsAsync, timed from
	/// the key-changing frame's Add to the visible range's landing and to the last chunk's.
	/// </summary>
	private static async Task TimeReReadsAsync(MeasuredState measured, IntensityCache cache)
	{
		var history = new SpectrumHistory(measured.File.Path);
		ReReadRequest? request = null;
		using var subscription = cache.ReReadRequested.Subscribe(changed => request = changed);
		var toVisible = new double[WarmRuns + 1];
		var toLast = new double[WarmRuns + 1];
		var landedPoints = 0L;
		for (var run = 0; run < toVisible.Length; run++)
		{
			var wavelengthNm = OtherWavelengthNm(measured.Options, run);
			var frame = measured.File.FrameAfterWindow(run, wavelengthNm);
			request = null;
			var stopwatch = Stopwatch.StartNew();
			cache.Add(frame);
			var reRead = request is { ReadsHistory: true } asked
				? asked
				: throw new InvalidOperationException($"{measured.State}: the key change to {wavelengthNm} nm asked no read");
			var ranges = TrendHistoryPlan.For(frame.ReceivedAt - _tenMinutes, frame.ReceivedAt, frame.ReceivedAt, reRead.RecentStart);
			for (var i = 0; i < ranges.Count; i++)
			{
				var chunk = await history.IntensityPointsAsync(
					wavelengthNm, BenchmarkFile.Delta, ranges[i].From, ranges[i].To, measured.Token);
				BenchmarkFile.ThrowIfFailed(chunk);
				cache.LandHistory(reRead.Generation, chunk.Value);
				landedPoints += chunk.Value.Points.Length;
				toVisible[run] = i == 0 ? stopwatch.Elapsed.TotalMilliseconds : toVisible[run];
			}

			toLast[run] = stopwatch.Elapsed.TotalMilliseconds;
		}

		if (landedPoints == 0)
		{
			throw new InvalidOperationException($"{measured.State}: the trend re-reads landed no stored point");
		}

		WriteTiming(measured, "Trend re-read at another wavelength: visible 10 min first, to its landing (ms)", toVisible);
		WriteTiming(measured, "Trend re-read at another wavelength: the whole day in its plan, to the last chunk (ms)", toLast);
	}

	/// <summary>Alternates two wavelengths above the stored one, inside both devices' firmware table ranges.</summary>
	private static ushort OtherWavelengthNm(BenchmarkOptions options, int run)
	{
		return (ushort)(options.WavelengthNm + (OtherWavelengthStepNm * (1 + (run % 2))));
	}

	private static async Task<T> TimeAsync<T>(MeasuredState measured, string metric, Func<Task<Result<T>>> read)
	{
		var milliseconds = new double[WarmRuns + 1];
		Result<T>? result = null;
		for (var run = 0; run < milliseconds.Length; run++)
		{
			var stopwatch = Stopwatch.StartNew();
			result = await read();
			milliseconds[run] = stopwatch.Elapsed.TotalMilliseconds;
			BenchmarkFile.ThrowIfFailed(result);
		}

		WriteTiming(measured, metric, milliseconds);
		return result!.Value;
	}

	private static void Time(MeasuredState measured, string metric, Func<SqliteConnection, IntensitySeries> read)
	{
		var milliseconds = new double[WarmRuns + 1];
		for (var run = 0; run < milliseconds.Length; run++)
		{
			var stopwatch = Stopwatch.StartNew();
			using (var connection = measured.File.OpenReader())
			{
				read(connection);
			}

			milliseconds[run] = stopwatch.Elapsed.TotalMilliseconds;
		}

		WriteTiming(measured, metric, milliseconds);
	}

	private static void TimeInMemory(MeasuredState measured, string metric, Func<IntensitySeries> fold)
	{
		var milliseconds = new double[WarmRuns + 1];
		for (var run = 0; run < milliseconds.Length; run++)
		{
			var stopwatch = Stopwatch.StartNew();
			fold();
			milliseconds[run] = stopwatch.Elapsed.TotalMilliseconds;
		}

		WriteTiming(measured, metric, milliseconds);
	}

	/// <summary>Writes the first run, then the median, min and max of the runs after it.</summary>
	private static void WriteTiming(MeasuredState measured, string metric, double[] milliseconds)
	{
		var warm = milliseconds[1..];
		Array.Sort(warm);
		var (first, median, min, max) = (milliseconds[0], warm[warm.Length / 2], warm[0], warm[^1]);
		measured.Output.WriteLine(Invariant(
			$"| {measured.State} | {metric} | {first:0.0} | {median:0.0} | {min:0.0} | {max:0.0} |"));
	}

	private static void WriteValue(MeasuredState measured, string metric, double value, string format)
	{
		var text = double.IsNaN(value) ? "n/a" : value.ToString(format, CultureInfo.InvariantCulture);
		measured.Output.WriteLine($"| {measured.State} | {metric} | - | {text} | - | - |");
	}

	/// <summary>A query over the range in columns of a whole millisecond width, Columns of them give or take one.</summary>
	private static IntensityQuery Query(ushort wavelengthNm, DateTimeOffset from, DateTimeOffset to)
	{
		var widthMilliseconds = Math.Max((long)(to - from).TotalMilliseconds / Columns, 1);
		var width = TimeSpan.FromMilliseconds(widthMilliseconds);
		return IntensityQuery.Create(wavelengthNm, BenchmarkFile.Delta, from, to, width).Value;
	}

	/// <summary>A query over the range in TrendColumns columns of the whole millisecond width the trend computes.</summary>
	private static IntensityQuery TrendQuery(ushort wavelengthNm, DateTimeOffset from, DateTimeOffset to)
	{
		var widthMilliseconds = Math.Max(((long)(to - from).TotalMilliseconds + TrendColumns - 1) / TrendColumns, 1);
		var width = TimeSpan.FromMilliseconds(widthMilliseconds);
		return IntensityQuery.Create(wavelengthNm, BenchmarkFile.Delta, from, to, width).Value;
	}

	private static double PerThousand(BenchmarkFile file, BenchmarkOptions options)
	{
		return file.FileBytes / BytesPerMegabyte / options.Rows * 1000;
	}

	private static double SpanHours(BenchmarkOptions options)
	{
		return options.Rows / 2.0 / 3600;
	}

	private static string Invariant(FormattableString text)
	{
		return text.ToString(CultureInfo.InvariantCulture);
	}

	private sealed record MeasuredState(
		TextWriter Output,
		string State,
		BenchmarkOptions Options,
		BenchmarkFile File,
		CancellationToken Token);

	/// <summary>A clock stopped at the newest row, so the cache's 24 h retention keeps every row, churned ones too.</summary>
	private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow()
		{
			return now;
		}
	}
}
