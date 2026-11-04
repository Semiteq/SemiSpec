namespace SpectrumVisualizer.Core.Trend;

/// <summary>
/// A key change: the store points before RecentStart are to be read at Key and landed under Generation, which also
/// counts the key changes; with ReadsHistory false the cache already holds them and the pending read is only cancelled.
/// </summary>
public sealed record ReReadRequest(int Generation, TrendKey Key, DateTimeOffset RecentStart, bool ReadsHistory);
