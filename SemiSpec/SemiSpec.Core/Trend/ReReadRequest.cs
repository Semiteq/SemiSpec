using SemiSpec.Core.Store;

namespace SemiSpec.Core.Trend;

/// <summary>
/// A key change, a normalization change or the stored day at start: the store points before RecentStart are to be read
/// at Key, normalized to Reference when it is set, and landed under Generation, which counts the requests.
/// </summary>
public sealed record ReReadRequest(
	int Generation, TrendKey Key, DateTimeOffset RecentStart, NormalizationReference? Reference = null);
