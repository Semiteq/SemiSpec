using System.Globalization;

using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Core.Trend;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Trend;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Trend;

[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendReadoutTests
{
	private static readonly DateTimeOffset _start = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
	private static readonly TimeSpan _width = TimeSpan.FromSeconds(1);
	private static readonly TrendKey _key = new(650, 10);

	[Fact]
	public void TheFirstLineIsTheLocalTimeOfTheColumn()
	{
		var lines = Read([Value(0, 1.5)], 0);

		lines[0].Should().Be(_start.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture));
	}

	[Fact]
	public void AColumnOfOneValueShowsTheValueAndTheCount()
	{
		var lines = Read([Value(0, 1234.5678)], 0);

		lines.Skip(1).Should().Equal(Number(1234.5678), Resources.FormatTrendReadoutCountFormat(Count(1)));
	}

	[Fact]
	public void AColumnOfSeveralValuesShowsTheRange()
	{
		var column = Value(0, 10.0) with { Max = 20.25, Count = 3, Last = 20.25 };

		var lines = Read([column], 0);

		lines.Skip(1).Should().Equal(
			Resources.FormatTrendReadoutRangeFormat(Number(10.0), Number(20.25)),
			Resources.FormatTrendReadoutCountFormat(Count(3)));
	}

	[Fact]
	public void AnEmptyColumnWithNothingAfterItReadsNoData()
	{
		var lines = Read([Value(0, 1.0), Empty(1), Empty(2)], 1);

		lines.Skip(1).Should().Equal(Resources.TrendReadoutNoData);
	}

	[Theory]
	[InlineData(2)]
	[InlineData(201)]
	public void AnEmptyColumnBeforeALossBreakReadsTheLostCount(int lost)
	{
		var afterLoss = Value(2, 3.0) with { BreakCount = 1, LostCount = lost };

		var lines = Read([Value(0, 1.0), Empty(1), afterLoss], 1);

		lines.Skip(1).Should().Equal(Resources.FormatTrendReadoutLostFormat(Count(lost)));
	}

	[Fact]
	public void TheColumnOfALossBreakShowsItsValueAndTheLostCount()
	{
		var afterLoss = Value(0, 3.0) with { BreakCount = 1, LostCount = 1 };

		var lines = Read([afterLoss], 0);

		lines.Skip(1).Should().Equal(
			Number(3.0),
			Resources.FormatTrendReadoutCountFormat(Count(1)),
			Resources.FormatTrendReadoutLostFormat(Count(1)));
	}

	[Fact]
	public void ANoSpectraGapReadsNoSpectraAndTheFailureAHeartbeatInItCarried()
	{
		var heartbeat = Empty(2) with { Failed = BoardState.Atp8000MeasurementFailed };
		var afterSilence = Value(4, 2.0) with { BreakCount = 1 };

		var lines = Read([Value(0, 1.0), Empty(1), heartbeat, Empty(3), afterSilence], 1);

		lines.Skip(1).Should().Equal(
			Resources.TrendReadoutNoSpectra,
			Resources.FormatTrendReadoutFailedFormat("ATP-8000"));
	}

	[Fact]
	public void AnUncoveredColumnNamesTheWavelength()
	{
		var uncovered = Empty(0) with { UncoveredCount = 1 };

		var lines = Read([uncovered], 0);

		lines.Skip(1).Should().Equal(Resources.FormatTrendReadoutUncoveredFormat(Count(_key.WavelengthNm)));
	}

	[Fact]
	public void AnEmptyColumnBetweenConnectedValuesReadsTheValueTheLineComesFrom()
	{
		var lines = Read([Value(0, 1.0), Empty(1), Value(2, 2.0)], 1);

		lines.Skip(1).Should().Equal(Number(1.0), Resources.FormatTrendReadoutCountFormat(Count(1)));
	}

	[Fact]
	public void EveryFailedDeviceGetsItsOwnLine()
	{
		var column = Value(0, 1.0) with
		{
			Failed = BoardState.Atp2000MeasurementFailed | BoardState.Atp8000MeasurementFailed,
		};

		var lines = Read([column], 0);

		lines.TakeLast(2).Should().Equal(
			Resources.FormatTrendReadoutFailedFormat("ATP-2000"),
			Resources.FormatTrendReadoutFailedFormat("ATP-8000"));
	}

	private static string[] Read(IntensityColumn[] columns, int index)
	{
		var series = new IntensitySeries(_start, _start + (_width * columns.Length), _width, columns);
		return TrendReadout.For(series, index, _key).Split('\n');
	}

	private static IntensityColumn Value(int second, double value)
	{
		return new IntensityColumn(At(second), value, value, 1, 0, value, 0, 0, BoardState.None);
	}

	private static IntensityColumn Empty(int second)
	{
		return new IntensityColumn(At(second), double.NaN, double.NaN, 0, 0, double.NaN, 0, 0, BoardState.None);
	}

	private static DateTimeOffset At(int second)
	{
		return _start + (_width * second);
	}

	private static string Number(double value)
	{
		return value.ToString("G6", CultureInfo.CurrentCulture);
	}

	private static string Count(int count)
	{
		return count.ToString(CultureInfo.CurrentCulture);
	}
}
