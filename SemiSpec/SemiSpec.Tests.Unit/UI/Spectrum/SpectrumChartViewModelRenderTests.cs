using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using ScottPlot;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Spectrum;
using SemiSpec.UI.Spectrum;

using Xunit;

using static SemiSpec.Tests.Unit.UI.Spectrum.SpectrumSamples;

namespace SemiSpec.Tests.Unit.UI.Spectrum;

[Trait("Component", "UI")]
[Trait("Area", "Spectrum")]
[Trait("Category", "Unit")]
public sealed class SpectrumChartViewModelRenderTests : IDisposable
{
	private readonly SpectrumChartViewModel _chart = new();

	public void Dispose()
	{
		_chart.Dispose();
	}

	[AvaloniaFact]
	public void ADarkFrameRendersOnTheLinearAxisWithFiniteLimits()
	{
		_chart.Show(Dark(Spectrometer.Atp2000));

		var limits = RenderedLimits();
		limits.Bottom.Should().Be(-1.0);
		limits.Top.Should().Be(1.0);

		_chart.Show(Atp2000Peak);
		var kept = RenderedLimits();
		_chart.Show(Dark(Spectrometer.Atp2000));

		var afterDark = RenderedLimits();
		afterDark.Bottom.Should().Be(kept.Bottom);
		afterDark.Top.Should().Be(kept.Top);
	}

	[AvaloniaFact]
	public void ADarkFrameRendersOnTheLogAxisAtTheFloorWithFiniteLimits()
	{
		_chart.SetDisplay(new SpectrumDisplay(IsInverted: false, IsLogarithmic: true));
		_chart.Show(Dark(Spectrometer.Atp8000));

		var limits = RenderedLimits();
		limits.Bottom.Should().Be(-0.5);
		limits.Top.Should().Be(0.5);

		_chart.Show(Atp8000Peak);
		var kept = RenderedLimits();
		_chart.Show(Dark(Spectrometer.Atp8000));

		var afterDark = RenderedLimits();
		afterDark.Bottom.Should().Be(kept.Bottom);
		afterDark.Top.Should().Be(kept.Top);
	}

	[AvaloniaFact]
	public void AnInvertedDarkFrameOnTheLogAxisRenders()
	{
		_chart.SetDisplay(new SpectrumDisplay(IsInverted: true, IsLogarithmic: true));

		_chart.Show(Dark(Spectrometer.Atp2000));

		var limits = RenderedLimits();
		limits.Bottom.Should().Be(-0.5);
		limits.Top.Should().Be(0.5);
	}

	private AxisLimits RenderedLimits()
	{
		_chart.Plot.RenderInMemory(640, 480);

		var limits = _chart.Plot.Axes.GetLimits();
		double[] sides = [limits.Left, limits.Right, limits.Bottom, limits.Top];
		sides.Should().AllSatisfy(side => double.IsFinite(side).Should().BeTrue());
		return limits;
	}
}
