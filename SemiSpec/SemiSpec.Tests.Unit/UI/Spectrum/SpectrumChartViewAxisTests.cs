using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using AwesomeAssertions;

using SemiSpec.UI.Chart;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Spectrum;

/// <summary>A left press on the spectrum's intensity axis and the flyout's open, Escape and close.</summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Spectrum")]
[Trait("Category", "Unit")]
public sealed class SpectrumChartViewAxisTests : SpectrumChartViewTestBase
{
	private const double IntoTheBand = 10.0;

	private Flyout IntensityFlyout => (Flyout)View.Resources["IntensityAxisFlyout"]!;

	[AvaloniaFact]
	public void APressOnTheIntensityAxisOpensItsFlyoutAndAHeldMoveFreezesNothing()
	{
		var press = PressIntensityAxis();
		var moved = new Point(press.X + 10.0, press.Y);

		Window.MouseMove(moved, RawInputModifiers.LeftMouseButton);
		Window.MouseUp(moved, MouseButton.Left);
		Dispatcher.UIThread.RunJobs();

		IntensityFlyout.IsOpen.Should().BeTrue();
		Chart.IsFollowing.Should().BeTrue("the press opened the flyout and never reached ScottPlot's pan");
	}

	[AvaloniaFact]
	public void EscapeWithAnEditedFieldWritesNothing()
	{
		var release = PressIntensityAxis();
		Window.MouseUp(release, MouseButton.Left);
		Dispatcher.UIThread.RunJobs();
		var maximum = Field("IntensityMaximum");

		maximum.Text = "123456";
		maximum.Focus();
		TopLevel.GetTopLevel(maximum)!.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
		Dispatcher.UIThread.RunJobs();

		IntensityFlyout.IsOpen.Should().BeFalse();
		Chart.IsFollowing.Should().BeTrue("Escape discards the edited bound");
	}

	[AvaloniaFact]
	public void ClosingTheFlyoutCommitsAnEditedBound()
	{
		var release = PressIntensityAxis();
		Window.MouseUp(release, MouseButton.Left);
		Dispatcher.UIThread.RunJobs();

		Field("IntensityMaximum").Text = "123456";
		IntensityFlyout.Hide();
		Dispatcher.UIThread.RunJobs();

		Chart.IsFollowing.Should().BeFalse("the close committed the typed bound");
		Chart.Plot.Axes.GetLimits().Top.Should().Be(123456.0);
	}

	[AvaloniaFact]
	public void HoveringAnAxisBandShowsTheHandAndTheDataAreaDoesNot()
	{
		ShowSpectrum();
		var data = Chart.Plot.RenderManager.LastRender.Layout.DataRect;

		Window.MouseMove(PlotControl.TranslatePoint(new Point(data.Left - IntoTheBand, data.VerticalCenter), Window)!.Value);
		var onIntensityAxis = PlotControl.Cursor;
		Window.MouseMove(PlotControl.TranslatePoint(new Point(data.HorizontalCenter, data.Bottom + IntoTheBand), Window)!.Value);
		var onWavelengthAxis = PlotControl.Cursor;
		Window.MouseMove(PlotControl.TranslatePoint(new Point(data.HorizontalCenter, data.VerticalCenter), Window)!.Value);

		onIntensityAxis.Should().NotBeNull("a click on the intensity axis opens its flyout");
		onWavelengthAxis.Should().NotBeNull("a click on the wavelength axis opens its flyout");
		PlotControl.Cursor.Should().BeNull("the data area pans and keeps the default pointer");
	}

	/// <summary>Shows a spectrum and presses inside the intensity axis band; returns the press in window coordinates.</summary>
	private Point PressIntensityAxis()
	{
		ShowSpectrum();
		var data = Chart.Plot.RenderManager.LastRender.Layout.DataRect;
		var press = new Point(data.Left - IntoTheBand, data.VerticalCenter);
		ChartAxisRegion.TryCreateLeft(Chart.Plot)!.Contains(press).Should().BeTrue();
		var inWindow = PlotControl.TranslatePoint(press, Window)!.Value;
		Window.MouseMove(inWindow);
		Window.MouseDown(inWindow, MouseButton.Left);
		Dispatcher.UIThread.RunJobs();

		return inWindow;
	}

	private TextBox Field(string name)
	{
		return ((Control)IntensityFlyout.Content!).GetLogicalDescendants().OfType<TextBox>().Single(box => box.Name == name);
	}
}
