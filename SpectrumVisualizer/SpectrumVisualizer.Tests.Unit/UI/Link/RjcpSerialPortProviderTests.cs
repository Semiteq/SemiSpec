using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;

using SpectrumVisualizer.UI.Link;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Link;

[Trait("Component", "UI")]
[Trait("Area", "Link")]
[Trait("Category", "Unit")]
public sealed class RjcpSerialPortProviderTests
{
	private const string PortName = "COM7";

	public static TheoryData<Exception, Type> OpenFailures => new()
	{
		{ new UnauthorizedAccessException($"Access Denied: {PortName}"), typeof(PortBusyError) },
		{ new IOException($"Sharing violation: {PortName}", 32), typeof(PortBusyError) },
		{ new IOException($"Port not found: {PortName}", 2), typeof(PortNotFoundError) },
		{ new IOException($"Unknown error 0x1F: {PortName}", 31), typeof(PortNotFoundError) },
		{ new PathTooLongException($"Path too long: {PortName}"), typeof(PortNotFoundError) },
		{ new ArgumentException("Must provide a valid name for a COM port"), typeof(PortNotFoundError) },
		{ new InvalidOperationException("Serial Port already opened"), typeof(PortNotFoundError) }
	};

	public static TheoryData<LinkError> LinkErrors =>
	[
		new PortBusyError(PortName),
		new PortNotFoundError(PortName),
		new PortLostError(PortName),
		new BoardNotRespondingError(PortName),
		new UnrecognisedDataError(PortName)
	];

	[Fact]
	public void SortPortNamesOrdersComPortsByNumberAndPutsOtherNamesAfterThem()
	{
		var sorted = RjcpSerialPortProvider.SortPortNames(["COM10", "ttyUSB0", "COM2", "CNCA0", "COM1"]);

		sorted.Should().Equal("COM1", "COM2", "COM10", "CNCA0", "ttyUSB0");
	}

	[Theory]
	[MemberData(nameof(OpenFailures))]
	public void MapOpenFailureTellsABusyPortFromAMissingOne(Exception exception, Type expected)
	{
		var error = RjcpSerialPortProvider.MapOpenFailure(PortName, exception);

		error.Should().BeOfType(expected);
		error.Message.Should().Contain(PortName);
	}

	[Fact]
	public void MapOpenFailureKeepsTheExceptionOfAFailureThatIsNotABusyPort()
	{
		var thrown = new InvalidOperationException("the driver refused the settings");

		var error = RjcpSerialPortProvider.MapOpenFailure(PortName, thrown);

		error.Should().BeOfType<PortNotFoundError>();
		error.Reasons.Should().ContainSingle().Which.Should().BeOfType<ExceptionalError>()
			.Which.Exception.Should().BeSameAs(thrown);
	}

	[Fact]
	public void OpenOfAPortTheMachineDoesNotListReturnsPortNotFound()
	{
		const string MissingPort = "COM-not-on-this-machine";

		var result = new RjcpSerialPortProvider(NullLogger<RjcpSerialPortProvider>.Instance).Open(MissingPort);

		result.IsFailed.Should().BeTrue();
		result.Errors.Should().ContainSingle().Which.Should().BeOfType<PortNotFoundError>()
			.Which.Message.Should().Contain(MissingPort);
	}

	[Theory]
	[MemberData(nameof(LinkErrors))]
	public void EveryLinkErrorNamesItsPortInTheMessage(LinkError error)
	{
		error.Message.Should().Contain(PortName);
	}
}
