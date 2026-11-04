using System.Text;

using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Protocol;

[Trait("Component", "Core")]
[Trait("Area", "Protocol")]
[Trait("Category", "Unit")]
public sealed class Crc16ModbusTests
{
	[Fact]
	public void TheStandardCheckInputGivesTheStandardCheckValue()
	{
		var crc = Crc16Modbus.Compute(Encoding.ASCII.GetBytes("123456789"));

		crc.Should().Be(0x4B37);
	}

	[Fact]
	public void TheHeartbeatHeaderGivesTheCrcTheBoardSends()
	{
		var crc = Crc16Modbus.Compute([0x02, 0x00, 0x03, 0x2A]);

		crc.Should().Be(0xB380);
	}
}
