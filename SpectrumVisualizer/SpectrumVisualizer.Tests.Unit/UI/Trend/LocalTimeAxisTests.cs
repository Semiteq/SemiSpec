using AwesomeAssertions;

using SpectrumVisualizer.UI.Trend;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Trend;

[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class LocalTimeAxisTests
{
	private static readonly DateTimeOffset _utc = new(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);

	[Fact]
	public void FromAxisReturnsUtcThatRoundTripsThroughToAxis()
	{
		var roundTripped = LocalTimeAxis.FromAxis(LocalTimeAxis.ToAxis(_utc));

		roundTripped.Offset.Should().Be(TimeSpan.Zero);
		roundTripped.Should().BeCloseTo(_utc, TimeSpan.FromMilliseconds(1.0));
	}

	[Fact]
	public void TwoOffsetsOfOneInstantMapToOneX()
	{
		var elsewhere = _utc.ToOffset(TimeSpan.FromHours(5));

		LocalTimeAxis.ToAxis(elsewhere).Should().Be(LocalTimeAxis.ToAxis(_utc));
	}

	[Fact]
	public void ToAxisIsTheLocalClockTime()
	{
		var local = _utc.ToLocalTime().DateTime;

		DateTime.FromOADate(LocalTimeAxis.ToAxis(_utc)).Should().BeCloseTo(local, TimeSpan.FromMilliseconds(1.0));
	}
}
