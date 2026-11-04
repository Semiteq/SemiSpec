using AwesomeAssertions;

using SpectrumVisualizer.UI.Chart;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class LogAxisTests
{
	private const double MinimumMajorGap = 40.0;
	private const double Floor = -9.0;
	private const double ProjectionTolerance = 1e-12;
	private const double PositionTolerance = 1e-9;
	private const double RelativeValueTolerance = 1e-9;
	private const double GapTolerance = 1e-6;
	private static readonly TimeSpan _ticksDeadline = TimeSpan.FromSeconds(5);

	[Fact]
	public void ProjectPositiveValueGivesItsDecade()
	{
		LogAxis.Project(100.0, Floor).Should().BeApproximately(2.0, ProjectionTolerance);
		LogAxis.Project(1e-6, Floor).Should().BeApproximately(-6.0, ProjectionTolerance);
	}

	[Theory]
	[InlineData(0.0)]
	[InlineData(-5.0)]
	public void ProjectNonPositiveValueLandsOnTheFloor(double value)
	{
		LogAxis.Project(value, Floor).Should().Be(Floor);
	}

	[Fact]
	public void ProjectPositiveValueBelowTheFloorLandsOnTheFloor()
	{
		LogAxis.Project(1e-12, Floor).Should().Be(Floor);
	}

	[Fact]
	public void ProjectGapStaysNaN()
	{
		double.IsNaN(LogAxis.Project(double.NaN, Floor)).Should().BeTrue();
	}

	[Fact]
	public void TicksSixDecadesOver400PixelsPutMajorsOnDecadesAndMinorsInside()
	{
		var ticks = LogAxis.Ticks(-6.0, -1.0, 400.0);

		Positions(ticks, isMajor: true).Should().Equal([-6.0, -5.0, -4.0, -3.0, -2.0, -1.0], SamePosition);

		var expectedMinors = new List<double>();
		for (var decade = -6; decade <= -2; decade++)
		{
			for (var mantissa = 2; mantissa <= 9; mantissa++)
			{
				expectedMinors.Add(decade + Math.Log10(mantissa));
			}
		}

		Positions(ticks, isMajor: false).Should().Equal(expectedMinors, SamePosition);
	}

	[Fact]
	public void TicksTwelveDecadesOver200PixelsStrideDecadesAndMarkTheSkippedOnes()
	{
		var ticks = LogAxis.Ticks(-12.0, 0.0, 200.0);

		Positions(ticks, isMajor: true).Should().Equal([-12.0, -9.0, -6.0, -3.0, 0.0], SamePosition);
		Positions(ticks, isMajor: false).Should().Equal(
			[-11.0, -10.0, -8.0, -7.0, -5.0, -4.0, -2.0, -1.0], SamePosition);
	}

	[Theory]
	[InlineData(0.2, 5.0, new[] { 0.2, 0.5, 1.0, 2.0, 5.0 })]
	[InlineData(1.5, 14.0, new[] { 2.0, 5.0, 10.0 })]
	[InlineData(0.9, 1.4, new[] { 0.9, 1.0, 1.1, 1.2, 1.3, 1.4 })]
	[InlineData(1.05, 1.2, new[] { 1.06, 1.08, 1.1, 1.12, 1.14, 1.16, 1.18, 1.2 })]
	public void TicksRangeUnderAFewDecadesLabelsReadableValues(double bottom, double top, double[] expected)
	{
		var ticks = LogAxis.Ticks(Math.Log10(bottom), Math.Log10(top), 400.0);

		Values(ticks, isMajor: true).Should().Equal(expected, SameValue);
	}

	[Fact]
	public void TicksLinearStepsPlaceMinorsInThePartialIntervalsAtBothEnds()
	{
		var ticks = LogAxis.Ticks(Math.Log10(1.05), Math.Log10(1.19), 400.0);

		Values(ticks, isMajor: true).Should().Equal([1.06, 1.08, 1.1, 1.12, 1.14, 1.16, 1.18], SameValue);
		Values(ticks, isMajor: false).Should().Equal(
			[
				1.05, 1.055, 1.065, 1.07, 1.075, 1.085, 1.09, 1.095, 1.105, 1.11, 1.115,
				1.125, 1.13, 1.135, 1.145, 1.15, 1.155, 1.165, 1.17, 1.175, 1.185, 1.19
			],
			SameValue);
	}

	[Fact]
	public void TicksOneTwoFivePlaceMinorsOnTheMantissasItSkips()
	{
		var ticks = LogAxis.Ticks(Math.Log10(2.0), Math.Log10(50.0), 400.0);

		Values(ticks, isMajor: true).Should().Equal([2.0, 5.0, 10.0, 20.0, 50.0], SameValue);
		Values(ticks, isMajor: false).Should().Equal([3.0, 4.0, 6.0, 7.0, 8.0, 9.0, 30.0, 40.0], SameValue);
	}

	[Fact]
	public void TicksAxisTooShortForAnyFamilyMakesTheRangeEndsTheMajorsWithNoMinors()
	{
		var ticks = LogAxis.Ticks(-6.0, -1.0, 30.0);

		ticks.Should().Equal(new LogTick(-6.0, IsMajor: true), new LogTick(-1.0, IsMajor: true));
	}

	[Fact]
	public void TicksDecadesPlaceMinorsInThePartialDecadesAtBothEnds()
	{
		var ticks = LogAxis.Ticks(Math.Log10(1.5e-6), Math.Log10(5e-2), 400.0);

		Positions(ticks, isMajor: true).Should().Equal([-5.0, -4.0, -3.0, -2.0], SamePosition);
		var minors = Positions(ticks, isMajor: false);
		minors.Should().Contain(position => position < -5.0);
		minors.Should().Contain(position => position > -2.0);
	}

	[Fact]
	public void TicksSpanEndingOnADecadeKeepsTheDecadeAsTheTopMajor()
	{
		var maxLog = Math.Log10(0.1);

		var ticks = LogAxis.Ticks(Math.Log10(3e-3), maxLog, 400.0);

		var top = ticks.Last(tick => tick.IsMajor);
		top.Position.Should().BeApproximately(maxLog, PositionTolerance);
		ticks[^1].Should().Be(top);
	}

	[Theory]
	[InlineData(1e-6, 1e-1, 400.0)]
	[InlineData(1e-12, 1.0, 200.0)]
	[InlineData(2.0, 50.0, 400.0)]
	[InlineData(0.2, 5.0, 400.0)]
	[InlineData(1.5, 14.0, 400.0)]
	[InlineData(0.9, 1.4, 400.0)]
	[InlineData(1.05, 1.2, 400.0)]
	[InlineData(1.05, 1.19, 400.0)]
	[InlineData(1.5e-6, 5e-2, 400.0)]
	[InlineData(3e-3, 1e-1, 400.0)]
	public void TicksAnyRangeStayInsideItAscendingWithReadableMajors(double bottom, double top, double pixelLength)
	{
		var minLog = Math.Log10(bottom);
		var maxLog = Math.Log10(top);
		var pixelsPerDecade = pixelLength / (maxLog - minLog);

		var ticks = LogAxis.Ticks(minLog, maxLog, pixelLength);

		ticks.Select(tick => tick.Position).Should().AllSatisfy(
			position => position.Should().BeInRange(minLog, maxLog));
		ticks.Select(tick => tick.Position).Should().BeInAscendingOrder();
		ticks.Select(tick => tick.Position).Should().OnlyHaveUniqueItems();

		var majors = Positions(ticks, isMajor: true);
		majors.Should().HaveCountGreaterThanOrEqualTo(2);
		majors.Zip(majors.Skip(1), (lower, upper) => (upper - lower) * pixelsPerDecade)
			.Should().AllSatisfy(gap => gap.Should().BeGreaterThanOrEqualTo(MinimumMajorGap - GapTolerance));
	}

	[Theory]
	[InlineData(1.0, 1.0000000000000007, 400.0)]
	[InlineData(2.5, 2.5000000000000044, 300.0)]
	[InlineData(2.5, 2.5000000000000044, 400.0)]
	[InlineData(2.5, 2.5000000000000044, 900.0)]
	public async Task TicksSpanOfFloatNoiseReturnsTicksInsideTheRange(double bottom, double top, double pixelLength)
	{
		var minLog = Math.Log10(bottom);
		var maxLog = Math.Log10(top);

		var ticks = await TicksWithinDeadline(minLog, maxLog, pixelLength);

		ticks.Select(tick => tick.Position).Should().AllSatisfy(
			position => position.Should().BeInRange(minLog, maxLog));
	}

	[Fact]
	public async Task TicksDecadesBeyondExactIntegersReturnMajorsWithNoMinors()
	{
		var ticks = await TicksWithinDeadline(-1e20, 1e20, 400.0);

		ticks.Should().NotBeEmpty();
		ticks.Should().OnlyContain(tick => tick.IsMajor);
	}

	[Theory]
	[InlineData(-6.0, double.PositiveInfinity, 400.0)]
	[InlineData(double.NegativeInfinity, -1.0, 400.0)]
	[InlineData(double.NaN, -1.0, 400.0)]
	[InlineData(-3.0, -3.0, 400.0)]
	[InlineData(-1.0, -6.0, 400.0)]
	[InlineData(-6.0, -1.0, 0.0)]
	[InlineData(-6.0, -1.0, -400.0)]
	[InlineData(-6.0, -1.0, double.NaN)]
	public void TicksDegenerateInputGivesNoTicks(double minLog, double maxLog, double pixelLength)
	{
		LogAxis.Ticks(minLog, maxLog, pixelLength).Should().BeEmpty();
	}

	private static async Task<IReadOnlyList<LogTick>> TicksWithinDeadline(
		double minLog,
		double maxLog,
		double pixelLength)
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		return await Task.Run(() => LogAxis.Ticks(minLog, maxLog, pixelLength), cancellationToken)
			.WaitAsync(_ticksDeadline, cancellationToken);
	}

	private static List<double> Positions(IReadOnlyList<LogTick> ticks, bool isMajor)
	{
		return [.. ticks.Where(tick => tick.IsMajor == isMajor).Select(tick => tick.Position)];
	}

	private static List<double> Values(IReadOnlyList<LogTick> ticks, bool isMajor)
	{
		return [.. Positions(ticks, isMajor).Select(position => Math.Pow(10.0, position))];
	}

	private static bool SamePosition(double actual, double expected)
	{
		return Math.Abs(actual - expected) < PositionTolerance;
	}

	private static bool SameValue(double actual, double expected)
	{
		return Math.Abs(actual - expected) < expected * RelativeValueTolerance;
	}
}
