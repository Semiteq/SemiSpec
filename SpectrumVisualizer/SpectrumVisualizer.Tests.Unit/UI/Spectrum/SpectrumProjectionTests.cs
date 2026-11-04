using AwesomeAssertions;

using SpectrumVisualizer.UI.Spectrum;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Spectrum;

[Trait("Component", "UI")]
[Trait("Area", "Spectrum")]
[Trait("Category", "Unit")]
public sealed class SpectrumProjectionTests
{
	private const double Tolerance = 1e-4;

	public static TheoryData<ushort[], bool, bool, double[], double, bool> ProjectionRows => new()
	{
		{ [0, 1000, 31000], false, false, [0, 1000, 31000], 31000, false },
		{ [0, 1000, 31000], true, false, [31000, 30000, 0], 31000, false },
		{ [0, 1, 10, 1000], false, true, [0, 0, 1, 3], 1000, false },
		{ [1000, 31000], true, true, [Math.Log10(30000), 0], 30000, false },
		{ [500, 500], true, false, [0, 0], 0, false },
		{ [65535], false, true, [4.8165], 65535, false },
		{ [0, 0, 0], false, false, [0, 0, 0], 0, true },
		{ [0, 0, 0], true, false, [0, 0, 0], 0, true },
		{ [0, 0, 0], false, true, [0, 0, 0], 0, true },
		{ [0, 0, 0], true, true, [0, 0, 0], 0, true },
	};

	[Theory]
	[MemberData(nameof(ProjectionRows))]
	public void FillInvertsOverTheFrameMaximumThenTakesTheFlooredLog(
		ushort[] pixels,
		bool inverted,
		bool logarithmic,
		double[] expectedYs,
		double expectedRawMax,
		bool expectedDark)
	{
		var ys = new double[pixels.Length];

		var projected = SpectrumProjection.Fill(pixels, inverted, logarithmic, ys);

		ys.Should().Equal(expectedYs, (actual, expected) => Math.Abs(actual - expected) < Tolerance);
		projected.Should().Be(new ProjectedSpectrum(expectedRawMax, expectedDark));
	}
}
