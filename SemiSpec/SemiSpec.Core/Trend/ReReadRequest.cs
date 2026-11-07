namespace SemiSpec.Core.Trend;

/// <summary>
/// A key change or the stored day at start: the store points before RecentStart are to be read at Key and landed under
/// Generation, which counts the requests.
/// </summary>
public sealed record ReReadRequest(int Generation, TrendKey Key, DateTimeOffset RecentStart);
