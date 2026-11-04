namespace SpectrumVisualizer.Core.Trend;

/// <summary>One envelope column at chart X: its min and max, and the centre value the line passes; NaN min is a gap.</summary>
public readonly record struct EnvelopeColumn(double X, double Min, double Max, double Center);
