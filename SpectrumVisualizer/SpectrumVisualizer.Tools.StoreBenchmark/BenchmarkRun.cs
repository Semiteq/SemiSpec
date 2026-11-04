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
		var folder = options.Folder
			?? Path.Combine(Path.GetTempPath(), $"spectrum-store-benchmark-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		var file = BenchmarkFile.Create(Path.Combine(folder, "spectra.db"), options, DateTimeOffset.UtcNow);
		var mismatches = new List<string>();
		try
		{
			WriteHeader(output, options, folder);
			var throughput = file.Fill();
			await MeasureAsync(new MeasuredState(output, "fresh", options, file, mismatches, token), throughput);
			throughput = file.Churn(options.ChurnRounds);
			await MeasureAsync(new MeasuredState(output, "churned", options, file, mismatches, token), throughput);
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
		var recentSubstr = Time(measured, $"Series 10 min to {Columns} columns, substr (ms)",
			c => SpectrumHistory.ReadSeries(c, recent, token));
		var recentBlob = Time(measured, $"Series 10 min to {Columns} columns, SqliteBlob (ms)",
			c => BlobSeries.Read(c, recent, token));
		Compare(measured, "10 min SqliteBlob against substr", recentBlob, recentSubstr);

		var whole = Query(options.WavelengthNm, file.WindowFrom, file.WindowTo);
		var wholeSubstr = Time(measured, $"Series {span} to {Columns} columns, substr (ms)",
			c => SpectrumHistory.ReadSeries(c, whole, token));
		var wholeBlob = Time(measured, $"Series {span} to {Columns} columns, SqliteBlob (ms)",
			c => BlobSeries.Read(c, whole, token));
		Compare(measured, $"{span} SqliteBlob against substr", wholeBlob, wholeSubstr);
		await MeasureHistoryAsync(measured, span, (recent, recentSubstr), (whole, wholeSubstr));

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

	/// <summary>Times SpectrumHistory, connection open included, against the substr reads it runs.</summary>
	private static async Task MeasureHistoryAsync(
		MeasuredState measured,
		string span,
		(IntensityQuery Query, IntensitySeries Substr) recent,
		(IntensityQuery Query, IntensitySeries Substr) whole)
	{
		var history = new SpectrumHistory(measured.File.Path);
		var token = measured.Token;
		var recentSeries = await TimeAsync(measured, $"Series 10 min to {Columns} columns, SpectrumHistory (ms)",
			() => history.IntensitySeriesAsync(recent.Query, token));
		Compare(measured, "10 min SpectrumHistory against substr", recentSeries, recent.Substr);
		var wholeSeries = await TimeAsync(measured, $"Series {span} to {Columns} columns, SpectrumHistory (ms)",
			() => history.IntensitySeriesAsync(whole.Query, token));
		Compare(measured, $"{span} SpectrumHistory against substr", wholeSeries, whole.Substr);
		var last = await TimeAsync(measured, "SpectrumAt the end of the range, SpectrumHistory (ms)",
			() => history.SpectrumAtAsync(measured.File.WindowTo, token));
		if (last is null)
		{
			measured.Mismatches.Add($"{measured.State} SpectrumAt the end of the range found no spectrum");
		}
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

	private static IntensitySeries Time(
		MeasuredState measured, string metric, Func<SqliteConnection, IntensitySeries> read)
	{
		var milliseconds = new double[WarmRuns + 1];
		IntensitySeries? series = null;
		for (var run = 0; run < milliseconds.Length; run++)
		{
			var stopwatch = Stopwatch.StartNew();
			using (var connection = measured.File.OpenReader())
			{
				series = read(connection);
			}

			milliseconds[run] = stopwatch.Elapsed.TotalMilliseconds;
		}

		WriteTiming(measured, metric, milliseconds);
		return series!;
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

	private static void Compare(MeasuredState measured, string what, IntensitySeries actual, IntensitySeries expected)
	{
		for (var i = 0; i < expected.Columns.Length; i++)
		{
			var a = actual.Columns[i];
			var e = expected.Columns[i];
			if (a.Count != e.Count
				|| a.UncoveredCount != e.UncoveredCount
				|| !Near(a.Min, e.Min)
				|| !Near(a.Max, e.Max))
			{
				measured.Mismatches.Add($"{measured.State} {what}: column {i} is {a}, expected {e}");
				return;
			}
		}
	}

	private static bool Near(double actual, double expected)
	{
		return (double.IsNaN(actual) && double.IsNaN(expected)) || Math.Abs(actual - expected) <= Tolerance;
	}

	private static IntensityQuery Query(ushort wavelengthNm, DateTimeOffset from, DateTimeOffset to)
	{
		return IntensityQuery.Create(wavelengthNm, BenchmarkFile.Delta, from, to, Columns).Value;
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

	/// <summary>
	/// What the rows of one file state share: the output, the state's name, the run and its mismatches.
	/// </summary>
	private sealed record MeasuredState(
		TextWriter Output,
		string State,
		BenchmarkOptions Options,
		BenchmarkFile File,
		List<string> Mismatches,
		CancellationToken Token);
}
