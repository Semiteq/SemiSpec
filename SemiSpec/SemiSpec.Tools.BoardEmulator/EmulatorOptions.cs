using SemiSpec.Core.Protocol;

namespace SemiSpec.Tools.BoardEmulator;

/// <summary>What the emulated board sends; Rate is in frames per second, 0 sends heartbeats only.</summary>
public sealed record EmulatorOptions(
	string Port,
	Spectrometer Device,
	double Rate,
	ushort WavelengthNm,
	byte Delta,
	int BadCrcEvery,
	int DarkEvery,
	bool BothConnected,
	Spectrometer? Failed);
