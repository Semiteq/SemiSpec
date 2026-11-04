using SpectrumVisualizer.Core.Store;

namespace SpectrumVisualizer.Core.Trend;

/// <summary>The wavelength and delta the trend's intensity is the window mean at.</summary>
public readonly record struct TrendKey(ushort WavelengthNm, byte Delta);

/// <summary>The current key with the receive times of the oldest and the newest point.</summary>
public readonly record struct TrendExtent(TrendKey Key, DateTimeOffset First, DateTimeOffset Newest);

/// <summary>A fold of the cache with the extent and the query it was built from, all read under one lock hold.</summary>
public sealed record TrendFold(TrendExtent Extent, IntensityQuery Query, IntensitySeries Series);
