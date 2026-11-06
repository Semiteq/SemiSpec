using AwesomeAssertions;

using SpectrumVisualizer.Core.Store;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Store;

[Trait("Component", "Core")]
[Trait("Area", "Store")]
[Trait("Category", "Unit")]
public sealed class IntensityBucketsGridTests : IntensityBucketsTestBase
{
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
}
