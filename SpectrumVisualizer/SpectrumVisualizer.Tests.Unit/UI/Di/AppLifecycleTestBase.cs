using Avalonia.Controls;
using Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Serilog;

using SpectrumVisualizer.Core;
using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Tests.Unit.UI.Link;
using SpectrumVisualizer.Tests.Unit.UI.Logging;
using SpectrumVisualizer.UI;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Logging;
using SpectrumVisualizer.UI.Store;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Di;

/// <summary>The production container with a fake port provider, so the window's close meets a real link.</summary>
public abstract class AppLifecycleTestBase : IAsyncDisposable
{
	protected const string PortName = "COM3";
	protected const string DistributorStopped = "The frame distributor stopped";

	private static readonly TimeSpan _storeStopLimit = TimeSpan.FromSeconds(10);

	private readonly CollectingSink _sink = new();
	private readonly LoggingSetup _logging;
	// Every test in the process gets the same instance file name, so each test owns its root.
	private readonly TempFolder _root = new();
	private readonly AppDataPaths _paths;
	private readonly List<SpectrumStore> _stores = [];

	protected AppLifecycleTestBase()
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

	protected static TimeSpan WaitLimit { get; } = TimeSpan.FromSeconds(5);

	private protected FakeSerialPortProvider PortProvider { get; } = new(PortName);

	public async ValueTask DisposeAsync()
	{
		// A writer still open would hold the instance file and fail the root's delete.
		foreach (var store in _stores)
		{
			await store.StopAsync().WaitAsync(_storeStopLimit);
		}

		_root.Dispose();
		_logging.Logger.Dispose();
		GC.SuppressFinalize(this);
	}

	/// <summary>The production container with the fake port provider; replace swaps further registrations.</summary>
	protected ServiceProvider BuildContainer(Action<IServiceCollection>? replace = null)
	{
		var services = new ServiceCollection()
			.AddUi(_logging, _paths)
			.Replace(ServiceDescriptor.Singleton<ISerialPortProvider>(PortProvider));
		replace?.Invoke(services);
		var provider = services.BuildServiceProvider();
		_stores.Add(provider.GetRequiredService<SpectrumStore>());

		return provider;
	}

	protected List<string> LoggedTemplates()
	{
		return [.. _sink.Events.Select(logEvent => logEvent.MessageTemplate.Text)];
	}

	/// <summary>
	/// The distributor hands the frame to every subscriber before its stop completes, and the store drains its queue
	/// before Closed.
	/// </summary>
	protected async Task PushOneFrameThenCloseAsync(IServiceProvider provider, Window window, BoardFrame frame)
	{
		await using var port = new FakePortStream();
		PortProvider.QueueOpen(port);
		var link = provider.GetRequiredService<SerialLink>();
		var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var subscription = provider.GetRequiredService<FrameDistributor>().Frames
			.Subscribe(_ => received.TrySetResult());
		var closed = new TaskCompletionSource();
		window.Closed += (_, _) => closed.TrySetResult();
		link.Connect(PortName);
		await port.WaitForPendingReadAsync().WaitAsync(WaitLimit, TestContext.Current.CancellationToken);

		port.PushBytes(FrameEncoder.Encode(frame));
		await received.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
		window.Close();

		await closed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
	}

	protected static Window ShowMainWindow(IServiceProvider provider)
	{
		var app = new App();
		app.Configure(provider);
		var window = app.CreateMainWindow();
		window.Show();
		Dispatcher.UIThread.RunJobs();

		return window;
	}
}
