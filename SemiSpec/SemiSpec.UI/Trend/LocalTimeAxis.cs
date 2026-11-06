namespace SemiSpec.UI.Trend;

// The single conversion between the UTC time domain and the chart's local-time axis, so plotted X, axis limits,
// cursor X and the navigation window never double-convert. Not perfectly invertible across a DST boundary: a
// round trip inside the repeated or skipped hour can shift by up to one hour.
internal static class LocalTimeAxis
{
	public static double ToAxis(DateTimeOffset moment)
	{
		return moment.ToLocalTime().DateTime.ToOADate();
	}

	public static DateTimeOffset FromAxis(double oaDate)
	{
		var local = DateTime.SpecifyKind(DateTime.FromOADate(oaDate), DateTimeKind.Local);

		return new DateTimeOffset(local).ToUniversalTime();
	}
}
