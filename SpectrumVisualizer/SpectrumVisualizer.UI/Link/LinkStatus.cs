using FluentResults;

using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.UI.Link;

public enum LinkState
{
	Disconnected,
	Connecting,
	PortUnavailable,
	Waiting,
	Receiving,
	NotResponding,
	OtherDevice,
}

/// <summary>What the link publishes; Fault is set on the first status of a failure, null on repeats.</summary>
public sealed record LinkStatus(LinkState State, string? PortName, BoardState? Board, long LostSpectra, IError? Fault);
