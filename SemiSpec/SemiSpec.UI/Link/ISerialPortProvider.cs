using FluentResults;

namespace SemiSpec.UI.Link;

/// <summary>The serial ports of this machine: the process boundary the link reads the board through.</summary>
public interface ISerialPortProvider
{
	/// <summary>The port names, or a failure when Windows cannot list the ports; never throws.</summary>
	Result<IReadOnlyList<string>> PortNames();

	Result<Stream> Open(string portName);
}
