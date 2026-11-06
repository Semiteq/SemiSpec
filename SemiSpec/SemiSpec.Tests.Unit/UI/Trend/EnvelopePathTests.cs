using AwesomeAssertions;

using SemiSpec.UI.Trend;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Trend;

[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class EnvelopePathTests
{
	// The last two rows have no overlap: the boundary column stays alone, which draws nothing the viewport can show.
	[Theory]
	[InlineData(0, 0.0, 10.0, 0, 0)]
	[InlineData(5, 1.5, 2.5, 1, 4)]
	[InlineData(5, 1.0, 3.0, 0, 5)]
	[InlineData(5, -10.0, 10.0, 0, 5)]
	[InlineData(5, -3.0, -1.0, 0, 1)]
	[InlineData(5, 9.0, 11.0, 4, 5)]
	public void VisibleRangeKeepsOneColumnBeyondEachEdgeOfTheWindow(
		int columnCount,
		double from,
		double to,
		int expectedStart,
		int expectedEnd)
	{
		EnvelopePath.VisibleRange(Lattice(columnCount), from, to).Should().Be((expectedStart, expectedEnd));
	}

	[Fact]
	public void BuildDegenerateColumnIsOnePoint()
	{
		var points = Build([new EnvelopeColumn(0.0, 4.0, 4.0, 4.0)]);

		points.Should().Equal(new EnvelopePoint(0.0, 4.0, true));
	}

	[Fact]
	public void BuildFirstColumnEntersAtItsMinAndEndsOnItsCenter()
	{
		var points = Build([new EnvelopeColumn(0.0, 1.0, 2.0, 1.5)]);

		points.Should().Equal(
			new EnvelopePoint(0.0, 1.0, true),
			new EnvelopePoint(0.0, 2.0, false),
			new EnvelopePoint(0.0, 1.5, false));
	}

	[Fact]
	public void BuildColumnAboveThePreviousPointEntersAtItsMin()
	{
		var points = Build(
		[
			new EnvelopeColumn(0.0, 0.0, 0.0, 0.0),
			new EnvelopeColumn(1.0, 5.0, 10.0, 7.0)
		]);

		points.Should().Equal(
			new EnvelopePoint(0.0, 0.0, true),
			new EnvelopePoint(1.0, 5.0, false),
			new EnvelopePoint(1.0, 10.0, false),
			new EnvelopePoint(1.0, 7.0, false));
	}

	[Fact]
	public void BuildColumnBelowThePreviousPointEntersAtItsMax()
	{
		var points = Build(
		[
			new EnvelopeColumn(0.0, 10.0, 10.0, 10.0),
			new EnvelopeColumn(1.0, 2.0, 3.0, 2.5)
		]);

		points.Should().Equal(
			new EnvelopePoint(0.0, 10.0, true),
			new EnvelopePoint(1.0, 3.0, false),
			new EnvelopePoint(1.0, 2.0, false),
			new EnvelopePoint(1.0, 2.5, false));
	}

	[Fact]
	public void BuildColumnEndingOnItsFarExtremeAddsNoVertexForItsCenter()
	{
		var points = Build(
		[
			new EnvelopeColumn(0.0, 0.0, 0.0, 0.0),
			new EnvelopeColumn(1.0, 5.0, 10.0, 10.0)
		]);

		points.Should().Equal(
			new EnvelopePoint(0.0, 0.0, true),
			new EnvelopePoint(1.0, 5.0, false),
			new EnvelopePoint(1.0, 10.0, false));
	}

	[Fact]
	public void AGapColumnBreaksThePath()
	{
		var points = Build(
		[
			new EnvelopeColumn(0.0, 1.0, 2.0, 1.5),
			new EnvelopeColumn(1.0, double.NaN, double.NaN, double.NaN),
			new EnvelopeColumn(2.0, 3.0, 4.0, 3.5)
		]);

		points.Should().Equal(
			new EnvelopePoint(0.0, 1.0, true),
			new EnvelopePoint(0.0, 2.0, false),
			new EnvelopePoint(0.0, 1.5, false),
			new EnvelopePoint(2.0, 3.0, true),
			new EnvelopePoint(2.0, 4.0, false),
			new EnvelopePoint(2.0, 3.5, false));
	}

	[Fact]
	public void BuildFromALaterFirstColumnSeedsThePreviousCenter()
	{
		var columns = Lattice(5);
		var whole = new List<EnvelopePoint>();
		var tail = new List<EnvelopePoint>();

		EnvelopePath.Build(columns, 0, 5, whole);
		EnvelopePath.Build(columns, 2, 5, tail);

		tail.Skip(1).Should().Equal(whole.Where(point => point.X >= 2.0).Skip(1));
	}

	[Fact]
	public void OnlyADegenerateColumnBetweenGapsIsLone()
	{
		var points = Build(
		[
			new EnvelopeColumn(0.0, 4.0, 4.0, 4.0),
			new EnvelopeColumn(1.0, double.NaN, double.NaN, double.NaN),
			new EnvelopeColumn(2.0, 1.0, 2.0, 1.5),
			new EnvelopeColumn(3.0, double.NaN, double.NaN, double.NaN),
			new EnvelopeColumn(4.0, 5.0, 5.0, 5.0)
		]);

		Enumerable.Range(0, points.Count).Select(index => EnvelopePath.IsLone(points, index))
			.Should().Equal(true, false, false, false, true);
	}

	private static List<EnvelopePoint> Build(IReadOnlyList<EnvelopeColumn> columns)
	{
		var points = new List<EnvelopePoint>();
		EnvelopePath.Build(columns, 0, columns.Count, points);

		return points;
	}

	private static List<EnvelopeColumn> Lattice(int count)
	{
		var columns = new List<EnvelopeColumn>(count);
		for (var index = 0; index < count; index++)
		{
			columns.Add(new EnvelopeColumn(index, index, index + 1.0, index + 0.5));
		}

		return columns;
	}
}
