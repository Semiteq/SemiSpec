using AwesomeAssertions;

using SemiSpec.Core.Protocol;
using SemiSpec.Tools.StoreBenchmark;

using Xunit;

namespace SemiSpec.Tests.Unit.Tools;

[Trait("Component", "Tools")]
[Trait("Area", "Store")]
[Trait("Category", "Unit")]
public sealed class StoreBenchmarkCommandTests
{
	[Fact]
	public async Task NoOptionYieldsTheDefaults()
	{
		var options = await ParseAsync();

		options.Should().Be(new BenchmarkOptions(172_800, Spectrometer.Atp2000, 24, null, false));
		options.WavelengthNm.Should().Be(650);
	}

	[Fact]
	public async Task Device8000UsesAWavelengthInsideItsRange()
	{
		var options = await ParseAsync("--device", "8000");

		options.Device.Should().Be(Spectrometer.Atp8000);
		options.WavelengthNm.Should().Be(1550);
	}

	[Fact]
	public async Task EveryOptionGivenIsParsed()
	{
		var options = await ParseAsync(
			"--rows", "1200", "--churn-rounds", "0", "--folder", "out", "--keep");

		options.Should().Be(new BenchmarkOptions(1200, Spectrometer.Atp2000, 0, "out", true));
	}

	[Theory]
	[InlineData("--rows", "10")]
	[InlineData("--rows", "1199")]
	[InlineData("--churn-rounds", "25")]
	[InlineData("--churn-rounds", "-1")]
	[InlineData("--device", "4000")]
	public void AnOutOfRangeValueIsAParseError(string option, string value)
	{
		var parsed = BenchmarkCommand.Build((_, _) => Task.FromResult(0)).Parse([option, value]);

		parsed.Errors.Should().NotBeEmpty();
	}

	private static async Task<BenchmarkOptions> ParseAsync(params string[] arguments)
	{
		BenchmarkOptions? captured = null;
		var parsed = BenchmarkCommand.Build((options, _) =>
		{
			captured = options;
			return Task.FromResult(0);
		}).Parse(arguments);

		parsed.Errors.Should().BeEmpty();
		(await parsed.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken)).Should().Be(0);
		return captured!;
	}
}
