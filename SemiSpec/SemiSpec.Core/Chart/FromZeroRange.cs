namespace SemiSpec.Core.Chart;

/// <summary>The vertical range of the From zero mode, shared by the spectrum, the trend and the overview band.</summary>
public static class FromZeroRange
{
	private const double Headroom = 1.05;
	private const double EmptyTop = 1.0;

	/// <summary>Zero to the maximum plus 5 % of it; 0..1 when the maximum is not above zero.</summary>
	public static (double Bottom, double Top) Over(double max)
	{
		return (0.0, max > 0.0 && double.IsFinite(max) ? max * Headroom : EmptyTop);
	}
}
