using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.UI.Trend;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Trend;

[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class EnvelopeColumnTests
{
	private static readonly DateTimeOffset _start = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
	private static readonly TimeSpan _width = TimeSpan.FromSeconds(2);
	[Fact]
	public void AColumnWithValuesBecomesAnEnvelopeColumnAtItsCentre()
	{
		var columns = Map(Column(_start, min: 3, max: 7, count: 2, last: 5));

		columns.Should().Equal(new EnvelopeColumn(Axis(_start + TimeSpan.FromSeconds(1)), 3, 7, 5));
	}

	[Fact]
	public void AnEmptyColumnWithoutABreakIsLeftOut()
	{
		var columns = Map(
			Column(_start, min: 1, max: 1, count: 1, last: 1),
			Empty(_start + _width),
			Column(_start + (2 * _width), min: 2, max: 2, count: 1, last: 2));

		columns.Select(column => column.Min).Should().Equal(1, 2);
	}

	[Fact]
	public void AnUncoveredEmptyColumnBecomesAGap()
	{
		var columns = Map(Empty(_start) with { UncoveredCount = 1 });

		columns.Should().ContainSingle();
		columns[0].X.Should().Be(Axis(_start));
		double.IsNaN(columns[0].Min).Should().BeTrue();
		double.IsNaN(columns[0].Max).Should().BeTrue();
	}

	[Fact]
	public void AnEmptyColumnWithABreakBecomesAGap()
	{
		var columns = Map(Empty(_start) with { BreakCount = 1 });

		columns.Should().ContainSingle();
		double.IsNaN(columns[0].Min).Should().BeTrue();
	}

	[Fact]
	public void ABreakInAColumnWithValuesPutsAGapBeforeIt()
	{
		var columns = Map(Column(_start, min: 3, max: 7, count: 2, last: 5) with { BreakCount = 1, LostCount = 2 });

		columns.Should().HaveCount(2);
		columns[0].X.Should().Be(Axis(_start));
		double.IsNaN(columns[0].Min).Should().BeTrue();
		columns[1].Should().Be(new EnvelopeColumn(Axis(_start + TimeSpan.FromSeconds(1)), 3, 7, 5));
	}

	[Fact]
	public void AFailureAloneAddsNoColumn()
	{
		var columns = Map(Empty(_start) with { Failed = BoardState.Atp8000MeasurementFailed });

		columns.Should().BeEmpty();
	}

	private static IReadOnlyList<EnvelopeColumn> Map(params IntensityColumn[] columns)
	{
		var series = new IntensitySeries(_start, _start + (columns.Length * _width), _width, columns);
		return EnvelopeColumn.Map(series, Axis);
	}

	private static double Axis(DateTimeOffset moment)
	{
		return moment.ToUnixTimeMilliseconds();
	}

	private static IntensityColumn Column(DateTimeOffset start, double min, double max, int count, double last)
	{
		return new IntensityColumn(start, min, max, count, 0, last, 0, 0, 0, BoardState.None);
	}

	private static IntensityColumn Empty(DateTimeOffset start)
	{
		return new IntensityColumn(start, double.NaN, double.NaN, 0, 0, double.NaN, 0, 0, 0, BoardState.None);
	}
}
