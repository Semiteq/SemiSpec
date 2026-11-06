using AwesomeAssertions;

using SemiSpec.UI.Logging;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Logging;

[Trait("Component", "UI")]
[Trait("Area", "Logging")]
[Trait("Category", "Unit")]
public sealed class AppLoggingTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), $"semispec-tests-{Guid.NewGuid():N}");

	public void Dispose()
	{
		if (Directory.Exists(_root))
		{
			Directory.Delete(_root, recursive: true);
		}
	}

	[Fact]
	public void AMissingLogFolderIsCreated()
	{
		var logFolder = Path.Combine(_root, "nested", "Logs");

		var setup = AppLogging.Create(logFolder);

		setup.IsSuccess.Should().BeTrue();
		setup.Value.Logger.Dispose();
		Directory.Exists(logFolder).Should().BeTrue();
	}

	[Fact]
	public void TheLogFileReceivesDebugLinesOnlyWhileVerbose()
	{
		var setup = AppLogging.Create(_root).Value;

		setup.Logger.Debug("before-verbose");
		setup.LevelControl.IsVerbose = true;
		setup.Logger.Debug("while-verbose");
		setup.Logger.Dispose();

		var text = File.ReadAllText(Path.Combine(_root, AppLogging.LogFileName));
		text.Should().Contain("while-verbose").And.NotContain("before-verbose");
	}

	[Fact]
	public void ALogFolderThatCannotBeCreatedFails()
	{
		Directory.CreateDirectory(_root);
		var fileInTheWay = Path.Combine(_root, "occupied");
		File.WriteAllText(fileInTheWay, string.Empty);

		var setup = AppLogging.Create(fileInTheWay);

		setup.IsFailed.Should().BeTrue();
		setup.Errors[0].Message.Should().Contain($"'{fileInTheWay}'");
	}
}
