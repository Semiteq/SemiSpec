using FluentResults;

namespace SemiSpec.UI.Store;

/// <summary>
/// The store file could not be prepared or opened, so nothing is stored for the run; the path sits in the message.
/// </summary>
public sealed class StoreOpenError(string path)
	: Error($"The spectrum store file '{path}' could not be opened.");

/// <summary>The history file could not be read, so it was set aside and a fresh one started; the run stores frames.</summary>
public sealed class StoreFileReplacedError(string path, string setAsidePath)
	: Error($"The spectrum store file '{path}' could not be read; it was set aside as '{setAsidePath}'.");

/// <summary>A batch of received frames could not be written to the store file.</summary>
public sealed class StoreWriteError()
	: Error("Received frames could not be written to the spectrum store.");

/// <summary>
/// An unexpected failure after the open ended the store writer, so nothing more is stored for the run.
/// </summary>
public sealed class StoreStoppedError()
	: Error("The spectrum store stopped after an unexpected failure.");
