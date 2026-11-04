using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using ScottPlot;

using SpectrumVisualizer.Core.Protocol;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Trend;

[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendChartViewModelRenderTests : TrendChartTestBase
{
	[AvaloniaFact]
	public void ARangeWithGapsRendersWithFiniteLimits()
	{
		AddFlat(0, 100);
		AddFlat(1, 200, Spectrometer.Atp2000);
		AddFlat(2, 300);
		AddFlat(30, 150);
		Sample();

		var limits = RenderedLimits();

		limits.Bottom.Should().BeApproximately(90.0, 1e-9);
		limits.Top.Should().BeApproximately(310.0, 1e-9);
	}

	[AvaloniaFact]
	public void AnAllGapRangeRendersTheEmptyAxes()
	{
		AddFlat(0, 100, Spectrometer.Atp2000);
		AddFlat(1, 100, Spectrometer.Atp2000);
		Sample();

		var limits = RenderedLimits();

		Chart.HasPoints.Should().BeTrue();
		limits.Bottom.Should().Be(0.0);
		limits.Top.Should().Be(1.0);
	}

	[AvaloniaFact]
	public void TheChartRendersBeforeTheFirstPoint()
	{
		var limits = RenderedLimits();

		Chart.HasPoints.Should().BeFalse();
		limits.Bottom.Should().Be(0.0);
		limits.Top.Should().Be(1.0);
	}

	private AxisLimits RenderedLimits()
	{
		Chart.Plot.RenderInMemory(640, 480);
		var limits = Chart.Plot.Axes.GetLimits();
		new[] { limits.Left, limits.Right, limits.Bottom, limits.Top }
			.Should().OnlyContain(edge => double.IsFinite(edge));
		return limits;
	}
}
