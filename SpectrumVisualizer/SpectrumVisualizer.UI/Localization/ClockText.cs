using System.Globalization;

namespace SpectrumVisualizer.UI.Localization;

/// <summary>The fixed local time of day of the trend readout and the historical line, in the invariant culture.</summary>
public static class ClockText
{
	private const string TimeOfDayFormat = "HH:mm:ss";

	public static string TimeOfDay(DateTimeOffset moment)
	{
		return moment.ToLocalTime().ToString(TimeOfDayFormat, CultureInfo.InvariantCulture);
	}
}
