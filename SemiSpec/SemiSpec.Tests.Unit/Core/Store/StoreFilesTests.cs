using AwesomeAssertions;

using Microsoft.Data.Sqlite;

using SemiSpec.Core.Store;

using Xunit;

namespace SemiSpec.Tests.Unit.Core.Store;

[Trait("Component", "Core")]
[Trait("Area", "Store")]
[Trait("Category", "Unit")]
public sealed class StoreFilesTests : IDisposable
{
	private readonly TempFolder _folder = new();

	public void Dispose()
	{
		_folder.Dispose();
	}

	[Fact]
	public void TheInstanceFileIsNamedForTheProcessId()
	{
		StoreFiles.InstanceFile(_folder.Root, 1234).Should().Be(_folder.FileIn("spectra-1234.db"));
	}

	[Fact]
	public void PrepareWipesTheOwnFileAndItsSidecars()
	{
		var own = StoreFiles.InstanceFile(_folder.Root, 1234);
		Touch(own, own + "-wal", own + "-shm");

		var result = StoreFiles.Prepare(own);

		result.IsSuccess.Should().BeTrue();
		Directory.GetFiles(_folder.Root).Should().BeEmpty();
	}

	[Fact]
	public void PrepareDeletesTheFileOfADeadInstance()
	{
		var dead = StoreFiles.InstanceFile(_folder.Root, 77);
		Touch(dead, dead + "-wal", dead + "-shm");

		var result = StoreFiles.Prepare(StoreFiles.InstanceFile(_folder.Root, 1234));

		result.Value.Should().Be(new StoreSweep(DeletedFiles: 1, KeptFiles: 0));
		Directory.GetFiles(_folder.Root).Should().BeEmpty();
	}

	[Fact]
	public void PrepareKeepsTheFileOfALiveInstance()
	{
		var live = StoreFiles.InstanceFile(_folder.Root, 77);
		using var connection = new SqliteConnection($"Data Source={live};Pooling=False");
		connection.Open();
		Execute(connection, "PRAGMA journal_mode=WAL; CREATE TABLE t (x INTEGER); INSERT INTO t VALUES (1);");

		var result = StoreFiles.Prepare(StoreFiles.InstanceFile(_folder.Root, 1234));

		result.Value.Should().Be(new StoreSweep(DeletedFiles: 0, KeptFiles: 1));
		File.Exists(live).Should().BeTrue();
		File.Exists(live + "-wal").Should().BeTrue();
	}

	[Fact]
	public void PrepareDeletesOrphanSidecars()
	{
		var orphan = StoreFiles.InstanceFile(_folder.Root, 77);
		Touch(orphan + "-wal", orphan + "-shm");

		var result = StoreFiles.Prepare(StoreFiles.InstanceFile(_folder.Root, 1234));

		result.IsSuccess.Should().BeTrue();
		Directory.GetFiles(_folder.Root).Should().BeEmpty();
	}

	[Fact]
	public void PrepareKeepsFilesOutsideTheStoreNaming()
	{
		var foreign = _folder.FileIn("notes.db");
		Touch(foreign);

		var result = StoreFiles.Prepare(StoreFiles.InstanceFile(_folder.Root, 1234));

		result.Value.Should().Be(new StoreSweep(DeletedFiles: 0, KeptFiles: 0));
		File.Exists(foreign).Should().BeTrue();
	}

	[Fact]
	public void PrepareKeepsAStoreNameWhoseMiddleIsNotAProcessId()
	{
		var copy = _folder.FileIn("spectra-1234 - Copy.db");
		Touch(copy, copy + "-wal");

		var result = StoreFiles.Prepare(StoreFiles.InstanceFile(_folder.Root, 77));

		result.Value.Should().Be(new StoreSweep(DeletedFiles: 0, KeptFiles: 0));
		File.Exists(copy).Should().BeTrue();
		File.Exists(copy + "-wal").Should().BeTrue();
	}

	[Fact]
	public void PrepareCreatesAMissingFolder()
	{
		var folder = Path.Combine(_folder.Root, "nested", "Spectra");

		var result = StoreFiles.Prepare(StoreFiles.InstanceFile(folder, 1234));

		result.IsSuccess.Should().BeTrue();
		Directory.Exists(folder).Should().BeTrue();
	}

	[Fact]
	public void PrepareFailsWhenTheFolderPathIsAFile()
	{
		var fileInTheWay = _folder.FileIn("occupied");
		Touch(fileInTheWay);
		var own = StoreFiles.InstanceFile(fileInTheWay, 1234);

		var result = StoreFiles.Prepare(own);

		result.IsFailed.Should().BeTrue();
		result.Errors[0].Message.Should().Contain($"'{own}'");
	}

	private static void Touch(params string[] files)
	{
		foreach (var file in files)
		{
			File.WriteAllText(file, string.Empty);
		}
	}

	private static void Execute(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}
}
