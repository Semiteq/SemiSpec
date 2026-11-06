using AwesomeAssertions;

using ScottPlot;

using SpectrumVisualizer.Core.Chart;
using SpectrumVisualizer.UI.Chart;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class LogTickGeneratorTests
{
	private const float AxisPixels = 400f;

	[Fact]
	public void MajorsOverDecadesCarryTheValueAsAnIntegerCount()
	{
		using var culture = new CultureScope("en");
		var generator = new LogTickGenerator();

		Regenerate(generator, new CoordinateRange(0.0, 5.0), AxisPixels);

		MajorLabels(generator).Should().Equal("1", "10", "100", "1000", "10000", "100000");
	}

	[Theory]
	[InlineData("en", "1 1.2 1.4 1.6 1.8 2")]
	[InlineData("ru", "1 1,2 1,4 1,6 1,8 2")]
	public void FractionalMajorsUseTheCultureDecimalSeparator(string cultureName, string expected)
	{
		using var culture = new CultureScope(cultureName);
		var generator = new LogTickGenerator();

		Regenerate(generator, new CoordinateRange(0.0, Math.Log10(2.0)), AxisPixels);

		string.Join(" ", MajorLabels(generator)).Should().Be(expected);
	}

	[Fact]
	public void ACultureChangeRegeneratesTheCachedLabels()
	{
		var generator = new LogTickGenerator();
		var range = new CoordinateRange(0.0, Math.Log10(2.0));
		using (new CultureScope("en"))
		{
			Regenerate(generator, range, AxisPixels);
		}

		var english = MajorLabels(generator);
		using (new CultureScope("ru"))
		{
			Regenerate(generator, range, AxisPixels);
		}

		MajorLabels(generator).Should().NotEqual(english);
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

	private static List<string> MajorLabels(LogTickGenerator generator)
	{
		return [.. generator.Ticks.Where(tick => tick.IsMajor).Select(tick => tick.Label)];
	}

	private static void Regenerate(ITickGenerator generator, CoordinateRange range, float pixels)
	{
		using var paint = Paint.NewDisposablePaint();
		generator.Regenerate(range, Edge.Left, new PixelLength(pixels), paint, new LabelStyle());
	}
}
