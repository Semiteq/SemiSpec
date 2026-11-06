using AwesomeAssertions;

using SemiSpec.Core.Chart;

using Xunit;

namespace SemiSpec.Tests.Unit.Core.Chart;

[Trait("Component", "Core")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class FromZeroRangeTests
{
	[Fact]
	public void APositiveMaximumRunsFromZeroToItPlusFivePercent()
	{
		FromZeroRange.Over(200.0).Should().Be((0.0, 210.0));
	}

	[Theory]
	[InlineData(0.0)]
	[InlineData(-5.0)]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	[InlineData(double.NegativeInfinity)]
	public void AMaximumThatGivesNoRangeRunsFromZeroToOne(double max)
	{
		FromZeroRange.Over(max).Should().Be((0.0, 1.0));
	}
}
