using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.Tools.BoardEmulator;

/// <summary>What the emulated board sends; Rate is in frames per second, 0 sends heartbeats only.</summary>
public sealed record EmulatorOptions(
	string Port,
	Spectrometer Device,
	double Rate,
	ushort WavelengthNm,
	byte Delta,
	int BadCrcEvery)
{
	public static ushort DefaultWavelength(Spectrometer device)
	{
		return device == Spectrometer.Atp2000 ? (ushort)650 : (ushort)1550;
	}
}
