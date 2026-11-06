using AwesomeAssertions;

using SemiSpec.UI.Link;
using SemiSpec.UI.Spectrum;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Spectrum;

[Trait("Component", "UI")]
[Trait("Area", "Spectrum")]
[Trait("Category", "Unit")]
public sealed class SpectrumStalenessTests
{
	private static readonly DateTimeOffset _received = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

	[Theory]
	[InlineData(LinkState.Receiving, 10.0, false)]
	[InlineData(LinkState.Receiving, 10.5, true)]
	[InlineData(LinkState.Waiting, 1.0, true)]
	[InlineData(LinkState.Disconnected, 0.0, true)]
	public void AShownSpectrumIsStaleWhenTheLinkIsNotReceivingOrSilentPastTheLimit(
		LinkState state,
		double secondsSince,
		bool expected)
	{
		SpectrumStaleness.IsStale(state, _received, _received.AddSeconds(secondsSince)).Should().Be(expected);
	}

	[Fact]
	public void NoSpectrumIsNeverStale()
	{
		SpectrumStaleness.IsStale(LinkState.Disconnected, null, _received).Should().BeFalse();
	}
}
