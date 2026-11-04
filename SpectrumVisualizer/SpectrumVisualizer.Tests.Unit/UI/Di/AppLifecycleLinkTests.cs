using System.Reactive.Disposables;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Tests.Unit.UI.Link;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.MainWindow;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SpectrumVisualizer.Tests.Unit.UI.Di;

/// <summary>The window's open and close against a real link and distributor.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class AppLifecycleLinkTests : AppLifecycleTestBase
{
	[AvaloniaFact]
	public async Task ClosingTheWindowWaitsUntilTheLinkReportsDisconnected()
	{
		await using var port = new FakePortStream();
		port.HoldDispose();

		// Disposed before the port, so a failing assertion cannot leave the port's dispose waiting forever.
		using var release = Disposable.Create(port.ReleaseDispose);
		PortProvider.QueueOpen(port);
		using var provider = BuildContainer();
		var link = provider.GetRequiredService<SerialLink>();
		var window = ShowMainWindow(provider);
		link.Connect(PortName);
		await port.WaitForPendingReadAsync().WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

		// Subscribed after the app's handler, so each entry is the decision the app made for that close. The held
		// dispose keeps the session running until the app has decided the first close.
		var closeCancelled = new List<bool>();
		window.Closing += (_, e) =>
		{
			closeCancelled.Add(e.Cancel);
			port.ReleaseDispose();
		};
		var stateAtClose = new TaskCompletionSource<LinkState>();
		window.Closed += (_, _) => stateAtClose.TrySetResult(link.Status.State);

		window.Close();

		stateAtClose.Task.IsCompleted.Should().BeFalse("the first close waits for the link");
		var state = await stateAtClose.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
		state.Should().Be(LinkState.Disconnected);
		closeCancelled.Should().Equal(true, false);
		port.IsDisposed.Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task ClosingTheWindowDuringTheReopenWaitStopsTheLink()
	{
		await using var port = new FakePortStream();
		PortProvider.QueueOpen(port);
		using var provider = BuildContainer();
		var link = provider.GetRequiredService<SerialLink>();
		var window = ShowMainWindow(provider);
		var portUnavailable = new TaskCompletionSource();
		link.StatusChanged += (_, status) =>
		{
			if (status.State == LinkState.PortUnavailable)
			{
				portUnavailable.TrySetResult();
			}
		};
		link.Connect(PortName);
		await port.WaitForPendingReadAsync().WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
		port.PushZeroRead();
		await portUnavailable.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

		var statusAtClose = new TaskCompletionSource<LinkStatus>();
		window.Closed += (_, _) => statusAtClose.TrySetResult(link.Status);
		window.Close();

		var status = await statusAtClose.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
		status.Should().Be(new LinkStatus(LinkState.Disconnected, null, null, 0, null),
			"an open during the wait would meet an empty open queue and end the session with a fault");
		PortProvider.OpenCount.Should().Be(1);
	}

	[AvaloniaFact]
	public async Task WithAnIdleLinkTheWindowClosesOnceTheDistributorStops()
	{
		using var provider = BuildContainer();
		var window = ShowMainWindow(provider);
		var closed = new TaskCompletionSource();
		window.Closed += (_, _) => closed.TrySetResult();

		window.Close();

		await closed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
	}

	[AvaloniaFact]
	public async Task OpeningTheWindowStartsTheFrameDistributor()
	{
		await using var port = new FakePortStream();
		PortProvider.QueueOpen(port);
		using var provider = BuildContainer();
		var link = provider.GetRequiredService<SerialLink>();
		var received = new TaskCompletionSource<ReceivedFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var subscription = provider.GetRequiredService<FrameDistributor>().Frames
			.Subscribe(frame => received.TrySetResult(frame));
		var window = ShowMainWindow(provider);
		var closed = new TaskCompletionSource();
		window.Closed += (_, _) => closed.TrySetResult();
		link.Connect(PortName);
		await port.WaitForPendingReadAsync().WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
		var frame = SpectrumFrame(Spectrometer.Atp8000, 1);

		port.PushBytes(FrameEncoder.Encode(frame));

		var first = await received.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
		first.Frame.Should().Be(frame);
		window.Close();
		await closed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
	}

	[AvaloniaFact]
	public async Task ClosingTheWindowStopsTheDistributorAfterTheLink()
	{
		await using var port = new FakePortStream();
		PortProvider.QueueOpen(port);
		using var provider = BuildContainer();
		var link = provider.GetRequiredService<SerialLink>();
		var window = ShowMainWindow(provider);
		var closed = new TaskCompletionSource();
		window.Closed += (_, _) => closed.TrySetResult();
		link.Connect(PortName);
		await port.WaitForPendingReadAsync().WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

		window.Close();

		await closed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
		var templates = LoggedTemplates();
		var linkStopped = templates.IndexOf("The link on port {Port} stopped");
		var distributorStopped = templates.IndexOf(DistributorStopped);
		linkStopped.Should().BeGreaterThanOrEqualTo(0);
		distributorStopped.Should().BeGreaterThan(linkStopped);
	}

	[AvaloniaFact]
	public async Task AfterTheCloseADistributorSubscriberReceivesNothingMore()
	{
		await using var firstPort = new FakePortStream();
		await using var secondPort = new FakePortStream();
		PortProvider.QueueOpen(firstPort);
		PortProvider.QueueOpen(secondPort);
		using var provider = BuildContainer();
		var link = provider.GetRequiredService<SerialLink>();
		var received = new List<ReceivedFrame>();
		var firstReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var subscription = provider.GetRequiredService<FrameDistributor>().Frames
			.Subscribe(frame =>
			{
				lock (received)
				{
					received.Add(frame);
				}

				firstReceived.TrySetResult();
			});
		var window = ShowMainWindow(provider);
		var closed = new TaskCompletionSource();
		window.Closed += (_, _) => closed.TrySetResult();
		link.Connect(PortName);
		await firstPort.WaitForPendingReadAsync().WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
		firstPort.PushBytes(FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 1)));
		await firstReceived.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
		window.Close();
		await closed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

		// The link reconnects after the close and writes a frame; with the distributor stopped, the frame stays in the
		// channel, so reading it back proves the write happened and that no subscriber was handed it.
		link.Connect(PortName);
		await secondPort.WaitForPendingReadAsync().WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
		var lateFrame = SpectrumFrame(Spectrometer.Atp2000, 2);
		secondPort.PushBytes(FrameEncoder.Encode(lateFrame));

		await link.Frames.WaitToReadAsync(TestContext.Current.CancellationToken).AsTask()
			.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
		link.Frames.TryRead(out var unread).Should().BeTrue();
		unread!.Frame.Should().Be(lateFrame);
		lock (received)
		{
			received.Should().ContainSingle();
		}

		await link.DisconnectAsync();
	}

	[AvaloniaFact]
	public void OpeningTheWindowFillsThePortList()
	{
		using var provider = BuildContainer();
		var window = ShowMainWindow(provider);

		provider.GetRequiredService<ConnectionBarViewModel>().Ports.Should().Equal(PortName);
		window.Close();
	}
}
