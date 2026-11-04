namespace SpectrumVisualizer.Tools.StoreBenchmark;

/// <summary>A clock stopped at the newest row, so the cache's 24 h retention keeps every row.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
	public override DateTimeOffset GetUtcNow()
	{
		return now;
	}
}
