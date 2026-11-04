using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Tests.Unit.Core.Store;
using SpectrumVisualizer.Tools.StoreBenchmark;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Tools;

[Trait("Component", "Tools")]
[Trait("Area", "Store")]
[Trait("Category", "Unit")]
public sealed class StoreBenchmarkRunTests : IDisposable
{
	private static readonly string[] _metrics =
	[
		"Insert throughput, batches of 256 (spectra/s)",
		"File size per 1000 spectra, after checkpoint (MB)",
		"File size, 0.28 h (MB)",
		"Series 10 min to 1000 columns, substr (ms)",
		"Series 10 min to 1000 columns, SqliteBlob (ms)",
		"Series 0.28 h to 1000 columns, substr (ms)",
		"Series 0.28 h to 1000 columns, SqliteBlob (ms)",
		"One retention pass, 120 rows (ms)",
		"WAL after a 0.28 h substr series read beside a writer at 2 spectra/s (MB)",
	];

	private readonly TempFolder _folder = new();

	[Fact]
	public async Task ASmallRunPrintsEveryMeasurementAndTheStrategiesAgree()
	{
		var output = new StringWriter();
		var options = new BenchmarkOptions(2000, Spectrometer.Atp2000, 0.5, 2, _folder.Root, Keep: false);

		var exitCode = await BenchmarkRun.RunAsync(options, output, TestContext.Current.CancellationToken);

		var text = output.ToString();
		exitCode.Should().Be(0, text);
		foreach (var state in new[] { "fresh", "churned" })
		{
			foreach (var metric in _metrics)
			{
				text.Should().Contain($"| {state} | {metric} |");
			}
		}

		text.Should().Contain("Strategies agree").And.NotContain("MISMATCH");
		Directory.EnumerateFiles(_folder.Root).Should().BeEmpty();
	}

	public void Dispose()
	{
		_folder.Dispose();
	}
}
