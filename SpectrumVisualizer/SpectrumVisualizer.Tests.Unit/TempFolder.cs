using Microsoft.Data.Sqlite;

namespace SpectrumVisualizer.Tests.Unit;

/// <summary>A fresh folder under the system temp folder that a test owns and deletes on dispose.</summary>
internal sealed class TempFolder : IDisposable
{
	public TempFolder()
	{
		Root = Path.Combine(Path.GetTempPath(), $"spectrum-visualizer-store-{Guid.NewGuid():N}");
		Directory.CreateDirectory(Root);
	}

	public string Root { get; }

	public string FileIn(string name)
	{
		return Path.Combine(Root, name);
	}

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		if (Directory.Exists(Root))
		{
			Directory.Delete(Root, recursive: true);
		}
	}
}
