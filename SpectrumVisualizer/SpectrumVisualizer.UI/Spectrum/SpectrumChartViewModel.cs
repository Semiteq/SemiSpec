using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ReactiveUI;

using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.TickGenerators;

using SpectrumVisualizer.Core.Calibration;
using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.UI.Chart;
using SpectrumVisualizer.UI.Localization;

namespace SpectrumVisualizer.UI.Spectrum;

/// <summary>The spectrum plot: one line per spectrometer, the window band, the centre line and the scale state.</summary>
public sealed class SpectrumChartViewModel : ReactiveObject, IDisposable
{
	private const float LogMinorGridWidth = 1f;

	private readonly HorizontalSpan _band;
	private readonly DeviceLine _atp2000Line;
	private readonly DeviceLine _atp8000Line;
	private readonly VerticalLine _centreLine;
	private readonly Subject<Unit> _redrawRequests = new();
	private DisplayedSpectrum? _shown;
	private ProjectedSpectrum _shownProjection;
	private bool _isInverted;
	private bool _isLogarithmic;
	private AxisRange? _lastNonDarkY;
	private bool _isDisposed;

	public SpectrumChartViewModel()
	{
		_band = Plot.Add.HorizontalSpan(0.0, 0.0);
		_band.IsVisible = false;
		_band.LineStyle.Width = 0f;
		_atp2000Line = AddLine(Spectrometer.Atp2000);
		_atp8000Line = AddLine(Spectrometer.Atp8000);
		_centreLine = Plot.Add.VerticalLine(0.0);
		_centreLine.IsVisible = false;

		Plot.Axes.Bottom.Label.Text = Resources.SpectrumAxisWavelength;
		Plot.Axes.Left.Label.Text = Resources.SpectrumAxisIntensity;
		Plot.Font.Automatic();
	}

	public Plot Plot { get; } = new();

	/// <summary>True while every spectrum sets the limits; false once the operator zoomed, panned or stuck to zero.</summary>
	public bool IsFollowing
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = true;

	public bool HasSpectrum
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>One signal per redraw request, raised at once on the caller's thread; completes on dispose.</summary>
	public IObservable<Unit> RedrawRequested => _redrawRequests.AsObservable();

	/// <summary>Draws the spectrum; the frame's pixel array is read, never written.</summary>
	public void Show(DisplayedSpectrum spectrum)
	{
		lock (Plot.Sync)
		{
			_shown = spectrum;
			Project();
		}

		HasSpectrum = true;
		RequestRedraw();
	}

	public void SetDisplay(bool inverted, bool logarithmic)
	{
		if (inverted == _isInverted && logarithmic == _isLogarithmic)
		{
			return;
		}

		lock (Plot.Sync)
		{
			if (logarithmic != _isLogarithmic)
			{
				Plot.Axes.Left.TickGenerator = logarithmic ? new LogTickGenerator() : new NumericAutomatic();
				Plot.Grid.YAxisStyle.MinorLineStyle.Width = logarithmic ? LogMinorGridWidth : 0f;
			}

			_isInverted = inverted;
			_isLogarithmic = logarithmic;
			_lastNonDarkY = null;
			IsFollowing = true;
			Project();
		}

		RequestRedraw();
	}

	/// <summary>Freezes the limits where ScottPlot's own pan or zoom left them.</summary>
	public void TakeManualScale()
	{
		IsFollowing = false;
	}

	public void ResetScale()
	{
		lock (Plot.Sync)
		{
			IsFollowing = true;
			Project();
		}

		RequestRedraw();
	}

	/// <summary>Y from zero to 1.2 times the largest displayed value, X over the device range, then frozen.</summary>
	public void StickToZero()
	{
		if (_shown is not { } shown)
		{
			return;
		}

		lock (Plot.Sync)
		{
			var limits = SpectrumScale.StuckToZero(LineFor(shown.Measurement.Device).Xs, _shownProjection.RawMax, _isLogarithmic);
			SetLimits(limits);
			IsFollowing = false;
		}

		RequestRedraw();
	}

	public void ApplyPalette(ChartColors colors)
	{
		lock (Plot.Sync)
		{
			_atp2000Line.Plottable.Color = colors.Line;
			_atp8000Line.Plottable.Color = colors.Line;
			_band.FillStyle.Color = colors.BandFill;
			_centreLine.Color = colors.CentreLine;
		}

		RequestRedraw();
	}

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;
		_redrawRequests.OnCompleted();
		_redrawRequests.Dispose();
		Plot.Dispose();
	}

	private DeviceLine AddLine(Spectrometer device)
	{
		var calibration = SpectrometerCalibration.For(device);
		var xs = new double[device.PixelCount];
		for (var pixel = 0; pixel < xs.Length; pixel++)
		{
			xs[pixel] = calibration.WavelengthOf(pixel);
		}

		var ys = new double[xs.Length];
		var line = Plot.Add.SignalXY(xs, ys);
		line.IsVisible = false;
		return new DeviceLine(line, xs, ys);
	}

	private DeviceLine LineFor(Spectrometer device)
	{
		return device == Spectrometer.Atp2000 ? _atp2000Line : _atp8000Line;
	}

	/// <summary>Writes the shown spectrum into its line, moves the band and, while following, the limits; caller holds Plot.Sync.</summary>
	private void Project()
	{
		if (_shown is not { Measurement: var measurement })
		{
			return;
		}

		var line = LineFor(measurement.Device);
		_shownProjection = SpectrumProjection.Fill(measurement.Pixels, _isInverted, _isLogarithmic, line.Ys);
		_atp2000Line.Plottable.IsVisible = line == _atp2000Line;
		_atp8000Line.Plottable.IsVisible = line == _atp8000Line;
		PlaceWindow(measurement, line.Xs);

		if (!IsFollowing)
		{
			return;
		}

		var limits = SpectrumScale.Following(line.Xs, line.Ys, _shownProjection.IsDark, _isLogarithmic, _lastNonDarkY);
		if (!_shownProjection.IsDark)
		{
			_lastNonDarkY = limits.Y;
		}

		SetLimits(limits);
	}

	private void PlaceWindow(SpectrumMeasurement measurement, double[] xs)
	{
		var low = (double)measurement.WavelengthNm - measurement.Delta;
		var high = (double)measurement.WavelengthNm + measurement.Delta;
		var overlapsDevice = high >= xs[0] && low <= xs[^1];

		_band.X1 = low;
		_band.X2 = high;
		_band.IsVisible = overlapsDevice && measurement.Delta > 0;
		_centreLine.X = measurement.WavelengthNm;
		_centreLine.IsVisible = overlapsDevice;
	}

	private void SetLimits(SpectrumLimits limits)
	{
		Plot.Axes.SetLimits(limits.X.Min, limits.X.Max, limits.Y.Min, limits.Y.Max);
	}

	private void RequestRedraw()
	{
		_redrawRequests.OnNext(Unit.Default);
	}

	private sealed record DeviceLine(SignalXY Plottable, double[] Xs, double[] Ys);
}
