using Microsoft.Data.Sqlite;

namespace SpectrumVisualizer.Core.Store;

/// <summary>Opens a connection to a spectrum store file with the store's connection settings and pragmas.</summary>
public static class StoreConnection
{
	private const int BusyTimeoutSeconds = 2;
	private const string Pragmas = """
		PRAGMA journal_mode=WAL;
		PRAGMA synchronous=NORMAL;
		PRAGMA journal_size_limit=67108864;
		""";

	/// <summary>Opens the file, creating it when missing; throws SqliteException when SQLite refuses it.</summary>
	public static SqliteConnection Open(string path)
	{
		var settings = new SqliteConnectionStringBuilder
		{
			DataSource = path,
			Mode = SqliteOpenMode.ReadWriteCreate,
			Pooling = false,
			DefaultTimeout = BusyTimeoutSeconds,
		};
		var connection = new SqliteConnection(settings.ToString());
		try
		{
			connection.Open();
			using var command = connection.CreateCommand();
			command.CommandText = Pragmas;
			command.ExecuteNonQuery();
			return connection;
		}
		catch
		{
			connection.Dispose();
			throw;
		}
	}

	/// <summary>Opens the file like Open and makes the connection refuse writes.</summary>
	public static SqliteConnection OpenReader(string path)
	{
		var connection = Open(path);
		try
		{
			using var command = connection.CreateCommand();
			command.CommandText = "PRAGMA query_only=ON";
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
