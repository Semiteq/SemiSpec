using ScottPlot;
using ScottPlot.Plottables;

using SpectrumVisualizer.Core.Calibration;
using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.UI.Spectrum;

/// <summary>
/// One spectrometer's line on the spectrum plot: its calibrated wavelengths and the projected intensities.
/// </summary>
internal sealed class DeviceLine
{
	private DeviceLine(SignalXY plottable, double[] xs, double[] ys)
	{
		Plottable = plottable;
		Xs = xs;
		Ys = ys;
	}

	public SignalXY Plottable { get; }

	public double[] Xs { get; }

	public double[] Ys { get; }

	/// <summary>The device's last non-dark measurement, the Y reference while it shows a dark frame.</summary>
	public SpectrumMeasurement? LastNonDark { get; set; }

	/// <summary>Adds the device's hidden line to the plot, at the wavelength of each pixel.</summary>
	public static DeviceLine AddTo(Plot plot, Spectrometer device)
	{
		var calibration = SpectrometerCalibration.For(device);
		var xs = new double[device.PixelCount];
		for (var pixel = 0; pixel < xs.Length; pixel++)
		{
			xs[pixel] = calibration.WavelengthOf(pixel);
		}

		var ys = new double[xs.Length];
		var line = plot.Add.SignalXY(xs, ys);
		line.IsVisible = false;
		return new DeviceLine(line, xs, ys);
	}
}
