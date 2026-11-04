using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;
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
			new IntensityColumn(_from, expectedMin, expectedMax, values.Length, 0, values[^1], 0, 0, 0, BoardState.None));
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

	[Fact]
	public void TheColumnsSitOnTheEpochGridAndCoverEveryGridColumnTheRangeTouches()
	{
		var from = _from.AddMilliseconds(250);
		var query = Query(from, from.AddMilliseconds(1000), TimeSpan.FromMilliseconds(100));
		var buckets = new IntensityBuckets(query);

		buckets.Add(from, 1.0);
		buckets.Add(from.AddMilliseconds(999), 2.0);

		var series = buckets.ToSeries();
		series.From.Should().Be(from);
		series.To.Should().Be(from.AddMilliseconds(1000));
		series.ColumnWidth.Should().Be(TimeSpan.FromMilliseconds(100));
		series.Columns.Select(c => c.Start).Should().Equal(
			Enumerable.Range(0, 11).Select(i => _from.AddMilliseconds(200 + (100 * i))));
		series.Columns[0].Count.Should().Be(1, "the first column starts before From and holds From");
		series.Columns[^1].Count.Should().Be(1, "the last column ends after To and holds the last millisecond");
	}

	[Fact]
	public void ARangeEndingOnAColumnBoundaryAddsNoEmptyColumn()
	{
		var query = Query(_from, _from.AddSeconds(2), TimeSpan.FromSeconds(1));

		query.ColumnCount.Should().Be(2);
	}

	[Fact]
	public void TheLastRowOfADayLandsInTheLastColumn()
	{
		var buckets = new IntensityBuckets(Query(_from, _from.AddHours(24), TimeSpan.FromMilliseconds(86_400)));

		buckets.Add(_from.AddHours(24).AddTicks(-1), 1.0);

		var columns = buckets.ToSeries().Columns;
		columns.Should().HaveCount(1000);
		columns[^1].Count.Should().Be(1);
	}

	[Fact]
	public void TwoQueriesThatOverlapProduceEqualColumnsWhereBothHoldThemInFull()
	{
		var width = TimeSpan.FromMilliseconds(250);
		var first = Fold(Query(_from.AddMilliseconds(123), _from.AddMilliseconds(7456), width));
		var second = Fold(Query(_from.AddMilliseconds(3789), _from.AddSeconds(10), width));

		var shared = FullColumns(first).Intersect(FullColumns(second)).ToArray();

		shared.Should().HaveCount(13, "the full columns starting from 4 000 ms to 7 000 ms lie in both ranges");
	}

	[Fact]
	public void ALiveWindowShiftedByLessThanOneWidthKeepsTheBoundariesOfEveryFullColumn()
	{
		var width = TimeSpan.FromSeconds(1);
		var to = _from.AddMilliseconds(9300);
		var before = Fold(Query(to.AddSeconds(-10), to, width));
		var after = Fold(Query(to.AddSeconds(-10).AddMilliseconds(400), to.AddMilliseconds(400), width));

		after.Columns.Should().AllSatisfy(c => (c.Start.ToUnixTimeMilliseconds() % 1000).Should().Be(0));
		FullColumns(after).Should().HaveCount(9).And.Equal(FullColumns(before));
	}

	[Fact]
	public void TheQueryTruncatesFromAndToToWholeMilliseconds()
	{
		var to = _from.AddMilliseconds(10).AddTicks(3_000);

		var query = Query(_from.AddTicks(7_000), to, TimeSpan.FromMilliseconds(5));

		query.From.Should().Be(_from);
		query.To.Should().Be(_from.AddMilliseconds(10));
		query.ColumnCount.Should().Be(2);
	}

	[Theory]
	[InlineData(0L, 1000)]
	[InlineData(10_000L, -1)]
	[InlineData(15_000L, 1000)]
	[InlineData(10_000L, 0)]
	[InlineData(10_000L, -5)]
	public void TheQueryRejectsAWidthOutsideWholeMillisecondsOrAnEmptyRange(long widthTicks, int rangeMs)
	{
		var to = _from.AddMilliseconds(rangeMs);

		var result = IntensityQuery.Create(650, 10, _from, to, TimeSpan.FromTicks(widthTicks));

		result.IsFailed.Should().BeTrue();
	}

	[Fact]
	public void TheQueryRejectsARangeThatTruncatesToNothing()
	{
		var result = IntensityQuery.Create(
			650, 10, _from.AddTicks(2_000), _from.AddTicks(8_000), TimeSpan.FromMilliseconds(1));

		result.IsFailed.Should().BeTrue();
	}

	[Fact]
	public void TheQueryRejectsMoreThanTheMaximumColumns()
	{
		var oneMillisecond = TimeSpan.FromMilliseconds(1);

		var tooMany = IntensityQuery.Create(650, 10, _from, _from.AddHours(24), oneMillisecond);
		var most = IntensityQuery.Create(650, 10, _from, _from.AddMilliseconds(IntensityQuery.MaxColumns), oneMillisecond);

		tooMany.IsFailed.Should().BeTrue();
		most.IsSuccess.Should().BeTrue();
	}

	private static IntensityQuery Query(DateTimeOffset from, DateTimeOffset to, TimeSpan width)
	{
		return IntensityQuery.Create(650, 10, from, to, width).Value;
	}

	/// <summary>Folds one row every 37 ms of the 10 s from _from, valued by its offset, into the query's columns.</summary>
	private static IntensitySeries Fold(IntensityQuery query)
	{
		var buckets = new IntensityBuckets(query);
		for (var offsetMs = 0; offsetMs < 10_000; offsetMs += 37)
		{
			buckets.Add(_from.AddMilliseconds(offsetMs), offsetMs);
		}

		return buckets.ToSeries();
	}

	private static IEnumerable<IntensityColumn> FullColumns(IntensitySeries series)
	{
		return series.Columns.Where(c => c.Start >= series.From && c.Start + series.ColumnWidth <= series.To);
	}
}
