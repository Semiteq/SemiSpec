namespace SpectrumVisualizer.Core.Trend;

/// <summary>The stored intensity points of a range that starts at From, in receive order.</summary>
public sealed record IntensityChunk(DateTimeOffset From, TrendPoint[] Points);

/// <summary>One spectrum's window mean at its receive time; NaN for an uncovered device or a corrupt row.</summary>
public readonly record struct TrendPoint(long UnixMs, double Value);
