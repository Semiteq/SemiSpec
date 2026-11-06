using SemiSpec.Core.Protocol;

namespace SemiSpec.Tools.StoreBenchmark;

/// <summary>
/// What the benchmark fills and measures; Folder null means a new folder under the system temp folder.
/// </summary>
public sealed record BenchmarkOptions(
	int Rows,
	Spectrometer Device,
	int ChurnRounds,
	string? Folder,
	bool Keep)
{
	/// <summary>The wavelength of every row and of the series.</summary>
	public ushort WavelengthNm => Device.DefaultWavelengthNm;
}
