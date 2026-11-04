using FluentResults;

namespace SpectrumVisualizer.UI.Link;

/// <summary>The serial ports of this machine: the process boundary the link reads the board through.</summary>
public interface ISerialPortProvider
{
	IReadOnlyList<string> PortNames();

	Result<Stream> Open(string portName);
}
