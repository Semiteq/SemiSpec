using SemiSpec.UI.Localization;

namespace SemiSpec.UI.Trend;

public sealed record TrendPreset(string Label, TimeSpan Width);

/// <summary>The time window widths the operator picks from; the window opens at <see cref="Default"/>.</summary>
public static class TrendPresets
{
	/// <summary>A new list per read, so the labels follow the culture in force.</summary>
	public static IReadOnlyList<TrendPreset> All =>
	[
		Minutes(1),
		Minutes(10),
		Hours(1),
		Hours(6),
		Hours(24),
	];

	public static TrendPreset Default => All[1];

	private static TrendPreset Minutes(int minutes)
	{
		return new TrendPreset(
			Resources.FormatTrendPresetMinutesFormat(minutes.ToString(Resources.Culture)),
			TimeSpan.FromMinutes(minutes));
	}

	private static TrendPreset Hours(int hours)
	{
		return new TrendPreset(
			Resources.FormatTrendPresetHoursFormat(hours.ToString(Resources.Culture)),
			TimeSpan.FromHours(hours));
	}
}
