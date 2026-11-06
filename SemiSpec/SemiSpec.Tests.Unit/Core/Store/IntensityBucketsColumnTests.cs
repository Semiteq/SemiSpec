using AwesomeAssertions;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Store;

using Xunit;

namespace SemiSpec.Tests.Unit.Core.Store;

[Trait("Component", "Core")]
[Trait("Area", "Store")]
[Trait("Category", "Unit")]
public sealed class IntensityBucketsColumnTests : IntensityBucketsTestBase
{
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
		var buckets = new IntensityBuckets(Query(_from, _from.AddSeconds(1), TimeSpan.FromMilliseconds(100)));

		buckets.Add(_from.AddMilliseconds(offsetMs), 5.0);

		var counts = buckets.ToSeries().Columns.Select(c => c.Count).ToArray();
		var expected = new int[10];
		if (expectedColumn >= 0)
		{
			expected[expectedColumn] = 1;
		}

		counts.Should().Equal(expected);
	}

	[Theory]
	[InlineData(new[] { 4.0 }, 4.0, 4.0)]
	[InlineData(new[] { 4.0, 1.0, 7.0 }, 1.0, 7.0)]
	[InlineData(new[] { 0.0, 3.5, 2.0, 3.5 }, 0.0, 3.5)]
	public void AColumnKeepsMinMaxAndCount(double[] values, double expectedMin, double expectedMax)
	{
		var buckets = new IntensityBuckets(Query(_from, _from.AddSeconds(1), TimeSpan.FromSeconds(1)));

		foreach (var value in values)
		{
			buckets.Add(_from.AddMilliseconds(500), value);
		}

		buckets.ToSeries().Columns.Should().Equal(
			new IntensityColumn(
				_from, expectedMin, expectedMax, values.Length, 0, values[^1], 0, 0, 0, BoardState.None));
	}

	[Fact]
	public void LastIsTheLatestValueOfTheColumn()
	{
		var buckets = new IntensityBuckets(Query(_from, _from.AddSeconds(2), TimeSpan.FromSeconds(1)));

		buckets.Add(_from.AddMilliseconds(100), 3.0);
		buckets.Add(_from.AddMilliseconds(700), 9.0);
		buckets.Add(_from.AddMilliseconds(400), 1.0);
		buckets.Add(_from.AddMilliseconds(1200), 6.0);

		var columns = buckets.ToSeries().Columns;
		columns.Select(c => c.Last).Should().Equal(9.0, 6.0);
	}

	[Fact]
	public void AnEmptyColumnHasNoLastValue()
	{
		var buckets = new IntensityBuckets(Query(_from, _from.AddSeconds(1), TimeSpan.FromSeconds(1)));

		buckets.AddUncovered(_from);

		double.IsNaN(buckets.ToSeries().Columns.Single().Last).Should().BeTrue();
	}

	[Fact]
	public void BreaksFoldIntoTheirColumnsWithTheirLostAndSilenceCounts()
	{
		var buckets = new IntensityBuckets(Query(_from, _from.AddSeconds(3), TimeSpan.FromSeconds(1)));

		buckets.AddBreak(_from.AddMilliseconds(100), 2);
		buckets.AddBreak(_from.AddMilliseconds(900), 0);
		buckets.AddBreak(_from.AddMilliseconds(2500), 201);

		var columns = buckets.ToSeries().Columns;
		columns.Select(c => c.BreakCount).Should().Equal(2, 0, 1);
		columns.Select(c => c.LostCount).Should().Equal(2, 0, 201);
		columns.Select(c => c.NoSpectraCount).Should().Equal(1, 0, 0);
		columns.Should().AllSatisfy(c => c.Count.Should().Be(0, "a break adds no value"));
	}

	[Fact]
	public void FailuresInOneColumnMergeTheirBits()
	{
		var buckets = new IntensityBuckets(Query(_from, _from.AddSeconds(2), TimeSpan.FromSeconds(1)));

		buckets.AddFailure(_from.AddMilliseconds(100), BoardState.Atp2000MeasurementFailed);
		buckets.AddFailure(_from.AddMilliseconds(200), BoardState.Atp8000MeasurementFailed);

		buckets.ToSeries().Columns.Select(c => c.Failed).Should().Equal(
			BoardState.Atp2000MeasurementFailed | BoardState.Atp8000MeasurementFailed, BoardState.None);
	}

	[Fact]
	public void BreaksAndFailuresOutsideTheRangeAreIgnored()
	{
		var buckets = new IntensityBuckets(Query(_from, _from.AddSeconds(1), TimeSpan.FromSeconds(1)));

		buckets.AddBreak(_from.AddTicks(-1), 3);
		buckets.AddBreak(_from.AddSeconds(1), 3);
		buckets.AddFailure(_from.AddTicks(-1), BoardState.Atp8000MeasurementFailed);
		buckets.AddFailure(_from.AddSeconds(1), BoardState.Atp8000MeasurementFailed);

		var column = buckets.ToSeries().Columns.Single();
		column.BreakCount.Should().Be(0);
		column.LostCount.Should().Be(0);
		column.Failed.Should().Be(BoardState.None);
	}

	[Fact]
	public void AnEmptyColumnIsNaNWithCountZero()
	{
		var buckets = new IntensityBuckets(Query(_from, _from.AddSeconds(1), TimeSpan.FromMilliseconds(500)));

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
		var buckets = new IntensityBuckets(Query(_from, _from.AddSeconds(1), TimeSpan.FromSeconds(1)));

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
		var buckets = new IntensityBuckets(Query(_from, _from.AddSeconds(1), TimeSpan.FromSeconds(1)));

		buckets.AddUncovered(_from.AddSeconds(1));
		buckets.AddUncovered(_from.AddTicks(-1));

		buckets.ToSeries().Columns.Single().UncoveredCount.Should().Be(0);
	}
}
