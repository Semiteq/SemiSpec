using FluentResults;

using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.Core.Store;

/// <summary>Folds window intensities, breaks and failure marks into columns on the time grid of the query.</summary>
public sealed class IntensityBuckets(IntensityQuery query)
{
	private readonly double[] _min = new double[query.ColumnCount];
	private readonly double[] _max = new double[query.ColumnCount];
	private readonly int[] _count = new int[query.ColumnCount];
	private readonly int[] _uncoveredCount = new int[query.ColumnCount];
	private readonly double[] _last = new double[query.ColumnCount];
	private readonly DateTimeOffset[] _lastAt = new DateTimeOffset[query.ColumnCount];
	private readonly int[] _breakCount = new int[query.ColumnCount];
	private readonly int[] _lostCount = new int[query.ColumnCount];
	private readonly int[] _noSpectraCount = new int[query.ColumnCount];
	private readonly BoardState[] _failed = new BoardState[query.ColumnCount];

	public void Add(DateTimeOffset receivedAt, double value)
	{
		var column = query.ColumnOf(receivedAt);
		if (column < 0)
		{
			return;
		}

		if (_count[column] == 0)
		{
			_min[column] = value;
			_max[column] = value;
		}
		else
		{
			_min[column] = Math.Min(_min[column], value);
			_max[column] = Math.Max(_max[column], value);
		}

		if (_count[column] == 0 || receivedAt >= _lastAt[column])
		{
			_last[column] = value;
			_lastAt[column] = receivedAt;
		}

		_count[column]++;
	}

	/// <summary>Adds the point's value, or counts the point as uncovered when its value is NaN.</summary>
	public void Add(TrendPoint point)
	{
		var receivedAt = DateTimeOffset.FromUnixTimeMilliseconds(point.UnixMs);
		if (double.IsNaN(point.Value))
		{
			AddUncovered(receivedAt);
		}
		else
		{
			Add(receivedAt, point.Value);
		}
	}

	/// <summary>Counts a spectrum the series cannot read: its device misses the wavelength, or the row is corrupt.</summary>
	public void AddUncovered(DateTimeOffset receivedAt)
	{
		var column = query.ColumnOf(receivedAt);
		if (column >= 0)
		{
			_uncoveredCount[column]++;
		}
	}

	/// <summary>Counts a break of the line; a break with none lost ends a stretch without spectra.</summary>
	public void AddBreak(DateTimeOffset moment, int lost)
	{
		var column = query.ColumnOf(moment);
		if (column < 0)
		{
			return;
		}

		_breakCount[column]++;
		_lostCount[column] += lost;
		if (lost == 0)
		{
			_noSpectraCount[column]++;
		}
	}

	/// <summary>Merges a frame's measurement-failed bits into its column; the continuity rule passes only those bits.</summary>
	public void AddFailure(DateTimeOffset moment, BoardState state)
	{
		var column = query.ColumnOf(moment);
		if (column >= 0)
		{
			_failed[column] |= state;
		}
	}

	public IntensitySeries ToSeries()
	{
		var columns = new IntensityColumn[_count.Length];
		for (var i = 0; i < columns.Length; i++)
		{
			var empty = _count[i] == 0;
			columns[i] = new IntensityColumn(
				query.ColumnStart(i),
				empty ? double.NaN : _min[i],
				empty ? double.NaN : _max[i],
				_count[i],
				_uncoveredCount[i],
				empty ? double.NaN : _last[i],
				_breakCount[i],
				_lostCount[i],
				_noSpectraCount[i],
				_failed[i]);
		}

		return new IntensitySeries(query.From, query.To, query.ColumnWidth, columns);
	}
}

/// <summary>
/// A request for the intensity series at a wavelength and delta over [From, To), both in whole milliseconds, in columns
/// of ColumnWidth on a grid anchored at the Unix epoch.
/// </summary>
public sealed record IntensityQuery
{
	/// <summary>The most columns a series holds, about ten times the pixels across a 4K screen.</summary>
	public const int MaxColumns = 40_000;

	private readonly long _widthMilliseconds;
	private readonly long _firstGridColumn;

	private IntensityQuery(
		ushort wavelengthNm,
		byte delta,
		DateTimeOffset from,
		DateTimeOffset to,
		long widthMilliseconds,
		long firstGridColumn,
		int columnCount)
	{
		WavelengthNm = wavelengthNm;
		Delta = delta;
		From = from;
		To = to;
		_widthMilliseconds = widthMilliseconds;
		_firstGridColumn = firstGridColumn;
		ColumnCount = columnCount;
	}

	public ushort WavelengthNm { get; }

	public byte Delta { get; }

	public DateTimeOffset From { get; }

	public DateTimeOffset To { get; }

	public TimeSpan ColumnWidth => TimeSpan.FromMilliseconds(_widthMilliseconds);

	/// <summary>The grid columns that intersect [From, To); the first and the last may stick out of the range.</summary>
	public int ColumnCount { get; }

	/// <summary>
	/// Builds the query with From and To truncated to whole milliseconds, or fails for an empty range, a width that is
	/// not a whole number of milliseconds of at least 1, or more than <see cref="MaxColumns"/> columns.
	/// </summary>
	public static Result<IntensityQuery> Create(
		ushort wavelengthNm, byte delta, DateTimeOffset from, DateTimeOffset to, TimeSpan columnWidth)
	{
		if (columnWidth < TimeSpan.FromMilliseconds(1) || columnWidth.Ticks % TimeSpan.TicksPerMillisecond != 0)
		{
			return Result.Fail(
				$"The intensity series needs a column width of whole milliseconds, at least 1 ms, got {columnWidth}");
		}

		from = WholeMilliseconds(from);
		to = WholeMilliseconds(to);
		if (to <= from)
		{
			return Result.Fail($"The intensity series range ends at {to:O}, not after its start {from:O}");
		}

		var widthMilliseconds = columnWidth.Ticks / TimeSpan.TicksPerMillisecond;
		var firstGridColumn = FloorDivide(from.ToUnixTimeMilliseconds(), widthMilliseconds);
		var columns = FloorDivide(to.ToUnixTimeMilliseconds() - 1, widthMilliseconds) - firstGridColumn + 1;
		if (columns > MaxColumns)
		{
			return Result.Fail(
				$"The intensity series from {from:O} to {to:O} in columns of {columnWidth} needs {columns} columns, "
				+ $"more than {MaxColumns}");
		}

		return new IntensityQuery(wavelengthNm, delta, from, to, widthMilliseconds, firstGridColumn, (int)columns);
	}

	/// <summary>The column of a time inside [From, To), or -1 outside it.</summary>
	public int ColumnOf(DateTimeOffset moment)
	{
		if (moment < From || moment >= To)
		{
			return -1;
		}

		return (int)(GridColumn(moment.ToUnixTimeMilliseconds()) - _firstGridColumn);
	}

	/// <summary>The start of a column: a multiple of the column width from the Unix epoch.</summary>
	public DateTimeOffset ColumnStart(int column)
	{
		return DateTimeOffset.FromUnixTimeMilliseconds((_firstGridColumn + column) * _widthMilliseconds);
	}

	private long GridColumn(long unixMilliseconds)
	{
		return FloorDivide(unixMilliseconds, _widthMilliseconds);
	}

	private static DateTimeOffset WholeMilliseconds(DateTimeOffset moment)
	{
		return moment.AddTicks(-(moment.UtcTicks % TimeSpan.TicksPerMillisecond));
	}

	private static long FloorDivide(long value, long divisor)
	{
		var quotient = Math.DivRem(value, divisor, out var remainder);
		return remainder < 0 ? quotient - 1 : quotient;
	}
}

/// <summary>
/// The series over [From, To): column i spans ColumnWidth from its Start, on a grid anchored at the Unix epoch, so two
/// series of one width share the boundaries of every column they both hold in full.
/// </summary>
public sealed record IntensitySeries(
	DateTimeOffset From, DateTimeOffset To, TimeSpan ColumnWidth, IntensityColumn[] Columns);

/// <summary>
/// Min, max and latest value of the column's covered spectra, NaN when Count is 0; UncoveredCount counts the spectra of
/// a device that does not cover the wavelength and the corrupt rows the series skipped; the break counts (NoSpectraCount
/// of them end a silence) and the failed bits come from the trend's continuity rule, and the store leaves them at zero.
/// </summary>
public readonly record struct IntensityColumn(
	DateTimeOffset Start,
	double Min,
	double Max,
	int Count,
	int UncoveredCount,
	double Last,
	int BreakCount,
	int LostCount,
	int NoSpectraCount,
	BoardState Failed);
