using SemiSpec.Core.Protocol;
using SemiSpec.Core.Store;

namespace SemiSpec.Tests.Unit.Core.Trend;

public abstract class OverviewGeometryTestBase
{
	protected static readonly DateTimeOffset First = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);
	protected static readonly DateTimeOffset Last = new(2026, 6, 20, 0, 0, 0, TimeSpan.Zero);

	protected static IntensitySeries Band(params (double Days, double Min, double Max, double Last)[] columns)
	{
		IntensityColumn[] bandColumns =
		[
			.. columns.Select(column => new IntensityColumn(
				First.AddDays(column.Days),
				column.Min,
				column.Max,
				double.IsNaN(column.Min) ? 0 : 1,
				0,
				column.Last,
				0,
				0,
				0,
				BoardState.None))
		];

		return new IntensitySeries(
			First.AddDays(columns[0].Days),
			First.AddDays(columns[^1].Days + 1),
			TimeSpan.FromDays(1),
			bandColumns);
	}
}
