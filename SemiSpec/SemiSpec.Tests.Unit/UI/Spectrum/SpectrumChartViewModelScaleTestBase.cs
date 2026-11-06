using AwesomeAssertions;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Spectrum;
using SemiSpec.UI.Spectrum;

using static SemiSpec.Tests.Unit.UI.Spectrum.SpectrumSamples;

namespace SemiSpec.Tests.Unit.UI.Spectrum;

/// <summary>A chart view model per test with the limit assertions of the scale tests.</summary>
public abstract class SpectrumChartViewModelScaleTestBase : IDisposable
{
	private protected const double Tolerance = 1e-9;

	private protected readonly SpectrumChartViewModel _chart = new();

	public void Dispose()
	{
		_chart.Dispose();
		GC.SuppressFinalize(this);
	}

	private protected static DisplayedSpectrum Historical(DisplayedSpectrum spectrum)
	{
		return spectrum with { IsHistorical = true };
	}

	/// <summary>The sample peak with its top pixel at twice the height, so its following limits differ.</summary>
	private protected static DisplayedSpectrum TallPeak(Spectrometer device)
	{
		var peak = Peak(device, 400, 25);
		var pixels = (ushort[])peak.Measurement.Pixels.Clone();
		pixels[pixels.Length / 2] = 2 * PeakTop;
		return peak with { Measurement = new SpectrumMeasurement(device, 400, 25, 60000f, 30f, 0f, pixels) };
	}

	/// <summary>The scale follows and its Y limits are the spectrum's following limits in the given display.</summary>
	private protected void ShouldFollow(DisplayedSpectrum spectrum, SpectrumDisplay display)
	{
		var pixels = spectrum.Measurement.Pixels;
		var ys = new double[pixels.Length];
		SpectrumProjection.Fill(pixels, display, ys);
		var expected = SpectrumScale.Following([0.0, 1.0], ys, display);

		_chart.IsFollowing.Should().BeTrue();
		var limits = _chart.Plot.Axes.GetLimits();
		limits.Bottom.Should().BeApproximately(expected.Y.Min, Tolerance);
		limits.Top.Should().BeApproximately(expected.Y.Max, Tolerance);
	}

	private protected void ShouldHaveLimits(double left, double right, double bottom, double top)
	{
		var limits = _chart.Plot.Axes.GetLimits();
		limits.Left.Should().Be(left);
		limits.Right.Should().Be(right);
		limits.Bottom.Should().Be(bottom);
		limits.Top.Should().Be(top);
	}
}
