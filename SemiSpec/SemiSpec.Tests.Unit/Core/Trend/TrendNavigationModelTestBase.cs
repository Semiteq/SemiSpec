using SemiSpec.Core.Trend;

namespace SemiSpec.Tests.Unit.Core.Trend;

public abstract class TrendNavigationModelTestBase
{
	private protected static readonly DateTimeOffset _origin = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
	private protected static readonly DateTimeOffset _firstSample = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);

	protected static TrendNavigationModel Model(bool isSticky, TimeSpan windowWidth)
	{
		var model = new TrendNavigationModel(_origin - windowWidth, _origin, _firstSample);
		if (!isSticky)
		{
			model.DetachSticky();
		}

		return model;
	}
}
