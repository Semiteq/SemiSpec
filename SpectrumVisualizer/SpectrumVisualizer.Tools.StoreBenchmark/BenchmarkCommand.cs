using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;

using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.Tools.StoreBenchmark;

/// <summary>The command line of the store benchmark; range rules live in option parsers.</summary>
public static class BenchmarkCommand
{
	public const int DefaultRows = 172_800;

	/// <summary>Ten minutes of spectra at 2 per second, so the 10 min series has rows.</summary>
	public const int MinimumRows = 1_200;

	public const int MaximumChurnRounds = 24;

	private static readonly Dictionary<string, Spectrometer> _models = new()
	{
		["2000"] = Spectrometer.Atp2000,
		["8000"] = Spectrometer.Atp8000,
	};

	private static readonly Option<int> _rows = new("--rows")
	{
		Description = "Spectra to insert, 500 ms apart, ending at the start time; at least 1200.",
		DefaultValueFactory = _ => DefaultRows,
		CustomParser = result => ParseWhole(result, "--rows", MinimumRows, int.MaxValue),
	};

	private static readonly Option<int> _device = new Option<int>("--device")
	{
		Description = "The device of every row: 2000 (ATP-2000, 650 then 700 nm) or 8000 (ATP-8000, 1550 then 1600 nm).",
		DefaultValueFactory = _ => 2000,
	}.AcceptOnlyFromAmong([.. _models.Keys]);

	private static readonly Option<double> _changeAt = new("--change-at")
	{
		Description = "From this fraction of the rows on, the rows carry the second wavelength; 1 never changes.",
		DefaultValueFactory = _ => 0.875,
		CustomParser = ParseFraction,
	};

	private static readonly Option<int> _churnRounds = new("--churn-rounds")
	{
		Description = "Rounds of 'delete the oldest 1/24 of the span, insert it again one span later'; 0 skips.",
		DefaultValueFactory = _ => MaximumChurnRounds,
		CustomParser = result => ParseWhole(result, "--churn-rounds", 0, MaximumChurnRounds),
	};

	private static readonly Option<string?> _folder = new("--folder")
	{
		Description = "Where the database files go; a new folder under the system temp folder by default.",
	};

	private static readonly Option<bool> _keep = new("--keep")
	{
		Description = "Keep the database files after the run.",
	};

	public static RootCommand Build(Func<BenchmarkOptions, CancellationToken, Task<int>> run)
	{
		var root = new RootCommand("Fills a spectrum store file and measures its writes, size and reads.")
		{
			_rows, _device, _changeAt, _churnRounds, _folder, _keep,
		};
		root.SetAction((parsed, cancellationToken) => run(From(parsed), cancellationToken));
		return root;
	}

	private static BenchmarkOptions From(ParseResult parsed)
	{
		return new BenchmarkOptions(
			parsed.GetValue(_rows),
			_models[parsed.GetValue(_device).ToString(CultureInfo.InvariantCulture)],
			parsed.GetValue(_changeAt),
			parsed.GetValue(_churnRounds),
			parsed.GetValue(_folder),
			parsed.GetValue(_keep));
	}

	private static double ParseFraction(ArgumentResult result)
	{
		var text = result.Tokens[^1].Value;
		if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var fraction)
			|| fraction is not (>= 0 and <= 1))
		{
			result.AddError($"Option '--change-at' expects a number from 0 to 1, got '{text}'.");
			return 0;
		}

		return fraction;
	}

	private static int ParseWhole(ArgumentResult result, string name, int minimum, int maximum)
	{
		var text = result.Tokens[^1].Value;
		if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
			|| value < minimum
			|| value > maximum)
		{
			result.AddError($"Option '{name}' expects a whole number from {minimum} to {maximum}, got '{text}'.");
			return 0;
		}

		return value;
	}
}
