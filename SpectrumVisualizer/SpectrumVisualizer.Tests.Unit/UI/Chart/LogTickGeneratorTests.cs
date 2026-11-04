using AwesomeAssertions;

using ScottPlot;

using SpectrumVisualizer.UI.Chart;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class LogTickGeneratorTests
{
	private const float AxisPixels = 400f;

	[Theory]
	[InlineData("en")]
	[InlineData("ru")]
	public void MajorsCarryTheValueAsAnIntegerCount(string cultureName)
	{
		using var culture = new CultureScope(cultureName);
		var generator = new LogTickGenerator();

		Regenerate(generator, new CoordinateRange(0.0, 5.0), AxisPixels);

		generator.Ticks.Where(tick => tick.IsMajor).Select(tick => tick.Label)
			.Should().Equal("1", "10", "100", "1000", "10000", "100000");
	}

	[Fact]
	public void ARepeatedLabelTurnsMinor()
	{
		using var culture = new CultureScope("en");
		var generator = new LogTickGenerator();
		var range = new CoordinateRange(0.0, Math.Log10(1.001));

		Regenerate(generator, range, AxisPixels);

		var majors = generator.Ticks.Where(tick => tick.IsMajor).Select(tick => tick.Label).ToList();
		majors.Should().NotBeEmpty().And.OnlyHaveUniqueItems();
		generator.Ticks.Should().HaveCount(
			LogAxis.Ticks(range.Min, range.Max, AxisPixels).Count,
			"a demoted major keeps its gridline as a minor");
		generator.Ticks.Count(tick => !tick.IsMajor).Should().BeGreaterThan(
			LogAxis.Ticks(range.Min, range.Max, AxisPixels).Count(tick => !tick.IsMajor),
			"labels of 1.0001 and 1.0002 round to the same text");
	}

	[Fact]
	public void TheSecondLengthOfAFrameReusesTheCachedTicks()
	{
		var generator = new LogTickGenerator();
		var range = new CoordinateRange(0.0, 5.0);

		Regenerate(generator, range, AxisPixels);
		var first = generator.Ticks;
		Regenerate(generator, range, 300f);
		Regenerate(generator, range, AxisPixels);

		generator.Ticks.Should().BeSameAs(first);
	}

	[Theory]
	[InlineData(double.NaN, 5.0)]
	[InlineData(0.0, double.PositiveInfinity)]
	[InlineData(double.NegativeInfinity, 0.0)]
	public void NothingForANonFiniteRange(double min, double max)
	{
		var generator = new LogTickGenerator();

		Regenerate(generator, new CoordinateRange(min, max), AxisPixels);

		generator.Ticks.Should().BeEmpty();
	}

	[Fact]
	public void IsLogarithmicOnlyWhileTheAxisCarriesThisGenerator()
	{
		using var plot = new Plot();
		var axis = plot.Axes.Left;

		LogTickGenerator.IsLogarithmic(axis).Should().BeFalse();
		axis.TickGenerator = new LogTickGenerator();
		LogTickGenerator.IsLogarithmic(axis).Should().BeTrue();
		LogTickGenerator.IsLogarithmic(null).Should().BeFalse();
	}

	private static void Regenerate(ITickGenerator generator, CoordinateRange range, float pixels)
	{
		using var paint = Paint.NewDisposablePaint();
		generator.Regenerate(range, Edge.Left, new PixelLength(pixels), paint, new LabelStyle());
	}
}
