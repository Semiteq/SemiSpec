using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ReactiveUI;

using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.TickGenerators;

using SpectrumVisualizer.Core.Calibration;
using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Spectrum;
using SpectrumVisualizer.UI.Chart;
using SpectrumVisualizer.UI.Localization;

using Point = Avalonia.Point;

namespace SpectrumVisualizer.UI.Spectrum;

/// <summary>The spectrum plot: a line per spectrometer, the window band, the centre line and the scale state.</summary>
public sealed class SpectrumChartViewModel : ReactiveObject, IDisposable
{
	private const float LogMinorGridWidth = 1f;

	// ScottPlot 5.1.59 MouseDragPan and MouseDragZoomRectangle ignore a held move under this distance from the press,
	// but still apply that short pan on release; the next spectrum restores the limits while following.
	private const double DragThreshold = 5.0;

	private readonly HorizontalSpan _band;
	private readonly DeviceLine _atp2000Line;
	private readonly DeviceLine _atp8000Line;
	private readonly VerticalLine _centreLine;
	private readonly Subject<Unit> _redrawRequests = new();
	private readonly double[] _referenceYs =
		new double[Math.Max(Spectrometer.Atp2000.PixelCount, Spectrometer.Atp8000.PixelCount)];
	private DisplayedSpectrum? _shown;
	private SpectrumDisplay _display;
	private Spectrometer? _frozenDevice;

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

	/// <summary>True while every spectrum sets the limits; false after a zoom, a pan or Stick to zero.</summary>
	public bool IsFollowing { get; private set; } = true;

	public bool HasSpectrum
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>One signal per redraw request, raised at once on the caller's thread; completes on dispose.</summary>
	public IObservable<Unit> RedrawRequested => _redrawRequests.AsObservable();

	/// <summary>
	/// Draws the spectrum; the frame's pixel array is read, never written. A switch into or out of a historical spectrum
	/// keeps a frozen scale only on the device it was frozen on.
	/// </summary>
	public void Show(DisplayedSpectrum spectrum)
	{
		var wasFollowing = IsFollowing;
		lock (Plot.Sync)
		{
			var crossesHistory = spectrum.IsHistorical || _shown is { IsHistorical: true };
			if (crossesHistory && spectrum.Measurement.Device != _frozenDevice)
			{
				IsFollowing = true;
			}

			_shown = spectrum;
			Project();
		}

		RaiseIfFollowingChanged(wasFollowing);
		HasSpectrum = true;
		RequestRedraw();
	}

	public void SetDisplay(SpectrumDisplay display)
	{
		if (display == _display)
		{
			return;
		}

		ChangeScale(() =>
		{
			if (display.IsLogarithmic != _display.IsLogarithmic)
			{
				Plot.Axes.Left.TickGenerator = display.IsLogarithmic ? new LogTickGenerator() : new NumericAutomatic();
				Plot.Grid.YAxisStyle.MinorLineStyle.Width = display.IsLogarithmic ? LogMinorGridWidth : 0f;
			}

			_display = display;
			IsFollowing = true;
			Project();
		});
	}

	/// <summary>Freezes the limits where ScottPlot's pan or zoom left them; no-op before the first spectrum.</summary>
	public void TakeManualScale()
	{
		if (!HasSpectrum)
		{
			return;
		}

		ChangeScale(() => IsFollowing = false);
	}

	/// <summary>A held move freezes the scale once it is far enough from the press for ScottPlot to pan.</summary>
	public void OnHeldMove(Point press, Point current, bool isButtonHeld)
	{
		var distance = Math.Max(Math.Abs(current.X - press.X), Math.Abs(current.Y - press.Y));
		if (isButtonHeld && IsFollowing && distance >= DragThreshold)
		{
			TakeManualScale();
		}
	}

	public void ResetScale()
	{
		ChangeScale(() =>
		{
			IsFollowing = true;
			Project();
		});
	}

	public void StickToZero()
	{
		if (_shown is not { Measurement: var measurement })
		{
			return;
		}

		ChangeScale(() =>
		{
			var rawMax = ProjectReference(measurement).Projection.RawMax;
			SetLimits(SpectrumScale.StuckToZero(LineFor(measurement.Device).Xs, rawMax, _display));
			IsFollowing = false;
		});
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

	/// <summary>Fills the shown line, the band and, if following, the limits; caller holds Plot.Sync.</summary>
	private void Project()
	{
		if (_shown is not { Measurement: var measurement, IsHistorical: var isHistorical })
		{
			return;
		}

		var line = LineFor(measurement.Device);
		var projection = SpectrumProjection.Fill(measurement.Pixels, _display, line.Ys);
		if (!projection.IsDark && !isHistorical)
		{
			line.LastNonDark = measurement;
		}

		_atp2000Line.Plottable.IsVisible = line == _atp2000Line;
		_atp8000Line.Plottable.IsVisible = line == _atp8000Line;
		PlaceWindow(measurement, line.Xs);

		if (IsFollowing)
		{
			var ys = projection.IsDark ? ProjectReference(measurement).Ys : line.Ys;
			SetLimits(SpectrumScale.Following(line.Xs, ys, _display));
		}
	}

	/// <summary>
	/// The shown device's last non-dark measurement, or the shown one, projected in the current mode; caller holds
	/// Plot.Sync.
	/// </summary>
	private ReferenceSpectrum ProjectReference(SpectrumMeasurement shown)
	{
		var reference = LineFor(shown.Device).LastNonDark ?? shown;
		var ys = _referenceYs.AsSpan(0, reference.Pixels.Length);
		var projection = SpectrumProjection.Fill(reference.Pixels, _display, ys);
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

	/// <summary>
	/// Runs the change under Plot.Sync and records the shown device when the scale ends frozen, then raises IsFollowing
	/// outside the lock and redraws.
	/// </summary>
	private void ChangeScale(Action change)
	{
		var wasFollowing = IsFollowing;
		lock (Plot.Sync)
		{
			change();
			if (!IsFollowing)
			{
				_frozenDevice = _shown?.Measurement.Device;
			}
		}

		RaiseIfFollowingChanged(wasFollowing);
		RequestRedraw();
	}

	private void RaiseIfFollowingChanged(bool wasFollowing)
	{
		if (wasFollowing != IsFollowing)
		{
			this.RaisePropertyChanged(nameof(IsFollowing));
		}
	}

	private sealed class DeviceLine(SignalXY plottable, double[] xs, double[] ys)
	{
		public SignalXY Plottable { get; } = plottable;

		public double[] Xs { get; } = xs;

		public double[] Ys { get; } = ys;

		/// <summary>The device's last non-dark measurement, the Y reference while it shows a dark frame.</summary>
		public SpectrumMeasurement? LastNonDark { get; set; }
	}

	private readonly ref struct ReferenceSpectrum(Span<double> ys, ProjectedSpectrum projection)
	{
		public ReadOnlySpan<double> Ys { get; } = ys;

		public ProjectedSpectrum Projection { get; } = projection;
	}
}
