using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using ScottPlot;

using SpectrumVisualizer.Core.Chart;

namespace SpectrumVisualizer.UI.Chart;

/// <summary>Ticks of a log10 axis whose limits are in decades; major labels use the current culture.</summary>
public sealed class LogTickGenerator : ITickGenerator
{
	private const string LabelFormat = "0.###";

	private readonly TickCache _cache = new();

	public Tick[] Ticks { get; private set; } = [];

	public int MaxTickCount { get; set; } = int.MaxValue;

	public void Regenerate(CoordinateRange range, Edge edge, PixelLength size, Paint paint, LabelStyle labelStyle)
	{
		var key = new TickKey(range.Min, range.Max, size.Length, CultureInfo.CurrentCulture.Name);

		if (!_cache.TryFind(key, out var ticks))
		{
			ticks = Generate(key);
			_cache.Add(key, ticks);
		}

		Ticks = ticks;
	}

	private static Tick[] Generate(TickKey key)
	{
		var positions = LogAxis.Ticks(key.MinLog, key.MaxLog, key.PixelLength);
		var ticks = new Tick[positions.Count];
		string? lastLabel = null;

		for (var index = 0; index < positions.Count; index++)
		{
			var position = positions[index].Position;
			if (!positions[index].IsMajor)
			{
				ticks[index] = Tick.Minor(position);
				continue;
			}

			var label = Math.Pow(10.0, position).ToString(LabelFormat, CultureInfo.CurrentCulture);
			if (label == lastLabel)
			{
				ticks[index] = Tick.Minor(position);
				continue;
			}

			ticks[index] = Tick.Major(position, label);
			lastLabel = label;
		}

		return ticks;
	}

	/// <summary>The culture is part of the key, because the labels are formatted under it.</summary>
	private readonly record struct TickKey(double MinLog, double MaxLog, float PixelLength, string CultureName);

	/// <summary>The last two tick results, used only by Regenerate under Plot.Sync.</summary>
	private sealed class TickCache
	{
		private Entry? _recent;
		private Entry? _older;

		public bool TryFind(TickKey key, [NotNullWhen(true)] out Tick[]? ticks)
		{
			if (_recent is { } recent && recent.Key == key)
			{
				ticks = recent.Ticks;
				return true;
			}

			if (_older is { } older && older.Key == key)
			{
				(_recent, _older) = (older, _recent);
				ticks = older.Ticks;
				return true;
			}

			ticks = null;
			return false;
		}

		public void Add(TickKey key, Tick[] ticks)
		{
			(_recent, _older) = (new Entry(key, ticks), _recent);
		}

		private sealed record Entry(TickKey Key, Tick[] Ticks);
	}
}
