using AwesomeAssertions;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Store;
using SemiSpec.Core.Trend;

using Xunit;

namespace SemiSpec.Tests.Unit.Core.Trend;

[Trait("Component", "Core")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendColumnsTests
{
	private static readonly DateTimeOffset _start = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
	private static readonly TimeSpan _width = TimeSpan.FromSeconds(2);

	[Fact]
	public void FailedColumnsLessThanFiveSecondsApartJoinIntoOneRange()
	{
		var failed = BoardState.Atp8000MeasurementFailed;
		var columns = Enumerable.Range(0, 10)
			.Select(index => Empty(_start + (index * _width)) with
			{
				Failed = index is 0 or 2 or 6 ? failed : BoardState.None,
			})
			.ToArray();

		var ranges = TrendColumns.FailureRanges(new IntensitySeries(_start, _start + (10 * _width), _width, columns));

		ranges.Should().Equal(
			new TimeRange(_start, _start + (3 * _width)),
			new TimeRange(_start + (6 * _width), _start + (7 * _width)));
	}

	[Theory]
	[InlineData(600_000, 1000.0, 600)]
	[InlineData(600_000, 100.0, 2344)]
	[InlineData(600_000, 5000.0, 293)]
	[InlineData(1_000, 1000.0, 1)]
	[InlineData(10, 1000.0, 1)]
	public void TheColumnWidthIsWholeMilliseconds(long spanMilliseconds, double pixels, long expectedMilliseconds)
	{
		var width = TrendColumns.ColumnWidth(TimeSpan.FromMilliseconds(spanMilliseconds), pixels);

		width.Should().Be(TimeSpan.FromMilliseconds(expectedMilliseconds));
		(width.Ticks % TimeSpan.TicksPerMillisecond).Should().Be(0);
	}

	private static IntensityColumn Empty(DateTimeOffset start)
	{
		return new IntensityColumn(start, double.NaN, double.NaN, 0, 0, double.NaN, 0, 0, 0, BoardState.None);
	}
}
