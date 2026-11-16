using FluentResults;

namespace SemiSpec.UI.Settings;

/// <summary>The settings file exists but could not be read, so the run starts with the default display choices.</summary>
public sealed class SettingsReadError(string path)
	: Error($"The settings file '{path}' could not be read.");

/// <summary>The settings file could not be written, so the changed choice holds for this run only.</summary>
public sealed class SettingsWriteError(string path)
	: Error($"The settings file '{path}' could not be written.");
