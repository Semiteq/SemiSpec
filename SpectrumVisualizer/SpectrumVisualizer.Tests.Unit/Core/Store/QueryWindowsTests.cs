using System.Runtime.InteropServices;

using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Store;

[Trait("Component", "Core")]
[Trait("Area", "Store")]
[Trait("Category", "Unit")]
public sealed class QueryWindowsTests
{
	[Theory]
	[InlineData(Spectrometer.Atp2000, 189, true)]
	[InlineData(Spectrometer.Atp2000, 1028, true)]
	[InlineData(Spectrometer.Atp2000, 188, false)]
	[InlineData(Spectrometer.Atp2000, 1029, false)]
	[InlineData(Spectrometer.Atp8000, 898, true)]
	[InlineData(Spectrometer.Atp8000, 2461, true)]
	[InlineData(Spectrometer.Atp8000, 897, false)]
	[InlineData(Spectrometer.Atp8000, 2462, false)]
	public void CoversFollowsTheFirmwareTableRange(Spectrometer device, ushort nm, bool expected)
	{
		new QueryWindows(nm, 5).Covers(device).Should().Be(expected);
	}

	[Fact]
	public void MeanOfAveragesTheWindowPixels()
	{
		ushort[] pixels = [100, 1, 2, 3, 6, 100];

		QueryWindows.MeanOf(MemoryMarshal.AsBytes(pixels.AsSpan(1, 4))).Should().Be(3.0);
	}

	[Fact]
	public void ADarkWindowHasMeanZero()
	{
		QueryWindows.MeanOf(new byte[sizeof(ushort) * 21]).Should().Be(0.0);
	}

	[Fact]
	public void ADeltaOfZeroTakesOnePixel()
	{
		var window = new QueryWindows(650, 0).For(Spectrometer.Atp2000);

		window.First.Should().Be(window.Last);
		QueryWindows.ByteCount(window).Should().Be(sizeof(ushort));
	}
}
