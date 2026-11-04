using AwesomeAssertions;

using SpectrumVisualizer.Core.Calibration;
using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Spectrum;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Spectrum;

[Trait("Component", "Core")]
[Trait("Area", "Spectrum")]
[Trait("Category", "Unit")]
public sealed class SpectrumScaleTests
{
	private const double Tolerance = 1e-9;
	private static readonly double[] _xs = [400.0, 500.0, 600.0];

	public static TheoryData<double[], bool, double, double> FollowingRows => new()
	{
		{ [1000, 31000, 3000], false, -500, 32500 },
		{ [3, 4, 4.5], true, 2.925, 4.575 },
		{ [700, 700, 700], false, 699, 701 },
		{ [3, 3, 3], true, 2.5, 3.5 },
		{ [0, 0, 0], false, -1, 1 },
		{ [0, 0, 0], true, -0.5, 0.5 },
	};

	public static TheoryData<double, bool, double> StuckRows => new()
	{
		{ 31000, false, 37200 },
		{ 31000, true, Math.Log10(37200) },
		{ 0, false, 1 },
		{ 1, true, 1 },
		{ 0, true, 1 },
	};

	[Theory]
	[MemberData(nameof(FollowingRows))]
	public void FollowingPadsFivePercentOfTheSpanOrWidensAFlatRange(
		double[] ys,
		bool logarithmic,
		double expectedBottom,
		double expectedTop)
	{
		var limits = SpectrumScale.Following(_xs, ys, Display(logarithmic));

		limits.X.Should().Be(new AxisRange(400.0, 600.0));
		limits.Y.Min.Should().BeApproximately(expectedBottom, Tolerance);
		limits.Y.Max.Should().BeApproximately(expectedTop, Tolerance);
	}

	[Theory]
	[MemberData(nameof(StuckRows))]
	public void StuckToZeroRunsFromZeroToTheHeadroomOverTheRawMaximum(
		double rawMax,
		bool logarithmic,
		double expectedTop)
	{
		var limits = SpectrumScale.StuckToZero(_xs, rawMax, Display(logarithmic));

		limits.X.Should().Be(new AxisRange(400.0, 600.0));
		limits.Y.Min.Should().Be(0.0);
		limits.Y.Max.Should().BeApproximately(expectedTop, Tolerance);
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(true, true)]
	public void AnAllZeroSpectrumGivesFiniteLimitsInEveryMode(bool inverted, bool logarithmic)
	{
		var xs = Wavelengths(Spectrometer.Atp2000);
		var ys = new double[xs.Length];
		var display = new SpectrumDisplay(inverted, logarithmic);
		var projected = SpectrumProjection.Fill(new ushort[xs.Length], display, ys);

		SpectrumLimits[] all =
		[
			SpectrumScale.Following(xs, ys, display),
			SpectrumScale.StuckToZero(xs, projected.RawMax, display),
		];

		foreach (var limits in all)
		{
			double[] values = [limits.X.Min, limits.X.Max, limits.Y.Min, limits.Y.Max];
			values.Should().OnlyContain(value => double.IsFinite(value));
			limits.Y.Max.Should().BeGreaterThan(limits.Y.Min);
		}
	}

	private static SpectrumDisplay Display(bool logarithmic)
	{
		return new SpectrumDisplay(IsInverted: false, logarithmic);
	}

	private static double[] Wavelengths(Spectrometer device)
	{
		var calibration = SpectrometerCalibration.For(device);
		var xs = new double[device.PixelCount];
		for (var pixel = 0; pixel < xs.Length; pixel++)
		{
			xs[pixel] = calibration.WavelengthOf(pixel);
		}

		return xs;
	}
}
