using System.Reactive.Disposables;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Serilog;

using SpectrumVisualizer.Core;
using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Tests.Unit.Core.Store;
using SpectrumVisualizer.Tests.Unit.UI.Link;
using SpectrumVisualizer.Tests.Unit.UI.Logging;
using SpectrumVisualizer.UI;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Logging;
using SpectrumVisualizer.UI.MainWindow;
using SpectrumVisualizer.UI.Store;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SpectrumVisualizer.Tests.Unit.UI.Di;

/// <summary>The production container with a fake port provider, so the window's close meets a real link.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class AppLifecycleTests : IAsyncDisposable
{
	private const string PortName = "COM3";

	private const string DistributorStopped = "The frame distributor stopped";
	private const string StoreStopped = "The spectrum store stopped: {Stored} frames stored, {Dropped} frames dropped";

	private static readonly TimeSpan _waitLimit = TimeSpan.FromSeconds(5);

	// A store whose writer still runs when the test ends closes its file within this long.
	private static readonly TimeSpan _storeStopLimit = TimeSpan.FromSeconds(10);

	private readonly FakeSerialPortProvider _portProvider = new(PortName);
	private readonly CollectingSink _sink = new();
	private readonly LoggingSetup _logging;
	// Every test in the process gets the same instance file name, so each test owns its root.
	private readonly TempFolder _root = new();
	private readonly AppDataPaths _paths;
	private readonly List<SpectrumStore> _stores = [];

	public AppLifecycleTests()
	{
		// Verbose, so the frame distributor's Debug start and stop lines reach the sink.
		var logLevelControl = new LogLevelControl { IsVerbose = true };
		var logger = new LoggerConfiguration()
			.MinimumLevel.ControlledBy(logLevelControl.Switch)
			.WriteTo.Sink(_sink)
			.CreateLogger();
		_logging = new LoggingSetup(logger, logLevelControl);
		_paths = AppDataPaths.Under(_root.Root).Value;
	}

	[AvaloniaFact]
	public async Task ClosingTheWindowWaitsUntilTheLinkReportsDisconnected()
	{
		await using var port = new FakePortStream();
		port.HoldDispose();

		// Disposed before the port, so a failing assertion cannot leave the port's dispose waiting forever.
		using var release = Disposable.Create(port.ReleaseDispose);
		_portProvider.QueueOpen(port);
		using var provider = BuildContainer();
		var link = provider.GetRequiredService<SerialLink>();
		var window = ShowMainWindow(provider);
		link.Connect(PortName);
		await port.WaitForPendingReadAsync().WaitAsync(_waitLimit, TestContext.Current.CancellationToken);

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
		var state = await stateAtClose.Task.WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
		state.Should().Be(LinkState.Disconnected);
		closeCancelled.Should().Equal(true, false);
		port.IsDisposed.Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task ClosingTheWindowDuringTheReopenWaitStopsTheLink()
	{
		await using var port = new FakePortStream();
		_portProvider.QueueOpen(port);
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
		await port.WaitForPendingReadAsync().WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
		port.PushZeroRead();
		await portUnavailable.Task.WaitAsync(_waitLimit, TestContext.Current.CancellationToken);

		var statusAtClose = new TaskCompletionSource<LinkStatus>();
		window.Closed += (_, _) => statusAtClose.TrySetResult(link.Status);
		window.Close();

		var status = await statusAtClose.Task.WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
		status.Should().Be(new LinkStatus(LinkState.Disconnected, null, null, 0, null),
			"an open during the wait would meet an empty open queue and end the session with a fault");
		_portProvider.OpenCount.Should().Be(1);
	}

	[AvaloniaFact]
	public async Task WithAnIdleLinkTheWindowClosesOnceTheDistributorStops()
	{
		using var provider = BuildContainer();
		var window = ShowMainWindow(provider);
		var closed = new TaskCompletionSource();
		window.Closed += (_, _) => closed.TrySetResult();

		window.Close();

		await closed.Task.WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
	}

	[AvaloniaFact]
	public async Task OpeningTheWindowStartsTheFrameDistributor()
	{
		await using var port = new FakePortStream();
		_portProvider.QueueOpen(port);
		using var provider = BuildContainer();
		var link = provider.GetRequiredService<SerialLink>();
		var received = new TaskCompletionSource<ReceivedFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var subscription = provider.GetRequiredService<FrameDistributor>().Frames
			.Subscribe(frame => received.TrySetResult(frame));
		var window = ShowMainWindow(provider);
		var closed = new TaskCompletionSource();
		window.Closed += (_, _) => closed.TrySetResult();
		link.Connect(PortName);
		await port.WaitForPendingReadAsync().WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
		var frame = SpectrumFrame(Spectrometer.Atp8000, 1);

		port.PushBytes(FrameEncoder.Encode(frame));

		var first = await received.Task.WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
		first.Frame.Should().Be(frame);
		window.Close();
		await closed.Task.WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
	}

	[AvaloniaFact]
	public async Task ClosingTheWindowStopsTheDistributorAfterTheLink()
	{
		await using var port = new FakePortStream();
		_portProvider.QueueOpen(port);
		using var provider = BuildContainer();
		var link = provider.GetRequiredService<SerialLink>();
		var window = ShowMainWindow(provider);
		var closed = new TaskCompletionSource();
		window.Closed += (_, _) => closed.TrySetResult();
		link.Connect(PortName);
		await port.WaitForPendingReadAsync().WaitAsync(_waitLimit, TestContext.Current.CancellationToken);

		window.Close();

		await closed.Task.WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
		var templates = LoggedTemplates();
		var linkStopped = templates.IndexOf("The link on port {Port} stopped");
		var distributorStopped = templates.IndexOf(DistributorStopped);
		linkStopped.Should().BeGreaterThanOrEqualTo(0);
		distributorStopped.Should().BeGreaterThan(linkStopped);
	}

	[AvaloniaFact]
	public async Task OpeningTheWindowStartsTheStore()
	{
		using var provider = BuildContainer();
		var window = ShowMainWindow(provider);

		await PushOneFrameThenCloseAsync(provider, window, SpectrumFrame(Spectrometer.Atp8000, 7));

		StoredSpectrumSeqs(provider).Should().Be("7");
	}

	[AvaloniaFact]
	public async Task ClosingTheWindowStopsTheStoreAfterTheDistributor()
	{
		using var provider = BuildContainer();
		var window = ShowMainWindow(provider);
		var closed = new TaskCompletionSource();
		window.Closed += (_, _) => closed.TrySetResult();

		window.Close();

		await closed.Task.WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
		var templates = LoggedTemplates();
		var distributorStopped = templates.IndexOf(DistributorStopped);
		var storeStopped = templates.IndexOf(StoreStopped);
		distributorStopped.Should().BeGreaterThanOrEqualTo(0);
		storeStopped.Should().BeGreaterThan(distributorStopped);
	}

	[AvaloniaFact]
	public async Task AfterTheCloseTheStoreFileHasNoWal()
	{
		using var provider = BuildContainer();
		var window = ShowMainWindow(provider);

		await PushOneFrameThenCloseAsync(provider, window, SpectrumFrame(Spectrometer.Atp2000, 3));

		var storePath = provider.GetRequiredService<StoreFile>().Path;
		File.Exists(storePath).Should().BeTrue();
		var wal = new FileInfo(storePath + "-wal");
		(wal.Exists ? wal.Length : 0).Should().Be(0);
	}

	[AvaloniaFact]
	public async Task AfterTheCloseADistributorSubscriberReceivesNothingMore()
	{
		await using var firstPort = new FakePortStream();
		await using var secondPort = new FakePortStream();
		_portProvider.QueueOpen(firstPort);
		_portProvider.QueueOpen(secondPort);
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
		await firstPort.WaitForPendingReadAsync().WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
		firstPort.PushBytes(FrameEncoder.Encode(SpectrumFrame(Spectrometer.Atp8000, 1)));
		await firstReceived.Task.WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
		window.Close();
		await closed.Task.WaitAsync(_waitLimit, TestContext.Current.CancellationToken);

		// The link reconnects after the close and writes a frame; with the distributor stopped, the frame stays in the
		// channel, so reading it back proves the write happened and that no subscriber was handed it.
		link.Connect(PortName);
		await secondPort.WaitForPendingReadAsync().WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
		var lateFrame = SpectrumFrame(Spectrometer.Atp2000, 2);
		secondPort.PushBytes(FrameEncoder.Encode(lateFrame));

		await link.Frames.WaitToReadAsync(TestContext.Current.CancellationToken).AsTask()
			.WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
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

	private ServiceProvider BuildContainer()
	{
		var provider = new ServiceCollection()
			.AddUi(_logging, _paths)
			.Replace(ServiceDescriptor.Singleton<ISerialPortProvider>(_portProvider))
			.BuildServiceProvider();
		_stores.Add(provider.GetRequiredService<SpectrumStore>());

		return provider;
	}

	/// <summary>
	/// The distributor hands the frame to every subscriber before its stop completes, and the store drains its queue
	/// before Closed.
	/// </summary>
	private async Task PushOneFrameThenCloseAsync(IServiceProvider provider, Window window, BoardFrame frame)
	{
		await using var port = new FakePortStream();
		_portProvider.QueueOpen(port);
		var link = provider.GetRequiredService<SerialLink>();
		var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var subscription = provider.GetRequiredService<FrameDistributor>().Frames
			.Subscribe(_ => received.TrySetResult());
		var closed = new TaskCompletionSource();
		window.Closed += (_, _) => closed.TrySetResult();
		link.Connect(PortName);
		await port.WaitForPendingReadAsync().WaitAsync(_waitLimit, TestContext.Current.CancellationToken);

		port.PushBytes(FrameEncoder.Encode(frame));
		await received.Task.WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
		window.Close();

		await closed.Task.WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
	}

	private static string? StoredSpectrumSeqs(IServiceProvider provider)
	{
		var connectionString = new SqliteConnectionStringBuilder
		{
			DataSource = provider.GetRequiredService<StoreFile>().Path,
			Pooling = false
		}.ToString();
		using var connection = new SqliteConnection(connectionString);
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT group_concat(seq) FROM (SELECT seq FROM spectrum ORDER BY id)";
		return command.ExecuteScalar() as string;
	}

	private List<string> LoggedTemplates()
	{
		return [.. _sink.Events.Select(logEvent => logEvent.MessageTemplate.Text)];
	}

	private static Window ShowMainWindow(IServiceProvider provider)
	{
		var app = new App();
		app.Configure(provider);
		var window = app.CreateMainWindow();
		window.Show();
		Dispatcher.UIThread.RunJobs();

		return window;
	}

	public async ValueTask DisposeAsync()
	{
		// A writer still open would hold the instance file and fail the root's delete.
		foreach (var store in _stores)
		{
			await store.StopAsync().WaitAsync(_storeStopLimit);
		}

		_root.Dispose();
		_logging.Logger.Dispose();
	}
}
