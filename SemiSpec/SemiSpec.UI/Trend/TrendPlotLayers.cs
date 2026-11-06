using System.Globalization;

using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.TickGenerators;

using SemiSpec.Core.Chart;
using SemiSpec.Core.Spectrum;
using SemiSpec.Core.Trend;
using SemiSpec.UI.Chart;
using SemiSpec.UI.Localization;

namespace SemiSpec.UI.Trend;

/// <summary>
/// The trend's layers on its plot: the envelope, the failure strip, the shown-moment line and the axes.
/// </summary>
public sealed class TrendPlotLayers
{
	private const int LabelDecimals = 10;
	private const string LabelFormat = "#,0.##########";
	private const string MinutesFormat = "HH:mm";
	private const string SecondsFormat = "HH:mm:ss";

	private static readonly TimeSpan _widestWindowWithSeconds = TimeSpan.FromMinutes(20);

	private readonly Plot _plot;
	private readonly EnvelopeLine _envelope = new();
	private readonly TrendMarkerStrip _strip = new();
	private readonly VerticalLine _momentLine;
	private string _timeFormat = SecondsFormat;

	public TrendPlotLayers(Plot plot)
	{
		_plot = plot;
		plot.Add.Plottable(_envelope);
		plot.Add.Plottable(_strip);
		_momentLine = plot.Add.VerticalLine(0.0);
		_momentLine.IsVisible = false;
		plot.Axes.Bottom.TickGenerator = new DateTimeAutomatic { LabelFormatter = TimeLabel };
		plot.Axes.Bottom.Label.Text = Resources.TrendAxisTime;
		plot.Axes.Left.TickGenerator = new NumericAutomatic { LabelFormatter = IntensityLabel };
		plot.Axes.Left.Label.Text = Resources.TrendAxisIntensity;
		plot.Axes.SetLimitsY(0.0, 1.0);
		plot.Font.Automatic();
		plot.Axes.Top.MinimumSize = ChartStateLine.TopMargin;
	}

	/// <summary>
	/// Draws the fold over [from, to]; a null fold clears the line and the strip and keeps the Y range. fromZero runs
	/// Y from zero to the drawn maximum plus 5 %; a manual range holds Y where the operator typed it.
	/// </summary>
	public void Draw(TrendFold? fold, DateTimeOffset from, DateTimeOffset to, bool fromZero, AxisRange? manualY)
	{
		var columns = fold is null ? [] : EnvelopeColumn.Map(fold.Series, LocalTimeAxis.ToAxis);
		var failures = fold is null ? [] : TrendColumns.FailureRanges(fold.Series);
		lock (_plot.Sync)
		{
			_envelope.ReplaceColumns(columns);
			_strip.ReplaceSpans(failures);
			_plot.Axes.SetLimitsX(LocalTimeAxis.ToAxis(from), LocalTimeAxis.ToAxis(to));
			_timeFormat = to - from <= _widestWindowWithSeconds ? SecondsFormat : MinutesFormat;
			_plot.Axes.Bottom.Label.Text = TimeAxisTitle(from, to);
			if (manualY is { } held)
			{
				_plot.Axes.SetLimitsY(held.Min, held.Max);
			}
			else if (EnvelopeColumn.VerticalRange(columns) is { } range)
			{
				var (bottom, top) = fromZero ? FromZeroRange.Over(EnvelopeColumn.DrawnMaximum(columns)) : range;
				_plot.Axes.SetLimitsY(bottom, top);
			}

			_plot.Axes.Left.Label.Text = fold?.Extent.Key is { } key
				? Resources.FormatTrendAxisIntensityFormat(key.WavelengthNm, key.Delta)
				: Resources.TrendAxisIntensity;
		}
	}

	public void Recolor(ChartColors colors)
	{
		lock (_plot.Sync)
		{
			_envelope.Recolor(colors.Envelope);
			_strip.Recolor(colors.FailureStrip);
			_momentLine.Color = colors.MomentLine;
		}
	}

	public void MarkMoment(DateTimeOffset? moment)
	{
		lock (_plot.Sync)
		{
			_momentLine.IsVisible = moment is not null;
			if (moment is { } shown)
			{
				_momentLine.X = LocalTimeAxis.ToAxis(shown);
			}
		}
	}

	/// <summary>"Time, date", or both dates for a window across midnight; the ticks carry the time only.</summary>
	private static string TimeAxisTitle(DateTimeOffset from, DateTimeOffset to)
	{
		var culture = Resources.Culture ?? CultureInfo.CurrentCulture;
		var firstDay = from.ToLocalTime().Date;
		var lastDay = to.ToLocalTime().Date;
		var first = firstDay.ToString("d", culture);
		return firstDay == lastDay
			? Resources.FormatTrendAxisTimeFormat(first)
			: Resources.FormatTrendAxisTimeRangeFormat(first, lastDay.ToString("d", culture));
	}

	private string TimeLabel(DateTime time)
	{
		return time.ToString(_timeFormat, CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// Prints the digits a tick needs; ScottPlot's default drops the fraction above 1000, so ticks under 1 apart repeat.
	/// </summary>
	private static string IntensityLabel(double value)
	{
		var rounded = Math.Round(value, LabelDecimals);
		var withoutNegativeZero = rounded == 0.0 ? 0.0 : rounded;
		return withoutNegativeZero.ToString(LabelFormat, CultureInfo.CurrentCulture);
	}
}
