using FluentResults;

namespace SpectrumVisualizer.UI.Link;

/// <summary>A link failure on one port; the port name sits in the message and under <see cref="PortKey"/>.</summary>
public abstract class LinkError : Error
{
	public const string PortKey = "Port";

	protected LinkError(string portName, string message)
		: base(message)
	{
		Metadata.Add(PortKey, portName);
	}
}

/// <summary>Another program holds the port, or Windows denied access to it.</summary>
public sealed class PortBusyError(string portName)
	: LinkError(portName, $"Port {portName} is held by another program.");

/// <summary>The port is not on this machine.</summary>
public sealed class PortNotFoundError(string portName)
	: LinkError(portName, $"Port {portName} was not found.");

/// <summary>The open port stopped answering reads or left the port list.</summary>
public sealed class PortLostError(string portName)
	: LinkError(portName, $"Port {portName} was lost.");

/// <summary>The port gave no valid frame and no data for the health window.</summary>
public sealed class BoardNotRespondingError(string portName)
	: LinkError(portName, $"The board on port {portName} is not responding.");

/// <summary>The port gave data but no valid frame for the health window.</summary>
public sealed class UnrecognisedDataError(string portName)
	: LinkError(portName, $"Port {portName} receives data that holds no valid frame.");
