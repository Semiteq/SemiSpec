using FluentResults;

using Microsoft.Data.Sqlite;

namespace SemiSpec.Core.Store;

/// <summary>The store file carries a schema version this build does not use; Found is the version the file carries.</summary>
public sealed class StoreSchemaMismatchError(int found)
	: Error($"The spectrum store file has schema version {found}; this build uses {SpectrumDatabase.SchemaVersion}.")
{
	public int Found { get; } = found;
}

/// <summary>SQLite reported the store file as not a database or as corrupt while opening it.</summary>
public sealed class StoreFileUnreadableError(SqliteException exception)
	: ExceptionalError("SQLite cannot read the spectrum store file", exception)
{
	public int SqliteErrorCode { get; } = exception.SqliteErrorCode;
}
