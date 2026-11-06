using System.Reactive.Linq;
using System.Windows.Input;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using ReactiveUI.Avalonia;

using SpectrumVisualizer.Tests.Unit.UI.Link;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.MainWindow;
using SpectrumVisualizer.UI.Messages;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.MainWindow;

[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class ConnectionBarViewModelTests : IDisposable
{
	private const string PortName = "COM3";

	private readonly FakeSerialPortProvider _portProvider = new(PortName, "COM5");
	private readonly FakePortStream _port = new();
	private readonly MessagePanelViewModel _messagePanel = new(TimeProvider.System);
	private readonly SerialLink _serialLink;
	private readonly ConnectionBarViewModel _viewModel;

	public ConnectionBarViewModelTests()
	{
		_serialLink = new SerialLink(_portProvider, new TestScheduler(), NullLogger<SerialLink>.Instance);
		_viewModel = NewViewModel();
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
	public void ASelectionKeepsItsPortWhenTheListSpellsTheNameInAnotherCase()
	{
		_viewModel.RefreshPorts();
		_viewModel.SelectedPort = "COM5";

		_portProvider.SetPorts("com5");
		_viewModel.RefreshPorts();

		_viewModel.SelectedPort.Should().Be("COM5");
	}

	[AvaloniaFact]
	public void AFailedListingShowsNoPortAndClearsTheSelection()
	{
		_viewModel.RefreshPorts();
		_viewModel.SelectedPort = PortName;

		_portProvider.FailPortNames(new Error("the port registry key is gone"));
		_viewModel.RefreshPorts();

		_viewModel.Ports.Should().BeEmpty();
		_viewModel.SelectedPort.Should().BeNull();
		_viewModel.PortPlaceholder.Should().Be(Resources.ConnectionPortPlaceholder);
		_messagePanel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ErrorMessageMapper.Map(new PortListError()));
	}

	[AvaloniaFact]
	public void AListingThatWorksAddsNoPanelEntry()
	{
		_viewModel.RefreshPorts();

		_messagePanel.Entries.Should().BeEmpty();
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
		await Waits.UntilAsync(
			() => _serialLink.Status.State == LinkState.PortUnavailable && !_viewModel.IsIdle,
			pumpDispatcher: true);

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
		await Waits.UntilAsync(() => _viewModel.IsIdle, pumpDispatcher: true);

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
		await _port.WaitForPendingReadAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);

		using var lateViewModel = NewViewModel();

		lateViewModel.IsIdle.Should().BeFalse("the link was already active when the view model was built");
	}

	public void Dispose()
	{
		_viewModel.Dispose();
		_serialLink.Dispose();
		_port.Dispose();
		_messagePanel.Dispose();
	}

	private ConnectionBarViewModel NewViewModel()
	{
		return new ConnectionBarViewModel(
			_portProvider,
			_serialLink,
			_messagePanel,
			AvaloniaScheduler.Instance,
			NullLogger<ConnectionBarViewModel>.Instance);
	}

	private static bool CanExecute(ICommand command)
	{
		return command.CanExecute(null);
	}

	private async Task ConnectThroughTheBarAsync()
	{
		_portProvider.QueueOpen(_port);
		_viewModel.RefreshPorts();
		_viewModel.SelectedPort = PortName;

		await _viewModel.ConnectCommand.Execute();
		await _port.WaitForPendingReadAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);
		await Waits.UntilAsync(() => !_viewModel.IsIdle, pumpDispatcher: true);
	}
}
