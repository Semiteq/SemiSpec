namespace SpectrumVisualizer.UI.Chart;

/// <summary>A tick on a log10 axis; the position is in decades.</summary>
public readonly record struct LogTick(double Position, bool IsMajor);

public static class LogAxis
{
	private const double MinimumMajorGap = 40.0;
	private const double MinimumMinorGap = 1.0;
	private const double LinearMinorSteps = 4.0;
	private const double LargestExactInteger = 9007199254740992.0;
	private const double Tolerance = 1e-9;
	private static readonly double[] _oneTwoFive = [1.0, 2.0, 5.0];
	private static readonly double[] _skippedByOneTwoFive = [3.0, 4.0, 6.0, 7.0, 8.0, 9.0];
	private static readonly double[] _insideDecade = [2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0];
	private static readonly double[] _strideMantissas = [1.0, 2.0, 3.0, 5.0];

	/// <summary>A gap stays NaN; any value below the floor, zero and negatives included, lands on it.</summary>
	public static double Project(double value, double floor)
	{
		if (double.IsNaN(value))
		{
			return value;
		}

		return value > 0.0 ? Math.Max(Math.Log10(value), floor) : floor;
	}

	public static IReadOnlyList<LogTick> Ticks(double minLog, double maxLog, double pixelLength)
	{
		var span = maxLog - minLog;
		if (!double.IsFinite(minLog) || !double.IsFinite(maxLog) || !double.IsFinite(span) || span <= 0.0
			|| !double.IsFinite(pixelLength) || pixelLength <= 0.0)
		{
			return [];
		}

		var axis = new AxisSpan(minLog, maxLog, pixelLength / span, (int)(pixelLength / MinimumMajorGap) + 2);
		var layout = ChooseMajors(axis);
		var ticks = new List<LogTick>();

		foreach (var major in layout.Majors)
		{
			ticks.Add(new LogTick(major, IsMajor: true));
		}

		AddMinors(layout, axis, ticks);
		ticks.Sort((left, right) => left.Position.CompareTo(right.Position));

		return ticks;
	}

	private static MajorLayout ChooseMajors(AxisSpan axis)
	{
		MajorLayout? chosen = null;

		foreach (var candidate in new[] { LinearMajors(axis), OneTwoFiveMajors(axis), DecadeMajors(axis) })
		{
			if (candidate is { } layout && axis.IsReadable(layout.Majors)
				&& (chosen is null || layout.Majors.Count > chosen.Value.Majors.Count))
			{
				chosen = layout;
			}
		}

		return chosen ?? new MajorLayout(MajorFamily.RangeEnds, [axis.MinLog, axis.MaxLog], Step: 0.0);
	}

	private static MajorLayout? LinearMajors(AxisSpan axis)
	{
		var bottom = Math.Pow(10.0, axis.MinLog);
		var top = Math.Pow(10.0, axis.MaxLog);
		var narrowestGapAtTop = 1.0 - Math.Pow(10.0, -MinimumMajorGap / axis.PixelsPerDecade);
		var step = SmallestStepAtLeast(top * narrowestGapAtTop, _oneTwoFive);
		var first = Math.Max(1.0, Math.Ceiling((bottom / step) - Tolerance));
		var last = Math.Floor((top / step) + Tolerance);
		var majors = LatticeMajors(axis, first, last, multiple => Math.Log10(multiple * step));

		return majors is null ? null : new MajorLayout(MajorFamily.Linear, majors, step);
	}

	private static MajorLayout? OneTwoFiveMajors(AxisSpan axis)
	{
		var decades = axis.DecadesTouched();
		if (decades - 1 > axis.MajorLimit)
		{
			return null;
		}

		var majors = new List<double>();
		for (var index = 0; index < decades; index++)
		{
			AddMantissas(axis, Math.Floor(axis.MinLog) + index, _oneTwoFive, majors);
			if (majors.Count > axis.MajorLimit)
			{
				return null;
			}
		}

		return new MajorLayout(MajorFamily.OneTwoFive, majors, Step: 0.0);
	}

	private static MajorLayout? DecadeMajors(AxisSpan axis)
	{
		var stride = Math.Max(1.0, SmallestStepAtLeast(MinimumMajorGap / axis.PixelsPerDecade, _strideMantissas));
		var first = Math.Ceiling((axis.MinLog / stride) - Tolerance);
		var last = Math.Floor((axis.MaxLog / stride) + Tolerance);
		var majors = LatticeMajors(axis, first, last, multiple => multiple * stride);

		return majors is null ? null : new MajorLayout(MajorFamily.Decades, majors, stride);
	}

	/// <summary>The majors on the multiples first..last; null for fewer than two or more than the axis holds.</summary>
	private static List<double>? LatticeMajors(
		AxisSpan axis,
		double first,
		double last,
		Func<double, double> positionOfMultiple)
	{
		var count = last - first + 1.0;

		if (!(count >= 2.0 && count <= axis.MajorLimit))
		{
			return null;
		}

		var majors = new List<double>((int)count);
		for (var index = 0; index < count; index++)
		{
			if (axis.TryPlace(positionOfMultiple(first + index), out var position))
			{
				majors.Add(position);
			}
		}

		return majors;
	}

	/// <summary>The smallest mantissa times a power of ten at or above the minimum; NaN for no finite one.</summary>
	private static double SmallestStepAtLeast(double minimum, double[] mantissas)
	{
		if (!double.IsFinite(minimum) || minimum <= 0.0)
		{
			return double.NaN;
		}

		var decade = Math.Pow(10.0, Math.Floor(Math.Log10(minimum)));
		foreach (var mantissa in mantissas)
		{
			if (mantissa * decade >= minimum * (1.0 - Tolerance))
			{
				return mantissa * decade;
			}
		}

		return 10.0 * decade;
	}

	private static void AddMinors(MajorLayout layout, AxisSpan axis, List<LogTick> ticks)
	{
		var minors = new List<double>();

		switch (layout.Family)
		{
			case MajorFamily.Linear:
				AddLinearMinors(axis, layout.Step, minors);
				break;
			case MajorFamily.OneTwoFive:
				AddMantissasOfEveryDecade(axis, _skippedByOneTwoFive, minors);
				break;
			case MajorFamily.Decades when layout.Step == 1.0:
				AddMantissasOfEveryDecade(axis, _insideDecade, minors);
				break;
			case MajorFamily.Decades:
				AddSkippedDecades(axis, layout.Step, minors);
				break;
			case MajorFamily.RangeEnds:
				break;
		}

		foreach (var minor in minors)
		{
			ticks.Add(new LogTick(minor, IsMajor: false));
		}
	}

	private static void AddLinearMinors(AxisSpan axis, double step, List<double> minors)
	{
		var minorStep = step / LinearMinorSteps;
		var first = Math.Max(1.0, Math.Ceiling((Math.Pow(10.0, axis.MinLog) / minorStep) - Tolerance));
		var last = Math.Floor((Math.Pow(10.0, axis.MaxLog) / minorStep) + Tolerance);
		var count = last - first + 1.0;
		var minorLimit = LinearMinorSteps * (axis.MajorLimit + 2);

		if (!(count <= minorLimit) || last > LargestExactInteger)
		{
			return;
		}

		for (var index = 0L; index < count; index++)
		{
			var multiple = first + index;
			if (multiple % LinearMinorSteps != 0.0 && axis.TryPlace(Math.Log10(multiple * minorStep), out var position))
			{
				minors.Add(position);
			}
		}
	}

	private static void AddMantissasOfEveryDecade(AxisSpan axis, double[] mantissas, List<double> minors)
	{
		var decades = axis.DecadesTouched();
		for (var index = 0; index < decades; index++)
		{
			AddMantissas(axis, Math.Floor(axis.MinLog) + index, mantissas, minors);
		}
	}

	private static void AddMantissas(AxisSpan axis, double decade, double[] mantissas, List<double> positions)
	{
		foreach (var mantissa in mantissas)
		{
			if (axis.TryPlace(decade + Math.Log10(mantissa), out var position))
			{
				positions.Add(position);
			}
		}
	}

	private static void AddSkippedDecades(AxisSpan axis, double stride, List<double> minors)
	{
		if (axis.PixelsPerDecade < MinimumMinorGap)
		{
			return;
		}

		var first = Math.Ceiling(axis.MinLog - Tolerance);
		var count = Math.Floor(axis.MaxLog + Tolerance) - first + 1.0;

		for (var index = 0L; index < count; index++)
		{
			var decade = first + index;
			if (decade % stride != 0.0 && axis.TryPlace(decade, out var position))
			{
				minors.Add(position);
			}
		}
	}

	private enum MajorFamily
	{
		Linear,
		OneTwoFive,
		Decades,
		RangeEnds
	}

	/// <summary>Step is the value step of the linear family and the decade stride of the decade family.</summary>
	private readonly record struct MajorLayout(MajorFamily Family, List<double> Majors, double Step);

	private readonly record struct AxisSpan(double MinLog, double MaxLog, double PixelsPerDecade, int MajorLimit)
	{
		public double DecadesTouched()
		{
			return Math.Floor(MaxLog) - Math.Floor(MinLog) + 1.0;
		}

		public bool TryPlace(double position, out double placed)
		{
			placed = Math.Clamp(position, MinLog, MaxLog);

			return position >= MinLog - Tolerance && position <= MaxLog + Tolerance;
		}

		public bool IsReadable(List<double> majors)
		{
			if (majors.Count < 2 || majors.Count > MajorLimit)
			{
				return false;
			}

			for (var index = 1; index < majors.Count; index++)
			{
				if ((majors[index] - majors[index - 1]) * PixelsPerDecade < MinimumMajorGap - Tolerance)
				{
					return false;
				}
			}

			return true;
		}
	}
}
