using Microsoft.Data.Sqlite;

namespace SpectrumVisualizer.Core.Store;

/// <summary>Opens a connection to a spectrum store file with the store's connection settings and pragmas.</summary>
public static class StoreConnection
{
	/// <summary>Moves the WAL into the file and truncates it; its one row's first column is 1 when a reader kept it busy.</summary>
	public const string CheckpointTruncate = "PRAGMA wal_checkpoint(TRUNCATE)";

	private const int BusyTimeoutSeconds = 2;
	private const string WriterPragmas = """
		PRAGMA journal_mode=WAL;
		PRAGMA synchronous=NORMAL;
		PRAGMA journal_size_limit=67108864;
		""";

	private const string ReaderPragmas = "PRAGMA query_only=ON";

	/// <summary>Opens the file for the writer, creating it when missing; throws SqliteException when SQLite refuses it.</summary>
	public static SqliteConnection Open(string path)
	{
		return Open(path, SqliteOpenMode.ReadWriteCreate, WriterPragmas);
	}

	/// <summary>
	/// Opens an existing file and refuses writes through query_only; throws SqliteException when the file is missing.
	/// The mode stays ReadWrite, because a WAL reader writes the -shm file.
	/// </summary>
	public static SqliteConnection OpenReader(string path)
	{
		return Open(path, SqliteOpenMode.ReadWrite, ReaderPragmas);
	}

	private static SqliteConnection Open(string path, SqliteOpenMode mode, string pragmas)
	{
		var settings = new SqliteConnectionStringBuilder
		{
			DataSource = path,
			Mode = mode,
			Pooling = false,
			DefaultTimeout = BusyTimeoutSeconds,
		};
		var connection = new SqliteConnection(settings.ToString());
		try
		{
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = pragmas;
			command.ExecuteNonQuery();
			return connection;
		}
		catch
		{
			connection.Dispose();
			throw;
		}
	}
}
