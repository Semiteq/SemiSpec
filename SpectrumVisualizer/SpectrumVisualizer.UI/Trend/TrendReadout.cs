using System.Globalization;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.UI.Localization;

namespace SpectrumVisualizer.UI.Trend;

/// <summary>
/// The hover text of one column of the last fold: its local time, its value or gap reason, its failures.
/// </summary>
public static class TrendReadout
{
	private const string ValueFormat = "G6";

	private static readonly Spectrometer[] _devices = [Spectrometer.Atp2000, Spectrometer.Atp8000];

	/// <summary>
	/// An empty column between two drawn ones reads as the line there: the gap reason of the column that ends the gap,
	/// or the column the line comes from.
	/// </summary>
	public static string For(IntensitySeries series, int index, TrendKey key)
	{
		var columns = series.Columns;
		var lines = new List<string> { ClockText.TimeOfDay(columns[index].Start) };

		if (TrendColumns.HasContent(columns[index]))
		{
			Describe(columns[index], key, lines);
			AddFailures(columns[index].Failed, lines);
			return string.Join('\n', lines);
		}

		var first = index;
		while (first > 0 && !TrendColumns.HasContent(columns[first - 1]))
		{
			first--;
		}

		var last = index;
		while (last + 1 < columns.Length && !TrendColumns.HasContent(columns[last + 1]))
		{
			last++;
		}

		var failed = BoardState.None;
		for (var i = first; i <= last; i++)
		{
			failed |= columns[i].Failed;
		}

		if (last + 1 < columns.Length && TrendColumns.OpensGap(columns[last + 1]))
		{
			AddGapReason(columns[last + 1], key, lines);
		}
		else if (first > 0 && last + 1 < columns.Length)
		{
			Describe(columns[first - 1], key, lines);
		}
		else
		{
			lines.Add(Resources.TrendReadoutNoData);
		}

		AddFailures(failed, lines);
		return string.Join('\n', lines);
	}

	private static void Describe(IntensityColumn column, TrendKey key, List<string> lines)
	{
		if (column.Count == 0)
		{
			AddGapReason(column, key, lines);
			return;
		}

		lines.Add(column.Min.Equals(column.Max)
			? Format(column.Min)
			: Resources.FormatTrendReadoutRangeFormat(Format(column.Min), Format(column.Max)));
		lines.Add(Resources.FormatTrendReadoutCountFormat(column.Count.ToString(CultureInfo.CurrentCulture)));
		AddBreakReasons(column, lines);
	}

	private static void AddGapReason(IntensityColumn column, TrendKey key, List<string> lines)
	{
		if (column.BreakCount > 0)
		{
			AddBreakReasons(column, lines);
			return;
		}

		lines.Add(column.UncoveredCount > 0
			? Resources.FormatTrendReadoutUncoveredFormat(key.WavelengthNm.ToString(CultureInfo.CurrentCulture))
			: Resources.TrendReadoutNoData);
	}

	/// <summary>
	/// A column can end a stretch without spectra and count lost spectra too; both reasons show, in that order.
	/// </summary>
	private static void AddBreakReasons(IntensityColumn column, List<string> lines)
	{
		if (column.NoSpectraCount > 0)
		{
			lines.Add(Resources.TrendReadoutNoSpectra);
		}

		if (column.LostCount > 0)
		{
			lines.Add(Resources.FormatTrendReadoutLostFormat(column.LostCount.ToString(CultureInfo.CurrentCulture)));
		}
	}

	private static void AddFailures(BoardState failed, List<string> lines)
	{
		foreach (var device in _devices)
		{
			if (failed.HasFlag(device.FailedBit))
			{
				lines.Add(Resources.FormatTrendReadoutFailedFormat(device.DisplayName));
			}
		}
	}

	private static string Format(double value)
	{
		return value.ToString(ValueFormat, CultureInfo.CurrentCulture);
	}
}
