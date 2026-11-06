using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ReactiveUI;

using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.TickGenerators;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Spectrum;
using SemiSpec.UI.Chart;
using SemiSpec.UI.Localization;

using Point = Avalonia.Point;

namespace SemiSpec.UI.Spectrum;

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
		_atp2000Line = DeviceLine.AddTo(Plot, Spectrometer.Atp2000);
		_atp8000Line = DeviceLine.AddTo(Plot, Spectrometer.Atp8000);
		_centreLine = Plot.Add.VerticalLine(0.0);
		_centreLine.IsVisible = false;

		Plot.Axes.Bottom.Label.Text = Resources.SpectrumAxisWavelength;
		Plot.Axes.Left.Label.Text = Resources.SpectrumAxisIntensity;
		Plot.Font.Automatic();
		Plot.Axes.Top.MinimumSize = ChartStateLine.TopMargin;
		SetEmptyLimits();
	}

	public Plot Plot { get; } = new();

	/// <summary>True while every spectrum sets the limits; false after a zoom or a pan.</summary>
	public bool IsFollowing { get; private set; } = true;

	public bool HasSpectrum
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>True while the following scale runs from zero; a manual zoom still freezes it.</summary>
	public bool IsFromZero { get; private set; }

	/// <summary>One signal per redraw request, raised at once on the caller's thread; completes on dispose.</summary>
	public IObservable<Unit> RedrawRequested => _redrawRequests.AsObservable();

	/// <summary>
	/// Draws the spectrum; the frame's pixel array is read, never written. A switch into or out of a historical
	/// spectrum keeps a frozen scale only on the device it was frozen on.
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

	/// <summary>The drawn wavelength range in nm.</summary>
	public AxisRange HorizontalRange()
	{
		lock (Plot.Sync)
		{
			var limits = Plot.Axes.GetLimits();
			return new AxisRange(limits.Left, limits.Right);
		}
	}

	/// <summary>The drawn intensity range in counts; the log axis converts from decades.</summary>
	public AxisRange VerticalRange()
	{
		lock (Plot.Sync)
		{
			var limits = Plot.Axes.GetLimits();
			return _display.IsLogarithmic
				? new AxisRange(Math.Pow(10.0, limits.Bottom), Math.Pow(10.0, limits.Top))
				: new AxisRange(limits.Bottom, limits.Top);
		}
	}

	/// <summary>Shows the typed wavelength range and freezes the scale; no-op before the first spectrum.</summary>
	public void SetHorizontalRange(AxisRange range)
	{
		if (!HasSpectrum)
		{
			return;
		}

		ChangeScale(() =>
		{
			Plot.Axes.SetLimitsX(range.Min, range.Max);
			IsFollowing = false;
		});
	}

	/// <summary>Why the intensity axis cannot show the range, or an empty string when it can.</summary>
	public string VerticalRangeProblem(AxisRange range)
	{
		return _display.IsLogarithmic && range.Min <= 0.0 ? Resources.AxisScaleLogMinimumPositive : string.Empty;
	}

	/// <summary>Shows the typed intensity range and freezes; no-op before the first spectrum or on a problem.</summary>
	public void SetVerticalRange(AxisRange range)
	{
		if (!HasSpectrum || VerticalRangeProblem(range).Length > 0)
		{
			return;
		}

		ChangeScale(() =>
		{
			if (_display.IsLogarithmic)
			{
				Plot.Axes.SetLimitsY(Math.Log10(range.Min), Math.Log10(range.Max));
			}
			else
			{
				Plot.Axes.SetLimitsY(range.Min, range.Max);
			}

			IsFollowing = false;
		});
	}

	/// <summary>A following scale freezes where it stands, as TakeManualScale does; a frozen one follows again.</summary>
	public void ToggleAutoscale()
	{
		if (IsFollowing)
		{
			TakeManualScale();
			return;
		}

		ResetScale();
	}

	public void ResetScale()
	{
		ChangeScale(() =>
		{
			IsFollowing = true;
			Project();
		});
	}

	/// <summary>Switches the following scale to or from zero and returns to following.</summary>
	public void SetFromZero(bool fromZero)
	{
		ChangeScale(() =>
		{
			IsFromZero = fromZero;
			IsFollowing = true;
			Project();
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
			var ys = projection.IsDark ? ProjectReference(measurement) : line.Ys;
			SetLimits(IsFromZero ? SpectrumScale.FromZero(line.Xs, ys) : SpectrumScale.Following(line.Xs, ys, _display));
		}
	}

	/// <summary>
	/// The shown device's last non-dark measurement, or the shown one, projected in the current mode; caller holds
	/// Plot.Sync.
	/// </summary>
	private ReadOnlySpan<double> ProjectReference(SpectrumMeasurement shown)
	{
		var reference = LineFor(shown.Device).LastNonDark ?? shown;
		var ys = _referenceYs.AsSpan(0, reference.Pixels.Length);
		SpectrumProjection.Fill(reference.Pixels, _display, ys);
		return ys;
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

	/// <summary>Before the first spectrum: X over both devices' wavelengths, Y 0..1.</summary>
	private void SetEmptyLimits()
	{
		var left = Math.Min(_atp2000Line.Xs[0], _atp8000Line.Xs[0]);
		var right = Math.Max(_atp2000Line.Xs[^1], _atp8000Line.Xs[^1]);
		Plot.Axes.SetLimits(left, right, 0.0, 1.0);
	}

	private void SetLimits(SpectrumLimits limits)
	{
		Plot.Axes.SetLimits(limits.X.Min, limits.X.Max, limits.Y.Min, limits.Y.Max);
	}

	private void RequestRedraw()
	{
		_redrawRequests.OnNext(Unit.Default);
	}

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
}
