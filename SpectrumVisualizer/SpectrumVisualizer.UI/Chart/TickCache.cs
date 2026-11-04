using System.Diagnostics.CodeAnalysis;

using ScottPlot;

namespace SpectrumVisualizer.UI.Chart;

/// <summary>The last two tick results of one generator, used only by its Regenerate under Plot.Sync.</summary>
internal sealed class TickCache<TKey>
	where TKey : struct, IEquatable<TKey>
{
	private Entry? _recent;
	private Entry? _older;

	public bool TryFind(TKey key, [NotNullWhen(true)] out Tick[]? ticks)
	{
		if (_recent is { } recent && recent.Key.Equals(key))
		{
			ticks = recent.Ticks;
			return true;
		}

		if (_older is { } older && older.Key.Equals(key))
		{
			(_recent, _older) = (older, _recent);
			ticks = older.Ticks;
			return true;
		}

		ticks = null;
		return false;
	}

	public void Add(TKey key, Tick[] ticks)
	{
		(_recent, _older) = (new Entry(key, ticks), _recent);
	}

	private sealed record Entry(TKey Key, Tick[] Ticks);
}
