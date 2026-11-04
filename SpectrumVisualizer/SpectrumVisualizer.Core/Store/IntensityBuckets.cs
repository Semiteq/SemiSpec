using FluentResults;

namespace SpectrumVisualizer.Core.Store;

/// <summary>Folds window intensities into min/max columns of equal time spans over the query range.</summary>
public sealed class IntensityBuckets
{
	private readonly DateTimeOffset _from;
	private readonly long _rangeTicks;
	private readonly double[] _min;
	private readonly double[] _max;
	private readonly int[] _count;
	private readonly int[] _uncoveredCount;

	public IntensityBuckets(IntensityQuery query)
	{
		_from = query.From;
		_rangeTicks = (query.To - query.From).Ticks;
		_min = new double[query.Columns];
		_max = new double[query.Columns];
		_count = new int[query.Columns];
		_uncoveredCount = new int[query.Columns];
		var wholeMilliseconds = Math.Max(_rangeTicks / query.Columns / TimeSpan.TicksPerMillisecond, 1);
		ColumnWidth = TimeSpan.FromMilliseconds(wholeMilliseconds);
	}

	/// <summary>
	/// The span of one column truncated to whole milliseconds, for drawing; the fold never divides by it.
	/// </summary>
	public TimeSpan ColumnWidth { get; }

	public void Add(DateTimeOffset receivedAt, double value)
	{
		var column = ColumnOf(receivedAt);
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

		_count[column]++;
	}

	/// <summary>Counts a spectrum whose device does not cover the queried wavelength.</summary>
	public void AddUncovered(DateTimeOffset receivedAt)
	{
		var column = ColumnOf(receivedAt);
		if (column >= 0)
		{
			_uncoveredCount[column]++;
		}
	}

	public IntensitySeries ToSeries()
	{
		var columns = new IntensityColumn[_count.Length];
		for (var i = 0; i < columns.Length; i++)
		{
			columns[i] = _count[i] == 0
				? new IntensityColumn(double.NaN, double.NaN, 0, _uncoveredCount[i])
				: new IntensityColumn(_min[i], _max[i], _count[i], _uncoveredCount[i]);
		}

		return new IntensitySeries(_from, ColumnWidth, columns);
	}

	/// <summary>The column of a time inside [From, To), or -1 outside it.</summary>
	private int ColumnOf(DateTimeOffset receivedAt)
	{
		var offsetTicks = (receivedAt - _from).Ticks;
		if (offsetTicks < 0 || offsetTicks >= _rangeTicks)
		{
			return -1;
		}

		return (int)((Int128)offsetTicks * _count.Length / _rangeTicks);
	}
}

/// <summary>
/// A request for the intensity series at a wavelength and delta over [From, To) in a number of columns.
/// </summary>
public sealed record IntensityQuery
{
	private IntensityQuery(ushort wavelengthNm, byte delta, DateTimeOffset from, DateTimeOffset to, int columns)
	{
		WavelengthNm = wavelengthNm;
		Delta = delta;
		From = from;
		To = to;
		Columns = columns;
	}

	public ushort WavelengthNm { get; }

	public byte Delta { get; }

	public DateTimeOffset From { get; }

	public DateTimeOffset To { get; }

	public int Columns { get; }

	/// <summary>
	/// From in the stored Unix milliseconds, rounded down, so the SQL range keeps every row the fold keeps.
	/// </summary>
	public long FromMilliseconds
	{
		get
		{
			var milliseconds = From.ToUnixTimeMilliseconds();
			return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) > From ? milliseconds - 1 : milliseconds;
		}
	}

	/// <summary>
	/// To in the stored Unix milliseconds, rounded up, so a row stamped in the millisecond To falls inside stays in.
	/// </summary>
	public long ToMilliseconds
	{
		get
		{
			var milliseconds = To.ToUnixTimeMilliseconds();
			return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) < To ? milliseconds + 1 : milliseconds;
		}
	}

	/// <summary>Builds the query, or fails for an empty range or fewer than one column.</summary>
	public static Result<IntensityQuery> Create(
		ushort wavelengthNm, byte delta, DateTimeOffset from, DateTimeOffset to, int columns)
	{
		if (columns < 1)
		{
			return Result.Fail($"The intensity series needs at least one column, got {columns}");
		}

		if (to <= from)
		{
			return Result.Fail($"The intensity series range ends at {to:O}, not after its start {from:O}");
		}

		return new IntensityQuery(wavelengthNm, delta, from, to, columns);
	}
}

/// <summary>
/// Column i holds the i-th of the equal spans of the query range; ColumnWidth is that span in whole milliseconds, at
/// least 1.
/// </summary>
public sealed record IntensitySeries(DateTimeOffset From, TimeSpan ColumnWidth, IntensityColumn[] Columns);

/// <summary>
/// Min and max of the column's covered spectra, NaN when Count is 0; UncoveredCount counts spectra of a device that
/// does not cover the wavelength.
/// </summary>
public readonly record struct IntensityColumn(double Min, double Max, int Count, int UncoveredCount);
