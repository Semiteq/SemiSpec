using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.Tools.StoreBenchmark;

/// <summary>What the benchmark fills and measures; Folder null means a new folder under the system temp folder.</summary>
public sealed record BenchmarkOptions(
	int Rows,
	Spectrometer Device,
	double ChangeAt,
	int ChurnRounds,
	string? Folder,
	bool Keep)
{
	/// <summary>The wavelength of the rows before the change: inside the device's firmware table range.</summary>
	public ushort FirstWavelengthNm => Device == Spectrometer.Atp2000 ? (ushort)650 : (ushort)1550;

	/// <summary>The wavelength of the rows from the change on.</summary>
	public ushort ChangedWavelengthNm => (ushort)(FirstWavelengthNm + 50);
}
