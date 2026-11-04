namespace SpectrumVisualizer.Core.Spectrum;

/// <summary>How the spectrum is drawn: inverted over the frame's maximum, and on a log10 axis.</summary>
public readonly record struct SpectrumDisplay(bool IsInverted, bool IsLogarithmic);
