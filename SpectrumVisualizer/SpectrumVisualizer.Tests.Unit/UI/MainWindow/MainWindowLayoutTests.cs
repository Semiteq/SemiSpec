using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.UI.Link;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;

using MainWindowView = SpectrumVisualizer.UI.MainWindow.MainWindow;

namespace SpectrumVisualizer.Tests.Unit.UI.MainWindow;

/// <summary>The desktop UI checklist for the size and alignment of the connection row and the status bar.</summary>
[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowLayoutTests : MainWindowLayoutTestBase
{
	private const double LargeWidth = 2560;

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

	private sealed record RowShape(
		Size Window,
		double ConnectionRowHeight,
		double StatusBarHeight,
		string[] ConnectionRowParts,
		string[] StatusBarParts);
}
