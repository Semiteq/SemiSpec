namespace SpectrumVisualizer.Core.Protocol;

/// <summary>CRC-16/MODBUS: init 0xFFFF, reflected polynomial 0xA001, no final XOR.</summary>
public static class Crc16Modbus
{
	private const ushort Initial = 0xFFFF;
	private const ushort ReflectedPolynomial = 0xA001;

	public static ushort Compute(ReadOnlySpan<byte> data)
	{
		var crc = Initial;
		foreach (var value in data)
		{
			crc = Update(crc, value);
		}

		return crc;
	}

	private static ushort Update(ushort crc, byte value)
	{
		crc ^= value;
		for (var bit = 0; bit < 8; bit++)
		{
			var lowBitSet = (crc & 1) != 0;
			crc >>= 1;
			if (lowBitSet)
			{
				crc ^= ReflectedPolynomial;
			}
		}

		return crc;
	}
}
