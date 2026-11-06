using System.CommandLine;

namespace SemiSpec.Tools.StoreBenchmark;

/// <summary>The command line of the store benchmark; range rules live in option parsers.</summary>
public static class BenchmarkCommand
{
	public const int DefaultRows = 172_800;

	/// <summary>Ten minutes of spectra at 2 per second, so the 10 min series has rows.</summary>
	public const int MinimumRows = 1_200;

	public const int MaximumChurnRounds = 24;

	private static readonly Option<int> _rows = new("--rows")
	{
		Description = "Spectra to insert, 500 ms apart, ending at the start time; at least 1200.",
		DefaultValueFactory = _ => DefaultRows,
		CustomParser = result => ToolCommandLine.ParseWhole(result, "--rows", MinimumRows, int.MaxValue),
	};

	private static readonly Option<int> _device = new Option<int>("--device")
	{
		Description = "The device of every row: 2000 (ATP-2000 at 650 nm) or 8000 (ATP-8000 at 1550 nm).",
		DefaultValueFactory = _ => 2000,
	}.AcceptOnlyFromAmong([.. ToolCommandLine.Models.Keys]);

	private static readonly Option<int> _churnRounds = new("--churn-rounds")
	{
		Description = "Rounds of 'delete the oldest 1/24 of the span, insert it again one span later'; 0 skips.",
		DefaultValueFactory = _ => MaximumChurnRounds,
		CustomParser = result => ToolCommandLine.ParseWhole(result, "--churn-rounds", 0, MaximumChurnRounds),
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
			_rows, _device, _churnRounds, _folder, _keep,
		};
		root.SetAction((parsed, cancellationToken) => run(From(parsed), cancellationToken));
		return root;
	}

	private static BenchmarkOptions From(ParseResult parsed)
	{
		return new BenchmarkOptions(
			parsed.GetValue(_rows),
			ToolCommandLine.ToSpectrometer(parsed.GetValue(_device)),
			parsed.GetValue(_churnRounds),
			parsed.GetValue(_folder),
			parsed.GetValue(_keep));
	}
}
