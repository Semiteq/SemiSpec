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
	public void AWritableSiteFolderTakesTheLog()
	{
		var site = Path.Combine(_root, "site");
		var fallback = Path.Combine(_root, "fallback");

		var setup = AppLogging.Create(site, fallback, _ => true).Value;
		setup.Logger.Information("on-site");
		setup.Logger.Dispose();

		setup.Folder.Should().Be(site);
		File.ReadAllText(Path.Combine(site, AppLogging.LogFileName)).Should().Contain("on-site");
		Directory.Exists(fallback).Should().BeFalse();
	}

	[Fact]
	public void AnUnwritableSiteFolderFallsBackWithAWarningThatNamesBothFolders()
	{
		var site = Path.Combine(_root, "site");
		var fallback = Path.Combine(_root, "fallback");
		var checkedFolders = new List<string>();

		var setup = AppLogging.Create(
			site,
			fallback,
			folder =>
			{
				checkedFolders.Add(folder);
				return false;
			}).Value;
		setup.Logger.Dispose();

		checkedFolders.Should().Equal(site);
		setup.Folder.Should().Be(fallback);
		var line = File.ReadAllLines(Path.Combine(fallback, AppLogging.LogFileName)).Single();
		line.Should().Contain("[WRN]").And.Contain(site).And.Contain(fallback);
	}

	[Fact]
	public void TheWriteCheckPassesForACreatableFolderAndKeepsTheFileOpenToTheSink()
	{
		var folder = Path.Combine(_root, "nested", "Logs");
		var setup = AppLogging.Create(folder).Value;
		setup.Logger.Information("held-open");

		var canWrite = AppLogging.CanWrite(folder);
		setup.Logger.Dispose();

		canWrite.Should().BeTrue("the check shares the file the way the running sink does");
	}

	[Fact]
	public void TheWriteCheckFailsWhereAFileBlocksTheFolder()
	{
		Directory.CreateDirectory(_root);
		var fileInTheWay = Path.Combine(_root, "occupied");
		File.WriteAllText(fileInTheWay, string.Empty);

		AppLogging.CanWrite(fileInTheWay).Should().BeFalse();
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
