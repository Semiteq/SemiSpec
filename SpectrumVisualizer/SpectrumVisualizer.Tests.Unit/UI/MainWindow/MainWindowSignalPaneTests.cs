using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.UI.Chart;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Spectrum;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.UI.Spectrum.SpectrumSamples;

using MainWindowView = SpectrumVisualizer.UI.MainWindow.MainWindow;

namespace SpectrumVisualizer.Tests.Unit.UI.MainWindow;

/// <summary>The signal pane in the shown window: its rows and controls, its insets and its size at any width.</summary>
[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowSignalPaneTests : MainWindowLayoutTestBase
{
	private const double OpeningWidth = 1280;

	private static readonly string[] _paneParts =
		["SignalHeader", .. PaneLabels, .. PaneValues, "ChartHeader", .. PaneControls];

	[AvaloniaFact]
	public void TheSignalPaneRealisesEveryRowAndControl()
	{
		var panel = SignalPanel(Realise());

		PaneLabels.Concat(PaneValues).Concat(PaneHeaders).Select(name => Named<TextBlock>(panel, name))
			.Should().OnlyContain(text => text.IsEffectivelyVisible && text.Bounds.Width > 0);

		PaneHeaders.Select(name => Named<TextBlock>(panel, name).Text)
			.Should().Equal(Resources.SignalTitle, Resources.ChartTitle);
		PaneLabels.Select(name => Named<TextBlock>(panel, name).Text).Should().Equal(
			Resources.SignalDevice,
			Resources.SignalWavelength,
			Resources.SignalWindow,
			Resources.SignalAmplitude,
			Resources.SignalSnr,
			Resources.SignalQ,
			Resources.SignalUpdated);
		PaneValues.Select(name => Named<TextBlock>(panel, name).Text)
			.Should().OnlyContain(text => text == Resources.StatusNoValue, "no spectrum has arrived yet");

		var invert = Named<CheckBox>(panel, "InvertSpectrum");
		var logarithmic = Named<CheckBox>(panel, "LogarithmicScale");
		invert.Content.Should().BeOfType<TextBlock>().Which.Text.Should().Be(Resources.ChartInvert);
		logarithmic.Content.Should().BeOfType<TextBlock>().Which.Text.Should().Be(Resources.ChartLogScale);
		new[] { invert, logarithmic }.Should().OnlyContain(box => box.IsEffectivelyVisible && box.IsChecked == false);

		var resetScale = Named<Button>(panel, "ResetScale");
		var stickToZero = Named<Button>(panel, "StickToZero");
		resetScale.Content.Should().Be(Resources.ChartResetScale);
		stickToZero.Content.Should().Be(Resources.ChartStickToZero);
		new[] { resetScale, stickToZero }
			.Should().OnlyContain(button => button.IsEffectivelyVisible && !button.IsEffectivelyEnabled);
	}

	[AvaloniaFact]
	public void ACheckboxReachesTheChart()
	{
		var window = Realise();
		SpectrumSource.Current = Atp8000Peak;
		SpectrumSource.Signal();
		Dispatcher.UIThread.RunJobs();
		var panel = SignalPanel(window);

		Named<CheckBox>(panel, "LogarithmicScale").IsChecked = true;
		Dispatcher.UIThread.RunJobs();

		Spectrum.IsLogarithmic.Should().BeTrue();
		Spectrum.Chart.Plot.Axes.Left.TickGenerator.Should().BeOfType<LogTickGenerator>();
		Named<TextBlock>(panel, "DeviceValue").Text.Should().Be(Atp8000Peak.Measurement.Device.DisplayName);
		Named<Button>(panel, "StickToZero").IsEffectivelyEnabled.Should().BeTrue("a spectrum is shown");
	}

	[AvaloniaFact]
	public void TheSignalPaneInsetsReadOffTheSpacingTable()
	{
		var window = Realise();
		var pane = Named<Border>(window, "SignalPane");
		var form = (Grid)pane.GetVisualDescendants().OfType<SpectrumPanel>().Single().Content!;
		var formOrigin = form.TranslatePoint(default, pane)!.Value;

		// The arranged slot is the grid cell, so a centred text's rounding inside its row does not count as a gap.
		Rect Cell(string name)
		{
			return LayoutInformation.GetPreviousArrangeBounds(Named<Control>(pane, name))!.Value;
		}

		double Gap(string upper, string lower)
		{
			var lowerPart = Named<Control>(pane, lower);
			return Cell(lower).Top + lowerPart.Margin.Top - Cell(upper).Bottom;
		}

		double Left(string name)
		{
			return Named<Control>(pane, name).TranslatePoint(default, pane)!.Value.X;
		}

		formOrigin.Should().Be(
			new Point(PaneBorder + PanePadding, PanePadding),
			"the left border and the pane padding");
		Cell("SignalHeader").Top.Should().Be(0, "the first section header has 0 above");
		string[] signalRows = ["SignalHeader", .. PaneLabels];
		string[] chartRows = ["ChartHeader", .. PaneControls];
		signalRows.Zip(signalRows.Skip(1), Gap).Concat(chartRows.Zip(chartRows.Skip(1), Gap))
			.Should().AllSatisfy(gap => gap.Should().Be(RowSpacing, "8 between rows and below a section header"));
		Gap(PaneLabels[^1], "ChartHeader").Should().Be(SectionHeaderAbove, "16 above a later section header");

		var fieldLeft = formOrigin.X + Cell(PaneLabels[0]).Right + LabelToField;
		PaneLabels.Select(Left).Should().AllSatisfy(left => left.Should().Be(formOrigin.X, "one label edge"));
		PaneValues.Concat(PaneControls).Select(Left)
			.Should().AllSatisfy(left => left.Should().Be(fieldLeft, "one field edge, 12 after the label column"));
	}

	[AvaloniaFact]
	public void AtTheMinimumWidthTheSignalPaneShowsEveryRowAndTheChartKeepsItsFloor()
	{
		var window = Realise();
		MessagePanel.ReportFailure(new Error("port busy"), NullLogger.Instance);

		window.Width = 400;
		Dispatcher.UIThread.RunJobs();

		var pane = Named<Border>(window, "SignalPane");
		var contentBottom = pane.Bounds.Height - pane.Padding.Bottom;
		window.Bounds.Width.Should().Be(MainWindowView.MinimumWidth);
		Named<Border>(window, "MessagePanel").IsVisible.Should().BeTrue("the shortest pane is the worst case");
		pane.Bounds.Width.Should().Be(SpectrumPanel.PaneWidth);
		Named<Border>(window, "SpectrumPane").Bounds.Width.Should().BeGreaterThanOrEqualTo(ChartFloorWidth);
		_paneParts.Select(name => Named<Control>(pane, name)).Should().OnlyContain(
			part => part.IsEffectivelyVisible
				&& part.TranslatePoint(new Point(0, part.Bounds.Height), pane)!.Value.Y <= contentBottom,
			"every row and control shows whole inside the pane padding");
	}

	[AvaloniaFact]
	public void AtALargeWidthTheSignalPaneKeepsItsWidthAndContentAndOnlyTheChartGrows()
	{
		var window = Realise();
		var pane = Named<Border>(window, "SignalPane");
		var chart = Named<Border>(window, "SpectrumPane");
		var openingChartWidth = chart.Bounds.Width;
		var openingOffsets = PaneOffsets(pane);

		window.Width = LargeWidth;
		Dispatcher.UIThread.RunJobs();

		window.Bounds.Width.Should().Be(LargeWidth);
		pane.Bounds.Width.Should().Be(SpectrumPanel.PaneWidth);
		(pane.TranslatePoint(default, window)!.Value.X + pane.Bounds.Width)
			.Should().Be(LargeWidth, "the pane ends at the window's right edge");
		chart.Bounds.Width.Should().Be(
			openingChartWidth + LargeWidth - OpeningWidth,
			"the chart takes every added pixel");
		PaneOffsets(pane).Should().Equal(openingOffsets, "the pane content stays where it was inside the pane");
	}

	[AvaloniaFact]
	public void EverySpectrumStateKeepsTheWindowAndThePaneBounds()
	{
		var window = Realise();
		var shapes = new List<(string State, PaneShape Shape)> { ("no spectrum", PaneShapeOf(window)) };
		void Record(string state)
		{
			Dispatcher.UIThread.RunJobs();
			window.UpdateLayout();
			shapes.Add((state, PaneShapeOf(window)));
		}

		void Show(DisplayedSpectrum spectrum)
		{
			SpectrumSource.Current = spectrum;
			SpectrumSource.Signal();
		}

		Show(Atp2000Peak);
		Record("ATP-2000");
		Show(Atp8000Peak);
		Record("ATP-8000");
		Show(Dark(Spectrometer.Atp8000));
		Record("dark");
		Spectrum.IsLogarithmic = true;
		Record("log on");
		Spectrum.IsInverted = true;
		Record("inverted");
		Spectrum.Chart.TakeManualScale();
		Record("frozen scale");
		var opening = shapes[0].Shape;

		MessagePanel.ReportFailure(new Error("port busy"), NullLogger.Instance);
		Dispatcher.UIThread.RunJobs();
		window.UpdateLayout();

		shapes.Should().HaveCount(7);
		Spectrum.Chart.IsFollowing.Should().BeFalse();
		shapes.Select(shape => shape.Shape).Should().AllBeEquivalentTo(opening);
		var messageHeight = Named<Border>(window, "MessagePanel").Bounds.Height;
		messageHeight.Should().BePositive();
		PaneShapeOf(window).Should().Be(
			opening with
			{
				Pane = opening.Pane.WithHeight(opening.Pane.Height - messageHeight),
				Chart = opening.Chart.WithHeight(opening.Chart.Height - messageHeight),
			},
			"the message panel row takes its height from the content row and never changes the window size");
	}

	private static List<Point> PaneOffsets(Border pane)
	{
		return [.. _paneParts.Select(name => Named<Control>(pane, name).TranslatePoint(default, pane)!.Value)];
	}

	private static PaneShape PaneShapeOf(Window window)
	{
		return new PaneShape(
			window.Bounds.Size,
			Named<Border>(window, "SignalPane").Bounds,
			Named<Border>(window, "SpectrumPane").Bounds);
	}

	private static SpectrumPanel SignalPanel(Window window)
	{
		return Named<Border>(window, "SignalPane").Child.Should().BeOfType<SpectrumPanel>().Which;
	}

	private sealed record PaneShape(Size Window, Rect Pane, Rect Chart);
}
