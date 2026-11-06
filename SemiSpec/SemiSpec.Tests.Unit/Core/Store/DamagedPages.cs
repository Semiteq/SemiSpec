namespace SemiSpec.Tests.Unit.Core.Store;

/// <summary>Overwrites pages of a closed store file with bytes SQLite cannot parse.</summary>
internal static class DamagedPages
{
	public const int DatabaseHeaderBytes = 100;
	public const int PageBytes = 4096;

	// No SQLite header starts with it, and as a b-tree page type it is invalid.
	private const byte DamageByte = 0xFF;

	public static byte[] Bytes(int count)
	{
		return [.. Enumerable.Repeat(DamageByte, count)];
	}

	/// <summary>Overwrites page 1 after its file header: the schema table.</summary>
	public static void SchemaPage(string path)
	{
		Overwrite(path, DatabaseHeaderBytes, PageBytes - DatabaseHeaderBytes);
	}

	/// <summary>Overwrites every page past page 1, so the schema reads and every table or index read fails.</summary>
	public static void TablePages(string path)
	{
		Overwrite(path, PageBytes, (int)new FileInfo(path).Length - PageBytes);
	}

	/// <summary>Overwrites one page, counted from 1 as sqlite_master.rootpage counts it.</summary>
	public static void Page(string path, long page)
	{
		Overwrite(path, (page - 1) * PageBytes, PageBytes);
	}

	private static void Overwrite(string path, long offset, int count)
	{
		using var file = new FileStream(path, FileMode.Open, FileAccess.Write);
		file.Position = offset;
		file.Write(Bytes(count));
	}
}
