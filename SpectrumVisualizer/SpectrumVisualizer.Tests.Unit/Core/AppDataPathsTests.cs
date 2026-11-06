using AwesomeAssertions;

using SpectrumVisualizer.Core;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core;

[Trait("Component", "Core")]
[Trait("Area", "Paths")]
[Trait("Category", "Unit")]
public sealed class AppDataPathsTests
{
	private static readonly string _root = Path.Combine(Path.GetTempPath(), "local-app-data");

	[Fact]
	public void TheLogFolderSitsInsideTheAppFolder()
	{
		var paths = AppDataPaths.Under(_root).Value;

		paths.LogFolder.Should().Be(Path.Combine(_root, "SpectrumVisualizer", "Logs"));
	}

	[Fact]
	public void TheStoreFolderSitsInsideTheAppFolder()
	{
		var paths = AppDataPaths.Under(_root).Value;

		paths.StoreFolder.Should().Be(Path.Combine(_root, "SpectrumVisualizer", "Spectra"));
	}

	[Fact]
	public void ATrailingSeparatorOnTheRootDoesNotChangeTheFolders()
	{
		var paths = AppDataPaths.Under(_root + Path.DirectorySeparatorChar).Value;

		paths.Should().Be(AppDataPaths.Under(_root).Value);
	}

	[Theory]
	[InlineData("")]
	[InlineData("relative")]
	[InlineData("relative/nested")]
	[InlineData(@"\rooted")]
	[InlineData("C:drive-relative")]
	public void ARootThatIsNotAbsoluteFails(string root)
	{
		var result = AppDataPaths.Under(root);

		result.IsFailed.Should().BeTrue();
		result.Errors[0].Message.Should().Contain($"'{root}'");
	}
}
