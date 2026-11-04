using System.Diagnostics;
using System.Globalization;

using FluentResults;

using Microsoft.Data.Sqlite;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;

namespace SpectrumVisualizer.Tools.StoreBenchmark;

/// <summary>Fills a store file, measures it fresh and churned, and prints one table.</summary>
public static class BenchmarkRun
{
	private const int Columns = 1000;
	private const int WarmRuns = 5;
	private const int RetentionRows = 120;
	private const double Tolerance = 1e-9;
	private const double BytesPerMegabyte = 1024 * 1024;

	private static readonly TimeSpan _tenMinutes = TimeSpan.FromMinutes(10);

	public static async Task<int> RunAsync(BenchmarkOptions options, TextWriter output, CancellationToken token)
	{
		var folder = options.Folder ?? Path.Combine(Path.GetTempPath(), $"spectrum-store-benchmark-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		var file = BenchmarkFile.Create(Path.Combine(folder, "spectra.db"), options, DateTimeOffset.UtcNow);
		var mismatches = new List<string>();
		try
		{
			WriteHeader(output, options, folder);
			var throughput = file.Fill();
			await MeasureAsync(output, "fresh", throughput, options, file, mismatches, token);
			throughput = file.Churn(options.ChurnRounds);
			await MeasureAsync(output, "churned", throughput, options, file, mismatches, token);
		}
		finally
		{
			file.Dispose();
			SqliteConnection.ClearAllPools();
			if (!options.Keep)
			{
				BenchmarkFile.DeleteFiles(file.Path);
			}
		}

		foreach (var mismatch in mismatches)
		{
			output.WriteLine($"MISMATCH: {mismatch}");
		}

		output.WriteLine(mismatches.Count == 0
			? $"Strategies agree: every column's min, max and counts match to {Tolerance:0e0}."
			: "Strategies disagree.");
		return mismatches.Count == 0 ? 0 : 1;
	}

	private static void WriteHeader(TextWriter output, BenchmarkOptions options, string folder)
	{
		output.Write(Invariant(
			$"Store benchmark: {options.Rows} {options.Device.DisplayName} spectra 500 ms apart ({SpanHours(options):0.##} h), "));
		output.Write(Invariant(
			$"{options.FirstWavelengthNm} nm, {options.ChangedWavelengthNm} nm from {options.ChangeAt} of the rows on, "));
		output.Write(Invariant(
			$"delta {BenchmarkFile.Delta}, {options.ChurnRounds} churn rounds, {Columns} columns, "));
		output.WriteLine(Invariant($"batches of {BenchmarkFile.BatchSize}. Folder {folder}."));
		output.Write(Invariant(
			$"Series query the {options.ChangedWavelengthNm} nm window. First is the first run on a fresh connection; "));
		output.WriteLine(
			$"Median, Min and Max cover the {WarmRuns} runs after it. The OS file cache is not dropped (that needs "
			+ "administrator rights), so a first run can be warm too.");
		output.WriteLine();
		output.WriteLine("| File | Metric | First | Median | Min | Max |");
		output.WriteLine("| --- | --- | --- | --- | --- | --- |");
	}

	private static async Task MeasureAsync(
		TextWriter output,
		string state,
		double throughput,
		BenchmarkOptions options,
		BenchmarkFile file,
		List<string> mismatches,
		CancellationToken token)
	{
		file.Checkpoint();
		var span = Invariant($"{SpanHours(options):0.##} h");
		WriteValue(output, state, "Insert throughput, batches of 256 (spectra/s)", throughput, "0");
		WriteValue(output, state, "File size per 1000 spectra, after checkpoint (MB)", PerThousand(file, options), "0.000");
		WriteValue(output, state, $"File size, {span} (MB)", file.FileBytes / BytesPerMegabyte, "0.0");

		var recent = Query(options.ChangedWavelengthNm, file.WindowTo - _tenMinutes, file.WindowTo);
		var recentSubstr = Time(output, state, "Series 10 min to 1000 columns, substr (ms)", file,
			c => ReadStrategies.Pixels(c, recent, PixelStrategy.Substr, token));
		var recentBlob = Time(output, state, "Series 10 min to 1000 columns, SqliteBlob (ms)", file,
			c => ReadStrategies.Pixels(c, recent, PixelStrategy.Blob, token));
		Compare(mismatches, $"{state} 10 min SqliteBlob against substr", recentBlob, recentSubstr);

		var whole = Query(options.ChangedWavelengthNm, file.WindowFrom, file.WindowTo);
		var wholeSubstr = Time(output, state, $"Series {span} to 1000 columns, substr (ms)", file,
			c => ReadStrategies.Pixels(c, whole, PixelStrategy.Substr, token));
		var wholeBlob = Time(output, state, $"Series {span} to 1000 columns, SqliteBlob (ms)", file,
			c => ReadStrategies.Pixels(c, whole, PixelStrategy.Blob, token));
		Compare(mismatches, $"{state} {span} SqliteBlob against substr", wholeBlob, wholeSubstr);
		await MeasureHistoryAsync(output, state, span, file, (recent, recentSubstr), (whole, wholeSubstr), mismatches, token);

		WriteTiming(output, state, $"One retention pass, {RetentionRows} rows (ms)",
			file.TimeRetentionPasses(WarmRuns + 1, RetentionRows));
		var walBytes = await file.WalBytesAfterReadBesideWriterAsync(() =>
		{
			using var connection = file.OpenReader();
			ReadStrategies.Pixels(connection, whole, PixelStrategy.Substr, token);
		});
		WriteValue(output, state, $"WAL after a {span} substr series read beside a writer at 2 spectra/s (MB)",
			walBytes / BytesPerMegabyte, "0.000");
	}

	/// <summary>Times SpectrumHistory, connection open included, against the substr reads it runs.</summary>
	private static async Task MeasureHistoryAsync(
		TextWriter output,
		string state,
		string span,
		BenchmarkFile file,
		(IntensityQuery Query, IntensitySeries Substr) recent,
		(IntensityQuery Query, IntensitySeries Substr) whole,
		List<string> mismatches,
		CancellationToken token)
	{
		var history = new SpectrumHistory(file.Path);
		var recentSeries = await TimeAsync(output, state, "Series 10 min to 1000 columns, SpectrumHistory (ms)",
			() => history.IntensitySeriesAsync(recent.Query, token));
		Compare(mismatches, $"{state} 10 min SpectrumHistory against substr", recentSeries, recent.Substr);
		var wholeSeries = await TimeAsync(output, state, $"Series {span} to 1000 columns, SpectrumHistory (ms)",
			() => history.IntensitySeriesAsync(whole.Query, token));
		Compare(mismatches, $"{state} {span} SpectrumHistory against substr", wholeSeries, whole.Substr);
		var last = await TimeAsync(output, state, "SpectrumAt the end of the range, SpectrumHistory (ms)",
			() => history.SpectrumAtAsync(file.WindowTo, token));
		if (last is null)
		{
			mismatches.Add($"{state} SpectrumAt the end of the range found no spectrum");
		}
	}

	private static async Task<T> TimeAsync<T>(TextWriter output, string state, string metric, Func<Task<Result<T>>> read)
	{
		var milliseconds = new double[WarmRuns + 1];
		Result<T>? result = null;
		for (var run = 0; run < milliseconds.Length; run++)
		{
			var stopwatch = Stopwatch.StartNew();
			result = await read();
			milliseconds[run] = stopwatch.Elapsed.TotalMilliseconds;
			if (result.IsFailed)
			{
				throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Message)));
			}
		}

		WriteTiming(output, state, metric, milliseconds);
		return result!.Value;
	}

	private static IntensitySeries Time(
		TextWriter output,
		string state,
		string metric,
		BenchmarkFile file,
		Func<SqliteConnection, IntensitySeries> read)
	{
		var milliseconds = new double[WarmRuns + 1];
		IntensitySeries? series = null;
		for (var run = 0; run < milliseconds.Length; run++)
		{
			var stopwatch = Stopwatch.StartNew();
			using (var connection = file.OpenReader())
			{
				series = read(connection);
			}

			milliseconds[run] = stopwatch.Elapsed.TotalMilliseconds;
		}

		WriteTiming(output, state, metric, milliseconds);
		return series!;
	}

	/// <summary>Writes the first run, then the median, min and max of the runs after it.</summary>
	private static void WriteTiming(TextWriter output, string state, string metric, double[] milliseconds)
	{
		var warm = milliseconds[1..];
		Array.Sort(warm);
		output.WriteLine(Invariant(
			$"| {state} | {metric} | {milliseconds[0]:0.0} | {warm[warm.Length / 2]:0.0} | {warm[0]:0.0} | {warm[^1]:0.0} |"));
	}

	private static void WriteValue(TextWriter output, string state, string metric, double value, string format)
	{
		var text = double.IsNaN(value) ? "n/a" : value.ToString(format, CultureInfo.InvariantCulture);
		output.WriteLine($"| {state} | {metric} | - | {text} | - | - |");
	}

	private static void Compare(List<string> mismatches, string what, IntensitySeries actual, IntensitySeries expected)
	{
		for (var i = 0; i < expected.Columns.Length; i++)
		{
			var a = actual.Columns[i];
			var e = expected.Columns[i];
			if (a.Count != e.Count || a.UncoveredCount != e.UncoveredCount || !Near(a.Min, e.Min) || !Near(a.Max, e.Max))
			{
				mismatches.Add($"{what}: column {i} is {a}, expected {e}");
				return;
			}
		}
	}

	private static bool Near(double actual, double expected)
	{
		return (double.IsNaN(actual) && double.IsNaN(expected)) || Math.Abs(actual - expected) <= Tolerance;
	}

	private static IntensityQuery Query(ushort nm, DateTimeOffset from, DateTimeOffset to)
	{
		return IntensityQuery.Create(nm, BenchmarkFile.Delta, from, to, Columns).Value;
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
}
