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

	private string HistoryFile => StoreFiles.HistoryFile(_folder.Root);

	public void Dispose()
	{
		_folder.Dispose();
	}

	[Fact]
	public void TheHistoryFileIsSpectraDbInTheStoreFolder()
	{
		HistoryFile.Should().Be(_folder.FileIn("spectra.db"));
	}

	[Fact]
	public void PrepareCreatesAMissingFolder()
	{
		var folder = Path.Combine(_folder.Root, "nested", "Spectra");

		var result = StoreFiles.Prepare(StoreFiles.HistoryFile(folder));

		result.IsSuccess.Should().BeTrue();
		Directory.Exists(folder).Should().BeTrue();
	}

	[Fact]
	public void PrepareKeepsTheHistoryFileAndItsRows()
	{
		using (var connection = OpenPlain(HistoryFile))
		{
			Execute(connection, "CREATE TABLE t (x INTEGER); INSERT INTO t VALUES (1);");
		}

		var result = StoreFiles.Prepare(HistoryFile);

		result.Value.Should().Be(0);
		using var reopened = OpenPlain(HistoryFile);
		using var count = reopened.CreateCommand();
		count.CommandText = "SELECT count(*) FROM t";
		count.ExecuteScalar().Should().Be(1L);
	}

	[Fact]
	public void PrepareDeletesLegacyInstanceFilesAndTheirSidecars()
	{
		var legacy = _folder.FileIn("spectra-1234.db");
		Touch(legacy, legacy + "-wal", legacy + "-shm");

		var result = StoreFiles.Prepare(HistoryFile);

		result.Value.Should().Be(1);
		Directory.GetFiles(_folder.Root).Should().BeEmpty();
	}

	[Fact]
	public void PrepareDeletesOrphanLegacySidecars()
	{
		var orphan = _folder.FileIn("spectra-77.db");
		Touch(orphan + "-wal", orphan + "-shm");

		var result = StoreFiles.Prepare(HistoryFile);

		result.Value.Should().Be(0, "a sidecar is no store file");
		Directory.GetFiles(_folder.Root).Should().BeEmpty();
	}

	[Fact]
	public void PrepareKeepsALegacyFileAnotherProcessHoldsOpen()
	{
		var held = _folder.FileIn("spectra-77.db");
		using var connection = OpenPlain(held);
		Execute(connection, "PRAGMA journal_mode=WAL; CREATE TABLE t (x INTEGER); INSERT INTO t VALUES (1);");

		var result = StoreFiles.Prepare(HistoryFile);

		result.Value.Should().Be(0);
		File.Exists(held).Should().BeTrue();
		File.Exists(held + "-wal").Should().BeTrue();
	}

	[Fact]
	public void PrepareKeepsNamesOutsideTheLegacyPattern()
	{
		string[] kept =
		[
			_folder.FileIn("spectra-1234 - Copy.db"),
			_folder.FileIn("spectra-1234 - Copy.db-wal"),
			_folder.FileIn("spectra.broken-20261106-101500.db"),
			_folder.FileIn("spectra.db.damaged"),
			_folder.FileIn("notes.db"),
		];
		Touch(kept);

		var result = StoreFiles.Prepare(HistoryFile);

		result.Value.Should().Be(0);
		Directory.GetFiles(_folder.Root).Should().BeEquivalentTo(kept);
	}

	[Fact]
	public void PrepareFailsWhenTheFolderPathIsAFile()
	{
		var fileInTheWay = _folder.FileIn("occupied");
		Touch(fileInTheWay);
		var own = StoreFiles.HistoryFile(fileInTheWay);

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

	private static SqliteConnection OpenPlain(string path)
	{
		var connection = new SqliteConnection(
			new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
		connection.Open();
		return connection;
	}

	private static void Execute(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}
}
