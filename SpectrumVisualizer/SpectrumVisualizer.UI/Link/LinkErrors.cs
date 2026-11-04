using FluentResults;

namespace SpectrumVisualizer.UI.Link;

/// <summary>A link failure on one port; the port name sits in the message.</summary>
public abstract class LinkError(string message) : Error(message);

/// <summary>Another program holds the port, or Windows denied access to it.</summary>
public sealed class PortBusyError(string portName)
	: LinkError($"Port {portName} is held by another program.");

/// <summary>The port is not on this machine, or it failed to open for a reason other than a busy port.</summary>
public sealed class PortNotFoundError(string portName)
	: LinkError($"Port {portName} was not found.");

/// <summary>The open port had a read fail or return zero bytes, or the port left the port list.</summary>
public sealed class PortLostError(string portName)
	: LinkError($"Port {portName} was lost.");

/// <summary>The port gave no valid frame and no more than a frame's worth of bytes for the health window.</summary>
public sealed class BoardNotRespondingError(string portName)
	: LinkError($"The board on port {portName} is not responding.");

/// <summary>The port gave more than a frame's worth of bytes but no valid frame for the health window.</summary>
public sealed class UnrecognisedDataError(string portName)
	: LinkError($"Port {portName} receives data that holds no valid frame.");
