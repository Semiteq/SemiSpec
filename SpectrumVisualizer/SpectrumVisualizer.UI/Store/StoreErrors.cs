using FluentResults;

namespace SpectrumVisualizer.UI.Store;

/// <summary>The store file could not be prepared or opened, so nothing is stored for the run; the path sits in the message.</summary>
public sealed class StoreOpenError(string path)
	: Error($"The spectrum store file '{path}' could not be opened.");

/// <summary>A batch of received frames could not be written to the store file.</summary>
public sealed class StoreWriteError()
	: Error("Received frames could not be written to the spectrum store.");
