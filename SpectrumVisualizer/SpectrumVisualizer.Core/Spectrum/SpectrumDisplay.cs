namespace SpectrumVisualizer.Core.Spectrum;

/// <summary>How the spectrum is drawn: whether inverted over the frame's maximum, and whether on a log10 axis.</summary>
public readonly record struct SpectrumDisplay(bool IsInverted, bool IsLogarithmic);
