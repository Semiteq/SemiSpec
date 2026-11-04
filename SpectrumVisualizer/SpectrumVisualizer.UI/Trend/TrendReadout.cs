using System.Globalization;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.UI.Localization;

namespace SpectrumVisualizer.UI.Trend;

/// <summary>The hover text of one column of the last fold: its local time, its value or gap reason, its failures.</summary>
public static class TrendReadout
{
	private const string TimeFormat = "HH:mm:ss";
	private const string ValueFormat = "G6";

	private static readonly Spectrometer[] _devices = [Spectrometer.Atp2000, Spectrometer.Atp8000];

	/// <summary>
	/// An empty column between two drawn ones reads as the line there: the gap reason of the column that ends the gap,
	/// or the column the line comes from.
	/// </summary>
	public static string For(IntensitySeries series, int index, TrendKey key)
	{
		var columns = series.Columns;
		var lines = new List<string>
		{
			columns[index].Start.ToLocalTime().ToString(TimeFormat, CultureInfo.InvariantCulture),
		};

		if (HasContent(columns[index]))
		{
			Describe(columns[index], key, lines);
			AddFailures(columns[index].Failed, lines);
			return string.Join('\n', lines);
		}

		var first = index;
		while (first > 0 && !HasContent(columns[first - 1]))
		{
			first--;
		}

		var last = index;
		var failed = BoardState.None;
		while (last + 1 < columns.Length && !HasContent(columns[last + 1]))
		{
			last++;
		}

		for (var i = first; i <= last; i++)
		{
			failed |= columns[i].Failed;
		}

		if (last + 1 < columns.Length && OpensAGap(columns[last + 1]))
		{
			lines.Add(GapReason(columns[last + 1], key));
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

	private static bool HasContent(IntensityColumn column)
	{
		return column.Count > 0 || column.BreakCount > 0 || column.UncoveredCount > 0;
	}

	private static bool OpensAGap(IntensityColumn column)
	{
		return column.BreakCount > 0 || column.Count == 0;
	}

	private static void Describe(IntensityColumn column, TrendKey key, List<string> lines)
	{
		if (column.Count == 0)
		{
			lines.Add(GapReason(column, key));
			return;
		}

		lines.Add(column.Min.Equals(column.Max)
			? Format(column.Min)
			: Resources.FormatTrendReadoutRangeFormat(Format(column.Min), Format(column.Max)));
		lines.Add(Resources.FormatTrendReadoutCountFormat(column.Count.ToString(CultureInfo.CurrentCulture)));
		if (column.BreakCount > 0)
		{
			lines.Add(BreakReason(column));
		}
	}

	private static string GapReason(IntensityColumn column, TrendKey key)
	{
		if (column.BreakCount > 0)
		{
			return BreakReason(column);
		}

		return column.UncoveredCount > 0
			? Resources.FormatTrendReadoutUncoveredFormat(key.WavelengthNm.ToString(CultureInfo.CurrentCulture))
			: Resources.TrendReadoutNoData;
	}

	/// <summary>Only a Loss break carries a lost count, so a break with none lost is a stretch without spectra.</summary>
	private static string BreakReason(IntensityColumn column)
	{
		return column.LostCount > 0
			? Resources.FormatTrendReadoutLostFormat(column.LostCount.ToString(CultureInfo.CurrentCulture))
			: Resources.TrendReadoutNoSpectra;
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
