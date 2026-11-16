using AwesomeAssertions;

using SemiSpec.UI.Settings;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Settings;

[Trait("Component", "UI")]
[Trait("Area", "Settings")]
[Trait("Category", "Unit")]
public sealed class SettingsFileTests : IDisposable
{
	private readonly TempFolder _root = new();

	public void Dispose()
	{
		_root.Dispose();
	}

	[Fact]
	public void AMissingFileLoadsTheDefaults()
	{
		var loaded = new SettingsFile(_root.FileIn(SettingsFile.FileName)).Load();

		loaded.Value.Should().Be(DisplaySettings.Default);
		DisplaySettings.Default.ShowQFactor.Should().BeFalse();
		DisplaySettings.Default.TrendAsLines.Should().BeFalse();
	}

	[Fact]
	public void AnEmptyFileLoadsTheDefaults()
	{
		var path = _root.FileIn(SettingsFile.FileName);
		File.WriteAllText(path, string.Empty);

		new SettingsFile(path).Load().Value.Should().Be(DisplaySettings.Default);
	}

	[Fact]
	public void ASavedFileLoadsBackTheSameChoices()
	{
		var file = new SettingsFile(Path.Combine(_root.Root, "nested", SettingsFile.FileName));
		var choices = new DisplaySettings(ShowQFactor: true, TrendAsLines: true);

		file.Save(choices).IsSuccess.Should().BeTrue();

		file.Load().Value.Should().Be(choices);
		File.ReadAllText(file.FullPath).Should().Contain("show_q_factor: true").And.Contain("trend_as_lines: true");
	}

	[Fact]
	public void UnknownKeysAreIgnored()
	{
		var path = _root.FileIn(SettingsFile.FileName);
		File.WriteAllText(path, "show_q_factor: true\nnewer_choice: 5\n");

		new SettingsFile(path).Load().Value.Should().Be(new DisplaySettings(ShowQFactor: true, TrendAsLines: false));
	}

	[Theory]
	[InlineData("show_q_factor: [")]
	[InlineData("show_q_factor: maybe")]
	public void AMalformedFileFailsWithAReadError(string content)
	{
		var path = _root.FileIn(SettingsFile.FileName);
		File.WriteAllText(path, content);

		var loaded = new SettingsFile(path).Load();

		loaded.IsFailed.Should().BeTrue();
		loaded.Errors[0].Should().BeOfType<SettingsReadError>().Which.Message.Should().Contain(path);
	}

	[Fact]
	public void AFolderThatIsAFileFailsTheSaveWithAWriteError()
	{
		var blocker = _root.FileIn("blocker");
		File.WriteAllText(blocker, string.Empty);
		var file = new SettingsFile(Path.Combine(blocker, SettingsFile.FileName));

		var saved = file.Save(DisplaySettings.Default);

		saved.IsFailed.Should().BeTrue();
		saved.Errors[0].Should().BeOfType<SettingsWriteError>();
	}

	[Fact]
	public void AWritableSiteFolderHoldsTheFile()
	{
		var site = Path.Combine(_root.Root, "site");
		var fallback = Path.Combine(_root.Root, "fallback");

		var file = SettingsFile.Choose(site, fallback, _ => true);

		file.FullPath.Should().Be(Path.Combine(site, SettingsFile.FileName));
		file.InFallbackFolder.Should().BeFalse();
	}

	[Fact]
	public void AnUnwritableSiteFolderMovesTheFileToTheFallbackFolder()
	{
		var site = Path.Combine(_root.Root, "site");
		var fallback = Path.Combine(_root.Root, "fallback");

		var file = SettingsFile.Choose(site, fallback, _ => false);

		file.FullPath.Should().Be(Path.Combine(fallback, SettingsFile.FileName));
		file.InFallbackFolder.Should().BeTrue();
	}

	[Fact]
	public void TheWriteCheckCreatesTheFolderAndLeavesNoProbe()
	{
		var folder = Path.Combine(_root.Root, "new");

		SettingsFile.CanWrite(folder).Should().BeTrue();

		Directory.EnumerateFileSystemEntries(folder).Should().BeEmpty();
	}

	[Fact]
	public void TheWriteCheckFailsUnderAFile()
	{
		var blocker = _root.FileIn("blocker");
		File.WriteAllText(blocker, string.Empty);

		SettingsFile.CanWrite(Path.Combine(blocker, "sub")).Should().BeFalse();
	}
}
