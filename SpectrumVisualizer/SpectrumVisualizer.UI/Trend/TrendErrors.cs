using FluentResults;

namespace SpectrumVisualizer.UI.Trend;

/// <summary>
/// The stored points at a new trend key could not be read; the trend shows only the data since the change.
/// </summary>
public sealed class TrendHistoryError()
	: Error("The trend history at the new wavelength or window could not be read from the spectrum store.");

/// <summary>The stored spectrum of a clicked trend moment could not be read.</summary>
public sealed class TrendMomentError()
	: Error("The stored spectrum of the clicked trend moment could not be read.");
