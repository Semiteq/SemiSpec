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
	private readonly double[] _referenceYs =
		new double[Math.Max(Spectrometer.Atp2000.PixelCount, Spectrometer.Atp8000.PixelCount)];
	private DisplayedSpectrum? _shown;
	private SpectrumMeasurement? _lastNonDark;
	private bool _isInverted;
	private bool _isLogarithmic;

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
	public bool IsFollowing { get; private set; } = true;

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

		var wasFollowing = IsFollowing;

		lock (Plot.Sync)
		{
			if (logarithmic != _isLogarithmic)
			{
				Plot.Axes.Left.TickGenerator = logarithmic ? new LogTickGenerator() : new NumericAutomatic();
				Plot.Grid.YAxisStyle.MinorLineStyle.Width = logarithmic ? LogMinorGridWidth : 0f;
			}

			_isInverted = inverted;
			_isLogarithmic = logarithmic;
			IsFollowing = true;
			Project();
		}

		RaiseFollowingChanged(wasFollowing);
		RequestRedraw();
	}

	/// <summary>Freezes the limits where ScottPlot's own pan or zoom left them; before the first spectrum there is nothing to freeze.</summary>
	public void TakeManualScale()
	{
		if (!HasSpectrum)
		{
			return;
		}

		var wasFollowing = IsFollowing;
		IsFollowing = false;
		RaiseFollowingChanged(wasFollowing);
	}

	public void ResetScale()
	{
		var wasFollowing = IsFollowing;
		lock (Plot.Sync)
		{
			IsFollowing = true;
			Project();
		}

		RaiseFollowingChanged(wasFollowing);
		RequestRedraw();
	}

	/// <summary>Y from zero to 1.2 times the largest displayed value of the reference spectrum, X over the device range, then frozen.</summary>
	public void StickToZero()
	{
		if (_shown is not { Measurement: var measurement })
		{
			return;
		}

		var wasFollowing = IsFollowing;
		lock (Plot.Sync)
		{
			var rawMax = ProjectReference(measurement).Projection.RawMax;
			SetLimits(SpectrumScale.StuckToZero(LineFor(measurement.Device).Xs, rawMax, _isLogarithmic));
			IsFollowing = false;
		}

		RaiseFollowingChanged(wasFollowing);
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
		var projection = SpectrumProjection.Fill(measurement.Pixels, _isInverted, _isLogarithmic, line.Ys);
		if (!projection.IsDark)
		{
			_lastNonDark = measurement;
		}

		_atp2000Line.Plottable.IsVisible = line == _atp2000Line;
		_atp8000Line.Plottable.IsVisible = line == _atp8000Line;
		PlaceWindow(measurement, line.Xs);

		if (IsFollowing)
		{
			var ys = projection.IsDark ? ProjectReference(measurement).Ys : line.Ys;
			SetLimits(SpectrumScale.Following(line.Xs, ys, _isLogarithmic));
		}
	}

	/// <summary>
	/// The spectrum the Y limits come from, projected in the current mode: the shown one, or while a dark frame is
	/// shown the last non-dark one, so the axis does not jump on every dark frame; caller holds Plot.Sync.
	/// </summary>
	private ReferenceSpectrum ProjectReference(SpectrumMeasurement shown)
	{
		var reference = _lastNonDark ?? shown;
		var ys = _referenceYs.AsSpan(0, reference.Pixels.Length);
		var projection = SpectrumProjection.Fill(reference.Pixels, _isInverted, _isLogarithmic, ys);
		return new ReferenceSpectrum(ys, projection);
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

	/// <summary>Raised after Plot.Sync is released, so no subscriber runs under the plot lock.</summary>
	private void RaiseFollowingChanged(bool wasFollowing)
	{
		if (wasFollowing != IsFollowing)
		{
			this.RaisePropertyChanged(nameof(IsFollowing));
		}
	}

	private sealed record DeviceLine(SignalXY Plottable, double[] Xs, double[] Ys);

	private readonly ref struct ReferenceSpectrum(Span<double> ys, ProjectedSpectrum projection)
	{
		public ReadOnlySpan<double> Ys { get; } = ys;

		public ProjectedSpectrum Projection { get; } = projection;
	}
}
