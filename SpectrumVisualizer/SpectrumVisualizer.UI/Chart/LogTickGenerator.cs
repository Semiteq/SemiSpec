using System.Globalization;

using ScottPlot;

namespace SpectrumVisualizer.UI.Chart;

/// <summary>Ticks of a log10 axis whose limits are in decades; a major reads its value under the current culture.</summary>
public sealed class LogTickGenerator : ITickGenerator
{
	private const string LabelFormat = "0.###";

	private readonly TickCache<TickKey> _cache = new();

	public Tick[] Ticks { get; private set; } = [];

	public int MaxTickCount { get; set; } = int.MaxValue;

	public void Regenerate(CoordinateRange range, Edge edge, PixelLength size, Paint paint, LabelStyle labelStyle)
	{
		var key = new TickKey(range.Min, range.Max, size.Length);

		if (!_cache.TryFind(key, out var ticks))
		{
			ticks = Generate(key);
			_cache.Add(key, ticks);
		}

		Ticks = ticks;
	}

	/// <summary>An axis runs in decades while this generator ticks it; ScottPlot leaves an unbound axis null.</summary>
	internal static bool IsLogarithmic(IYAxis? axis)
	{
		return axis?.TickGenerator is LogTickGenerator;
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

	private readonly record struct TickKey(double MinLog, double MaxLog, float PixelLength);
}
