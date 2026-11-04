using System.Reactive.Linq;
using System.Windows.Input;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using ReactiveUI.Avalonia;

using SpectrumVisualizer.Tests.Unit.UI.Link;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.MainWindow;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.MainWindow;

[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class ConnectionBarViewModelTests : IDisposable
{
	private const string PortName = "COM3";

	private static readonly TimeSpan _waitLimit = TimeSpan.FromSeconds(5);

	private readonly FakeSerialPortProvider _portProvider = new(PortName, "COM5");
	private readonly FakePortStream _port = new();
	private readonly SerialLink _serialLink;
	private readonly ConnectionBarViewModel _viewModel;

	public ConnectionBarViewModelTests()
	{
		_serialLink = new SerialLink(_portProvider, new TestScheduler(), NullLogger<SerialLink>.Instance);
		_viewModel = new ConnectionBarViewModel(_portProvider, _serialLink, AvaloniaScheduler.Instance);
	}

	[AvaloniaFact]
	public void RefreshFillsTheListAndKeepsAStillPresentSelection()
	{
		_viewModel.RefreshPorts();
		_viewModel.Ports.Should().Equal(PortName, "COM5");
		_viewModel.SelectedPort = "COM5";

		_portProvider.SetPorts("COM5", "COM7");
		_viewModel.RefreshCommand.Execute().Subscribe();

		_viewModel.Ports.Should().Equal("COM5", "COM7");
		_viewModel.SelectedPort.Should().Be("COM5");
	}

	[AvaloniaFact]
	public void RefreshClearsASelectionWhosePortIsGone()
	{
		_viewModel.RefreshPorts();
		_viewModel.SelectedPort = PortName;

		_portProvider.SetPorts("COM5");
		_viewModel.RefreshPorts();

		_viewModel.SelectedPort.Should().BeNull();
	}

	[AvaloniaFact]
	public void ThePlaceholderShowsOnlyWhileNoPortIsListed()
	{
		_viewModel.PortPlaceholder.Should().Be(Resources.ConnectionPortPlaceholder);

		_viewModel.RefreshPorts();
		_viewModel.PortPlaceholder.Should().BeEmpty();

		_portProvider.SetPorts();
		_viewModel.RefreshPorts();
		_viewModel.PortPlaceholder.Should().Be(Resources.ConnectionPortPlaceholder);
	}

	[AvaloniaFact]
	public void ConnectNeedsASelectedPort()
	{
		_viewModel.RefreshPorts();
		CanExecute(_viewModel.ConnectCommand).Should().BeFalse();

		_viewModel.SelectedPort = PortName;

		CanExecute(_viewModel.ConnectCommand).Should().BeTrue();
	}

	[AvaloniaFact]
	public void AnIdleLinkDisablesDisconnect()
	{
		_viewModel.IsIdle.Should().BeTrue();
		CanExecute(_viewModel.DisconnectCommand).Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task AnActiveLinkDisablesConnectAndEnablesDisconnect()
	{
		await ConnectThroughTheBarAsync();

		CanExecute(_viewModel.ConnectCommand).Should().BeFalse();
		CanExecute(_viewModel.DisconnectCommand).Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task ALinkRetryingAnUnavailablePortEnablesDisconnect()
	{
		_portProvider.QueueOpenFailure(new PortBusyError(PortName));
		_viewModel.SelectedPort = PortName;

		await _viewModel.ConnectCommand.Execute();
		await WaitUntilAsync(() => _serialLink.Status.State == LinkState.PortUnavailable && !_viewModel.IsIdle);

		CanExecute(_viewModel.ConnectCommand).Should().BeFalse();
		CanExecute(_viewModel.DisconnectCommand).Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task ConnectAndDisconnectReachTheLink()
	{
		await ConnectThroughTheBarAsync();
		_portProvider.OpenCount.Should().Be(1);
		_serialLink.Status.PortName.Should().Be(PortName);

		await _viewModel.DisconnectCommand.Execute();
		await WaitUntilAsync(() => _viewModel.IsIdle);

		_serialLink.Status.State.Should().Be(LinkState.Disconnected);
		_port.IsDisposed.Should().BeTrue();
		CanExecute(_viewModel.ConnectCommand).Should().BeTrue();
		CanExecute(_viewModel.DisconnectCommand).Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task TheInitialIsIdleComesFromTheLinkStatus()
	{
		_portProvider.QueueOpen(_port);
		_serialLink.Connect(PortName);
		await _port.WaitForPendingReadAsync().WaitAsync(_waitLimit, TestContext.Current.CancellationToken);

		using var lateViewModel = new ConnectionBarViewModel(_portProvider, _serialLink, AvaloniaScheduler.Instance);

		lateViewModel.IsIdle.Should().BeFalse("the link was already active when the view model was built");
	}

	public void Dispose()
	{
		_viewModel.Dispose();
		_serialLink.Dispose();
		_port.Dispose();
	}

	private static bool CanExecute(ICommand command)
	{
		return command.CanExecute(null);
	}

	/// <summary>The link raises its status off the UI thread, so the wait pumps the dispatcher for the hop back.</summary>
	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		using var limit = new CancellationTokenSource(_waitLimit);
		while (!condition())
		{
			Dispatcher.UIThread.RunJobs();
			await Task.Delay(10, limit.Token);
		}
	}

	private async Task ConnectThroughTheBarAsync()
	{
		_portProvider.QueueOpen(_port);
		_viewModel.RefreshPorts();
		_viewModel.SelectedPort = PortName;

		await _viewModel.ConnectCommand.Execute();
		await _port.WaitForPendingReadAsync().WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
		await WaitUntilAsync(() => !_viewModel.IsIdle);
	}
}
