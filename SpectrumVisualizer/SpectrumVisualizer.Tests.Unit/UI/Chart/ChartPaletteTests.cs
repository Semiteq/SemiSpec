using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;

using AwesomeAssertions;

using ScottPlot;

using SpectrumVisualizer.UI.Chart;

using Xunit;

using Color = ScottPlot.Color;

namespace SpectrumVisualizer.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartPaletteTests
{
	[AvaloniaTheory]
	[InlineData("Light")]
	[InlineData("Dark")]
	public void ApplyPaintsTheFiveSurfacesFromThePaletteUnderBothVariants(string variantName)
	{
		var variant = VariantOf(variantName);
		using var plot = new Plot();

		ChartPalette.Apply(plot, Application.Current!, variant);

		plot.FigureBackground.Color.Should().Be(Expected("AppPanelBackgroundBrush", variant));
		plot.DataBackground.Color.Should().Be(Expected("AppContentBackgroundBrush", variant));
		plot.Grid.MajorLineColor.Should().Be(Expected("AppSubtleLineBrush", variant));
		plot.Grid.YAxisStyle.MinorLineStyle.Color.Should().Be(Expected("AppMinorGridLineBrush", variant));

		var axisColor = Expected("AppSecondaryForegroundBrush", variant);
		foreach (var axis in plot.Axes.GetAxes())
		{
			axis.Label.ForeColor.Should().Be(axisColor);
			axis.TickLabelStyle.ForeColor.Should().Be(axisColor);
			axis.MajorTickStyle.Color.Should().Be(axisColor);
			axis.MinorTickStyle.Color.Should().Be(axisColor);
			axis.FrameLineStyle.Color.Should().Be(axisColor);
		}
	}

	[AvaloniaTheory]
	[InlineData("Light")]
	[InlineData("Dark")]
	public void ColorsResolveTheLineTheBandAndTheCentreLine(string variantName)
	{
		var variant = VariantOf(variantName);

		var colors = ChartPalette.Colors(Application.Current!, variant);

		colors.Line.Should().Be(Expected("AppAccentBrush", variant));
		colors.BandFill.Should().Be(Expected("AppAccentFillBrush", variant));
		colors.CentreLine.Should().Be(Expected("AppMarkerLineBrush", variant));
	}

	[AvaloniaTheory]
	[InlineData("Light")]
	[InlineData("Dark")]
	public void ColorsResolveTheEnvelopeTheMomentLineAndTheFailureStrip(string variantName)
	{
		var variant = VariantOf(variantName);

		var colors = ChartPalette.Colors(Application.Current!, variant);

		colors.Envelope.Should().Be(Expected("AppAccentBrush", variant));
		colors.MomentLine.Should().Be(Expected("AppMarkerLineBrush", variant));
		colors.FailureStrip.Should().Be(Expected("AppSeverityErrorBrush", variant));
	}

	[AvaloniaTheory]
	[InlineData("Light")]
	[InlineData("Dark")]
	public void ATranslucentKeyKeepsItsOpacityInTheAlpha(string variantName)
	{
		var colors = ChartPalette.Colors(Application.Current!, VariantOf(variantName));

		colors.BandFill.A.Should().Be(64);
		colors.Line.A.Should().Be(255);
	}

	[AvaloniaFact]
	public void AMissingKeyThrowsNamingTheKey()
	{
		var act = () => ChartPalette.Colors(new Border(), ThemeVariant.Light);

		act.Should().Throw<InvalidOperationException>().WithMessage("*'AppAccentBrush'*");
	}

	private static ThemeVariant VariantOf(string variantName)
	{
		return variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
	}

	// The palette's own colour, read independently of ChartPalette: ARGB with the brush opacity in the alpha.
	private static Color Expected(string key, ThemeVariant variant)
	{
		Application.Current!.TryGetResource(key, variant, out var value).Should().BeTrue();
		var brush = (ISolidColorBrush)value!;
		var alpha = (byte)Math.Round(brush.Color.A * brush.Opacity);
		return new Color(brush.Color.R, brush.Color.G, brush.Color.B, alpha);
	}
}
