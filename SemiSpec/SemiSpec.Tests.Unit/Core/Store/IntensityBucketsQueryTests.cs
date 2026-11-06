using AwesomeAssertions;

using SemiSpec.Core.Store;

using Xunit;

namespace SemiSpec.Tests.Unit.Core.Store;

[Trait("Component", "Core")]
[Trait("Area", "Store")]
[Trait("Category", "Unit")]
public sealed class IntensityBucketsQueryTests : IntensityBucketsTestBase
{
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
		var most = IntensityQuery.Create(
			650, 10, _from, _from.AddMilliseconds(IntensityQuery.MaxColumns), oneMillisecond);

		tooMany.IsFailed.Should().BeTrue();
		most.IsSuccess.Should().BeTrue();
	}
}
