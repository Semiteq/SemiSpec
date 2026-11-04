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
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Spectrum;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;
using static SpectrumVisualizer.Tests.Unit.UI.Spectrum.SpectrumSamples;

using MainWindowView = SpectrumVisualizer.UI.MainWindow.MainWindow;

namespace SpectrumVisualizer.Tests.Unit.UI.MainWindow;

/// <summary>The desktop UI checklist for the size and alignment of the rows and the signal pane.</summary>
[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowLayoutTests : MainWindowLayoutTestBase
{
	private const double LargeWidth = 2560;
	private const double OpeningWidth = 1280;
	private const double PaneBorder = 1;
	private const double PanePadding = 16;
	private const double RowSpacing = 8;
	private const double LabelToField = 12;
	private const double SectionHeaderAbove = 16;

	private static readonly string[] _paneLabels =
		["DeviceLabel", "WavelengthLabel", "WindowLabel", "AmplitudeLabel", "SnrLabel", "QLabel", "UpdatedLabel"];

	private static readonly string[] _paneValues =
		["DeviceValue", "WavelengthValue", "WindowValue", "AmplitudeValue", "SnrValue", "QValue", "UpdatedValue"];

	private static readonly string[] _paneControls = ["InvertSpectrum", "LogarithmicScale", "ResetScale", "StickToZero"];

	private static readonly string[] _paneParts =
		["SignalHeader", .. _paneLabels, .. _paneValues, "ChartHeader", .. _paneControls];

	private static readonly string[] _connectionRowParts =
		["PortLabel", "PortList", "RefreshPorts", "Connect", "Disconnect"];

	private static readonly string[] _statusSegments =
		["LinkText", "PortText", "Atp2000Text", "Atp8000Text", "LostSpectraText"];

	[AvaloniaFact]
	public void BelowTheMinimumTheWindowStopsAtItAndEveryFixedControlKeepsItsWidth()
	{
		var window = Realise();
		var opening = PartWidths(window);

		window.Width = 400;
		Dispatcher.UIThread.RunJobs();

		window.Bounds.Width.Should().Be(MainWindowView.MinimumWidth);
		Named<Border>(window, "ConnectionRow").Bounds.Width.Should().Be(MainWindowView.MinimumWidth);
		Named<Border>(window, "StatusBar").Bounds.Width.Should().Be(MainWindowView.MinimumWidth);
		PartWidths(window).Should().Equal(opening, "no control of either row shrinks at the minimum");
	}

	[AvaloniaFact]
	public void AtALargeWidthBothRowsStayLeftAlignedWithTheirGapsUnchanged()
	{
		var window = Realise();
		var opening = PartLefts(window);

		window.Width = LargeWidth;
		Dispatcher.UIThread.RunJobs();

		window.Bounds.Width.Should().Be(LargeWidth);
		PartLefts(window).Should().Equal(opening, "only the rows' empty right end grows");
	}

	[AvaloniaFact]
	public async Task EveryLinkStateKeepsTheWindowAndBothRowsAtTheirSize()
	{
		var window = Realise();
		var shapes = new List<(LinkState State, RowShape Shape)> { (SerialLink.Status.State, ShapeOf(window)) };
		async Task Record()
		{
			await Waits.StatusBarSettledAsync(StatusBar, SerialLink);
			window.UpdateLayout();
			shapes.Add((SerialLink.Status.State, ShapeOf(window)));
		}

		PortProvider.QueueOpenFailure(new PortBusyError(PortName));
		SerialLink.Connect(PortName);
		await Waits.UntilAsync(() => SerialLink.Status.State == LinkState.PortUnavailable, pumpDispatcher: true);
		await Record();
		PortProvider.QueueOpen(Port);
		LinkScheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks);
		await WaitForReadAsync();
		await Record();
		await AtAsync(2, () => Port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));
		await Record();
		await AtAsync(12, Port.PushTimeout);
		await Record();
		await AtAsync(13, () => Port.PushBytes(Corrupted(SpectrumFrame(Spectrometer.Atp2000, 2))));
		await AtAsync(18, () => Port.PushBytes(Corrupted(SpectrumFrame(Spectrometer.Atp2000, 3))));
		await AtAsync(23, Port.PushTimeout);
		await Record();
		await SerialLink.DisconnectAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);
		await Record();

		shapes.Select(shape => shape.State).Should().Equal(
			LinkState.Disconnected,
			LinkState.PortUnavailable,
			LinkState.Waiting,
			LinkState.Receiving,
			LinkState.NotResponding,
			LinkState.OtherDevice,
			LinkState.Disconnected);
		shapes.Select(shape => shape.Shape).Should().AllBeEquivalentTo(shapes[0].Shape);
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

		formOrigin.Should().Be(new Point(PaneBorder + PanePadding, PanePadding), "the left border and the pane padding");
		Cell("SignalHeader").Top.Should().Be(0, "the first section header has 0 above");
		string[] signalRows = ["SignalHeader", .. _paneLabels];
		string[] chartRows = ["ChartHeader", .. _paneControls];
		signalRows.Zip(signalRows.Skip(1), Gap).Concat(chartRows.Zip(chartRows.Skip(1), Gap))
			.Should().AllSatisfy(gap => gap.Should().Be(RowSpacing, "8 between rows and below a section header"));
		Gap(_paneLabels[^1], "ChartHeader").Should().Be(SectionHeaderAbove, "16 above a later section header");

		var fieldLeft = formOrigin.X + Cell(_paneLabels[0]).Right + LabelToField;
		_paneLabels.Select(Left).Should().AllSatisfy(left => left.Should().Be(formOrigin.X, "one label edge"));
		_paneValues.Concat(_paneControls).Select(Left)
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

	private static IEnumerable<Control> Parts(Window window)
	{
		return _connectionRowParts.Concat(_statusSegments).Select(name => Named<Control>(window, name));
	}

	private static List<double> PartWidths(Window window)
	{
		return [.. Parts(window).Select(part => part.Bounds.Width)];
	}

	private static List<double> PartLefts(Window window)
	{
		return [.. Parts(window).Select(part => part.TranslatePoint(default, window)!.Value.X)];
	}

	private static RowShape ShapeOf(Window window)
	{
		static string[] VisibleNames(Control row)
		{
			return [.. row.GetVisualDescendants().OfType<Control>()
				.Where(control => control.IsEffectivelyVisible && !string.IsNullOrEmpty(control.Name))
				.Select(control => control.Name!)];
		}

		var connectionRow = Named<Border>(window, "ConnectionRow");
		var statusRow = Named<Border>(window, "StatusBar");

		return new RowShape(
			window.Bounds.Size,
			connectionRow.Bounds.Height,
			statusRow.Bounds.Height,
			VisibleNames(connectionRow),
			VisibleNames(statusRow));
	}

	/// <summary>Moves virtual time first, so the link stamps the step with the given second.</summary>
	private async Task AtAsync(double seconds, Action push)
	{
		LinkScheduler.AdvanceTo(TimeSpan.FromSeconds(seconds).Ticks);
		push();
		await WaitForReadAsync();
	}

	private Task WaitForReadAsync()
	{
		return Port.WaitForPendingReadAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);
	}

	private sealed record PaneShape(Size Window, Rect Pane, Rect Chart);

	private sealed record RowShape(
		Size Window,
		double ConnectionRowHeight,
		double StatusBarHeight,
		string[] ConnectionRowParts,
		string[] StatusBarParts);
}
