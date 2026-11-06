using FluentResults;

using Microsoft.Data.Sqlite;

namespace SemiSpec.Core.Store;

/// <summary>Tells a failure SQLite reports as a damaged store file, not a database or corrupt, from any other.</summary>
public static class StoreDamage
{
	private const int SqliteCorrupt = 11;
	private const int SqliteNotADatabase = 26;

	/// <summary>True when the exception or one of its inner exceptions is a SQLite not-a-database or corrupt error.</summary>
	public static bool IsDamage(Exception exception)
	{
		for (var cause = exception; cause is not null; cause = cause.InnerException)
		{
			if (cause is SqliteException { SqliteErrorCode: SqliteCorrupt or SqliteNotADatabase })
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>True when one of the errors or of their reasons carries a damage exception.</summary>
	public static bool IsDamage(IEnumerable<IError> errors)
	{
		return errors.Any(error => IsDamage(error.Reasons)
			|| (error is IExceptionalError { Exception: { } exception } && IsDamage(exception)));
	}
}
