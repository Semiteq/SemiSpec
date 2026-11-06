using AwesomeAssertions;

using SemiSpec.Core.Protocol;

using Xunit;

namespace SemiSpec.Tests.Unit.Core.Protocol;

[Trait("Component", "Core")]
[Trait("Area", "Protocol")]
[Trait("Category", "Unit")]
public sealed class SpectrometerTests
{
	[Theory]
	[InlineData(Spectrometer.Atp2000, "ATP-2000", BoardState.Atp2000Connected, BoardState.Atp2000MeasurementFailed)]
	[InlineData(Spectrometer.Atp8000, "ATP-8000", BoardState.Atp8000Connected, BoardState.Atp8000MeasurementFailed)]
	public void EachSpectrometerHasItsNameAndItsOwnStateBits(
		Spectrometer device,
		string displayName,
		BoardState connectedBit,
		BoardState failedBit)
	{
		device.DisplayName.Should().Be(displayName);
		device.ConnectedBit.Should().Be(connectedBit);
		device.FailedBit.Should().Be(failedBit);
	}

	[Fact]
	public void AnUnknownSpectrometerHasNoStateBits()
	{
		var unknown = (Spectrometer)0;

		FluentActions.Invoking(() => unknown.ConnectedBit).Should().Throw<ArgumentOutOfRangeException>();
		FluentActions.Invoking(() => unknown.FailedBit).Should().Throw<ArgumentOutOfRangeException>();
		FluentActions.Invoking(() => unknown.DisplayName).Should().Throw<ArgumentOutOfRangeException>();
	}
}
