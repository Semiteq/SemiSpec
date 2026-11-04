using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Serilog;

using SpectrumVisualizer.Tests.Unit.UI.Link;
using SpectrumVisualizer.Tests.Unit.UI.Logging;

using SpectrumVisualizer.UI;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Logging;
using SpectrumVisualizer.UI.MainWindow;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Di;

/// <summary>The production container with a fake port provider, so the window's close meets a real link.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class AppLifecycleTests : IDisposable
{
	private const string PortName = "COM3";

	private static readonly TimeSpan _waitLimit = TimeSpan.FromSeconds(5);

	private readonly FakeSerialPortProvider _portProvider = new(PortName);
	private readonly LoggingSetup _logging;

	public AppLifecycleTests()
	{
		var logLevelControl = new LogLevelControl();
		var logger = new LoggerConfiguration()
			.MinimumLevel.ControlledBy(logLevelControl.Switch)
			.WriteTo.Sink(new CollectingSink())
			.CreateLogger();
		_logging = new LoggingSetup(logger, logLevelControl);
	}

	[AvaloniaFact]
	public async Task ClosingTheWindowWaitsUntilTheLinkReportsDisconnected()
	{
		await using var port = new FakePortStream();
		_portProvider.QueueOpen(port);
		using var provider = BuildContainer();
		var link = provider.GetRequiredService<SerialLink>();
		var window = ShowMainWindow(provider);
		link.Connect(PortName);
		await port.WaitForPendingReadAsync().WaitAsync(_waitLimit, TestContext.Current.CancellationToken);

		// Subscribed after the app's handler, so each entry is the decision the app made for that close.
		var closeCancelled = new List<bool>();
		window.Closing += (_, e) => closeCancelled.Add(e.Cancel);
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
	public void WithAnIdleLinkTheWindowClosesAtOnce()
	{
		using var provider = BuildContainer();
		var window = ShowMainWindow(provider);
		var closed = false;
		window.Closed += (_, _) => closed = true;

		window.Close();

		closed.Should().BeTrue();
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
		return new ServiceCollection()
			.AddUi(_logging)
			.Replace(ServiceDescriptor.Singleton<ISerialPortProvider>(_portProvider))
			.BuildServiceProvider();
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

	public void Dispose()
	{
		_logging.Logger.Dispose();
	}
}
