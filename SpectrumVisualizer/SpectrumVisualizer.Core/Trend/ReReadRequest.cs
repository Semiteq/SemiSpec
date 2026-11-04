namespace SpectrumVisualizer.Core.Trend;

/// <summary>
/// A key change: the store points before RecentStart are to be read at Key and landed under Generation, which also
/// counts the key changes.
/// </summary>
public sealed record ReReadRequest(int Generation, TrendKey Key, DateTimeOffset RecentStart);
