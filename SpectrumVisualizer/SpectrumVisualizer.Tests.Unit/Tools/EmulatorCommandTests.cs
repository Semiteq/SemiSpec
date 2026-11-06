using System.CommandLine;

using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Tools.BoardEmulator;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Tools;

[Trait("Component", "Tools")]
[Trait("Area", "Emulator")]
[Trait("Category", "Unit")]
public sealed class EmulatorCommandTests
{
	[Fact]
	public async Task OnlyThePortGivenYieldsTheDefaults()
	{
		var options = await ParseAsync("--port", "COM5");

		options.Should().Be(new EmulatorOptions("COM5", Spectrometer.Atp8000, 1.0, 1550, 10, 0, 0, false, null));
	}

	[Theory]
	[InlineData("2000", Spectrometer.Atp2000, 650)]
	[InlineData("8000", Spectrometer.Atp8000, 1550)]
	public async Task TheDefaultWavelengthFollowsTheDevice(string device, Spectrometer expected, ushort wavelengthNm)
	{
		var options = await ParseAsync("--port", "COM5", "--device", device);

		options.Device.Should().Be(expected);
		options.WavelengthNm.Should().Be(wavelengthNm);
	}

	[Fact]
	public async Task EveryOptionGivenIsParsed()
	{
		var options = await ParseAsync(
			"--port", "COM7", "--device", "2000", "--rate", "0.001", "--wavelength", "700", "--delta", "100",
			"--bad-crc-every", "4", "--dark-every", "3", "--both-connected", "--failed", "8000");

		options.Should().Be(
			new EmulatorOptions("COM7", Spectrometer.Atp2000, 0.001, 700, 100, 4, 3, true, Spectrometer.Atp8000));
	}

	[Theory]
	[InlineData("8000", "8000")]
	[InlineData("2000", "2000")]
	public async Task TheFailedBitOfTheEmulatedSpectrometerNeedsNoOtherOption(string device, string failed)
	{
		var options = await ParseAsync("--port", "COM5", "--device", device, "--failed", failed);

		options.Failed.Should().Be(options.Device);
		options.BothConnected.Should().BeFalse();
	}

	[Fact]
	public async Task TheOtherSpectrometersFailedBitWithoutBothConnectedPrintsItsErrorAndExitsOne()
	{
		var error = new StringWriter();
		var parsed = Parse("--port", "COM5", "--failed", "2000", "--device", "8000");

		var exitCode = await parsed.InvokeAsync(
			new InvocationConfiguration { Error = error, Output = TextWriter.Null },
			TestContext.Current.CancellationToken);

		exitCode.Should().Be(1);
		error.ToString().Should().Contain("--failed").And.Contain("--both-connected");
	}

	[Fact]
	public async Task RateZeroIsAccepted()
	{
		var options = await ParseAsync("--port", "COM5", "--rate", "0");

		options.Rate.Should().Be(0);
	}

	[Theory]
	[InlineData("189", 189)]
	[InlineData("2460", 2460)]
	public async Task TheWavelengthRangeEndsAreAccepted(string wavelength, ushort expectedNm)
	{
		var options = await ParseAsync("--port", "COM5", "--wavelength", wavelength);

		options.WavelengthNm.Should().Be(expectedNm);
	}

	[Theory]
	[InlineData("--rate", "-1")]
	[InlineData("--rate", "NaN")]
	[InlineData("--rate", "Infinity")]
	[InlineData("--rate", "0.0009")]
	[InlineData("--rate", "1e-300")]
	[InlineData("--rate", "fast")]
	[InlineData("--delta", "-1")]
	[InlineData("--delta", "101")]
	[InlineData("--delta", "256")]
	[InlineData("--delta", "1.5")]
	[InlineData("--bad-crc-every", "-1")]
	[InlineData("--bad-crc-every", "x")]
	[InlineData("--wavelength", "188")]
	[InlineData("--wavelength", "2461")]
	[InlineData("--wavelength", "70000")]
	[InlineData("--wavelength", "-1")]
	[InlineData("--device", "4000")]
	[InlineData("--dark-every", "-1")]
	[InlineData("--failed", "4000")]
	public void AnOutOfRangeValueIsAParseError(string option, string value)
	{
		var parsed = Parse("--port", "COM5", option, value);

		parsed.Errors.Should().NotBeEmpty();
	}

	[Fact]
	public void AnInvalidDeviceBesideFailedIsAParseError()
	{
		var parsed = Parse("--port", "COM5", "--device", "4000", "--failed", "2000");

		parsed.Errors.Should().NotBeEmpty();
	}

	[Fact]
	public void AMissingPortIsAParseError()
	{
		var parsed = Parse();

		parsed.Errors.Should().NotBeEmpty();
	}

	private static ParseResult Parse(params string[] arguments)
	{
		return EmulatorCommand.Build((_, _) => Task.FromResult(0)).Parse(arguments);
	}

	private static async Task<EmulatorOptions> ParseAsync(params string[] arguments)
	{
		EmulatorOptions? captured = null;
		var parsed = EmulatorCommand.Build((options, _) =>
		{
			captured = options;
			return Task.FromResult(0);
		}).Parse(arguments);

		parsed.Errors.Should().BeEmpty();
		(await parsed.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
		return captured!;
	}
}
