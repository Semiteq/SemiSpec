using AwesomeAssertions;

using SpectrumVisualizer.Core.Store;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Store;

[Trait("Component", "Core")]
[Trait("Area", "Store")]
[Trait("Category", "Unit")]
public sealed class IntensityBucketsTests
{
	private static readonly DateTimeOffset _from = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

	[Theory]
	[InlineData(0, 0)]
	[InlineData(99, 0)]
	[InlineData(100, 1)]
	[InlineData(250, 2)]
	[InlineData(999, 9)]
	[InlineData(1000, -1)]
	[InlineData(-1, -1)]
	public void ARowLandsInTheColumnOfItsTime(int offsetMs, int expectedColumn)
	{
		var buckets = new IntensityBuckets(Query(TimeSpan.FromSeconds(1), columns: 10));

		buckets.Add(_from.AddMilliseconds(offsetMs), 5.0);

		var counts = buckets.ToSeries().Columns.Select(c => c.Count).ToArray();
		var expected = new int[10];
		if (expectedColumn >= 0)
		{
			expected[expectedColumn] = 1;
		}

		counts.Should().Equal(expected);
	}

	[Fact]
	public void ARowNearToStaysInTheLastColumn()
	{
		var buckets = new IntensityBuckets(Query(TimeSpan.FromMilliseconds(10), columns: 3));

		buckets.Add(_from.AddMilliseconds(9), 5.0);

		buckets.ColumnWidth.Should().Be(TimeSpan.FromMilliseconds(3));
		buckets.ToSeries().Columns.Select(c => c.Count).Should().Equal(0, 0, 1);
	}

	[Theory]
	[InlineData(new[] { 4.0 }, 4.0, 4.0)]
	[InlineData(new[] { 4.0, 1.0, 7.0 }, 1.0, 7.0)]
	[InlineData(new[] { 0.0, 3.5, 2.0, 3.5 }, 0.0, 3.5)]
	public void AColumnKeepsMinMaxAndCount(double[] values, double expectedMin, double expectedMax)
	{
		var buckets = new IntensityBuckets(Query(TimeSpan.FromSeconds(1), columns: 1));

		foreach (var value in values)
		{
			buckets.Add(_from.AddMilliseconds(500), value);
		}

		buckets.ToSeries().Columns.Should().Equal(new IntensityColumn(expectedMin, expectedMax, values.Length, 0));
	}

	[Fact]
	public void AnEmptyColumnIsNaNWithCountZero()
	{
		var buckets = new IntensityBuckets(Query(TimeSpan.FromSeconds(1), columns: 2));

		buckets.Add(_from, 5.0);

		var empty = buckets.ToSeries().Columns[1];
		empty.Min.Should().Be(double.NaN);
		empty.Max.Should().Be(double.NaN);
		empty.Count.Should().Be(0);
		empty.UncoveredCount.Should().Be(0);
	}

	[Theory]
	[InlineData(0, false)]
	[InlineData(2, false)]
	[InlineData(1, true)]
	public void AnUncoveringRowCountsAsUncoveredOnly(int uncoveredRows, bool withCoveredRow)
	{
		var buckets = new IntensityBuckets(Query(TimeSpan.FromSeconds(1), columns: 1));

		for (var i = 0; i < uncoveredRows; i++)
		{
			buckets.AddUncovered(_from.AddMilliseconds(100));
		}

		if (withCoveredRow)
		{
			buckets.Add(_from.AddMilliseconds(200), 8.0);
		}

		var column = buckets.ToSeries().Columns.Single();
		column.UncoveredCount.Should().Be(uncoveredRows);
		column.Count.Should().Be(withCoveredRow ? 1 : 0);
		column.Max.Should().Be(withCoveredRow ? 8.0 : double.NaN);
	}

	[Fact]
	public void AnUncoveringRowOutsideTheRangeIsSkipped()
	{
		var buckets = new IntensityBuckets(Query(TimeSpan.FromSeconds(1), columns: 1));

		buckets.AddUncovered(_from.AddSeconds(1));
		buckets.AddUncovered(_from.AddTicks(-1));

		buckets.ToSeries().Columns.Single().UncoveredCount.Should().Be(0);
	}

	[Theory]
	[InlineData(1000, 10, 100)]
	[InlineData(10, 3, 3)]
	[InlineData(2, 5, 1)]
	[InlineData(1999, 1000, 1)]
	[InlineData(86_400_000, 1000, 86_400)]
	public void TheColumnWidthIsTheRangeOverTheColumnsFlooredAtOneMillisecond(int rangeMs, int columns, int expectedMs)
	{
		var buckets = new IntensityBuckets(Query(TimeSpan.FromMilliseconds(rangeMs), columns));

		buckets.ColumnWidth.Should().Be(TimeSpan.FromMilliseconds(expectedMs));
		buckets.ToSeries().ColumnWidth.Should().Be(TimeSpan.FromMilliseconds(expectedMs));
	}

	[Fact]
	public void TheLastRowOfADayLandsInTheLastColumn()
	{
		var buckets = new IntensityBuckets(Query(TimeSpan.FromHours(24), columns: 1000));

		buckets.Add(_from.AddHours(24).AddTicks(-1), 1.0);

		buckets.ToSeries().Columns[^1].Count.Should().Be(1);
	}

	[Theory]
	[InlineData(0, 1000)]
	[InlineData(-1, 1000)]
	[InlineData(10, 0)]
	[InlineData(10, -5)]
	public void TheQueryRejectsAnEmptyRangeOrNoColumns(int columns, int rangeMs)
	{
		var result = IntensityQuery.Create(650, 10, _from, _from.AddMilliseconds(rangeMs), columns);

		result.IsFailed.Should().BeTrue();
	}

	[Fact]
	public void TheSeriesStartsAtTheQueryStart()
	{
		var series = new IntensityBuckets(Query(TimeSpan.FromSeconds(1), columns: 4)).ToSeries();

		series.From.Should().Be(_from);
		series.Columns.Should().HaveCount(4);
	}

	private static IntensityQuery Query(TimeSpan range, int columns)
	{
		return IntensityQuery.Create(650, 10, _from, _from + range, columns).Value;
	}
}
