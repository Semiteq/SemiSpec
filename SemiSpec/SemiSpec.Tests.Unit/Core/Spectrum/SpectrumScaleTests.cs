using AwesomeAssertions;

using SemiSpec.Core.Calibration;
using SemiSpec.Core.Protocol;
using SemiSpec.Core.Spectrum;

using Xunit;

namespace SemiSpec.Tests.Unit.Core.Spectrum;

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

	public static TheoryData<double[], double> FromZeroRows => new()
	{
		{ [1000, 31000, 3000], 32550 },
		{ [3, 4, 4.5], 4.725 },
		{ [0, 0, 0], 1 },
		{ [-5, -1, -2], 1 },
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
	[MemberData(nameof(FromZeroRows))]
	public void FromZeroRunsFromZeroToTheMaximumPlusFivePercent(double[] ys, double expectedTop)
	{
		var limits = SpectrumScale.FromZero(_xs, ys);

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
		SpectrumProjection.Fill(new ushort[xs.Length], display, ys);

		SpectrumLimits[] all =
		[
			SpectrumScale.Following(xs, ys, display),
			SpectrumScale.FromZero(xs, ys),
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
