using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.UI.Store;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SpectrumVisualizer.Tests.Unit.UI.Di;

/// <summary>The window's open starts the store, and its close stops it after the distributor.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class AppLifecycleStoreTests : AppLifecycleTestBase
{
	private const string StoreStopped =
		"The spectrum store stopped: {Stored} frames stored, {Dropped} frames dropped, checkpoint {Checkpoint}";

	[AvaloniaFact]
	public async Task OpeningTheWindowStartsTheStore()
	{
		using var provider = BuildContainer();
		var window = ShowMainWindow(provider);

		await PushOneFrameThenCloseAsync(provider, window, SpectrumFrame(Spectrometer.Atp8000, 7));

		StoredSpectrumSeqs(provider).Should().Be("7");
	}

	[AvaloniaFact]
	public async Task ClosingTheWindowStopsTheStoreAfterTheDistributor()
	{
		using var provider = BuildContainer();
		var window = ShowMainWindow(provider);
		var closed = new TaskCompletionSource();
		window.Closed += (_, _) => closed.TrySetResult();

		window.Close();

		await closed.Task.WaitAsync(WaitLimit, TestContext.Current.CancellationToken);
		var templates = LoggedTemplates();
		var distributorStopped = templates.IndexOf(DistributorStopped);
		var storeStopped = templates.IndexOf(StoreStopped);
		distributorStopped.Should().BeGreaterThanOrEqualTo(0);
		storeStopped.Should().BeGreaterThan(distributorStopped);
	}

	[AvaloniaFact]
	public async Task AfterTheCloseTheStoreFileHasAnEmptyWal()
	{
		using var provider = BuildContainer();
		var window = ShowMainWindow(provider);
		var storePath = provider.GetRequiredService<StoreFile>().Path;

		// Open across the close, so the WAL outlives the writer's close and shows what the checkpoint did to it.
		using var idle = await OpenIdleConnectionAsync(storePath);
		await PushOneFrameThenCloseAsync(provider, window, SpectrumFrame(Spectrometer.Atp2000, 3));

		var wal = new FileInfo(storePath + "-wal");
		wal.Exists.Should().BeTrue("the idle connection keeps the WAL file");
		wal.Length.Should().Be(0);
	}

	private static string? StoredSpectrumSeqs(IServiceProvider provider)
	{
		var connectionString = new SqliteConnectionStringBuilder
		{
			DataSource = provider.GetRequiredService<StoreFile>().Path,
			Pooling = false
		}.ToString();
		using var connection = new SqliteConnection(connectionString);
		connection.Open();
		using var command = connection.CreateCommand();
		command.CommandText = "SELECT group_concat(seq) FROM (SELECT seq FROM spectrum ORDER BY id)";
		return command.ExecuteScalar() as string;
	}

	/// <summary>
	/// A connection that has read the store file once the writer created its schema; it never creates the file, which
	/// would make the writer's wipe fail.
	/// </summary>
	private static async Task<SqliteConnection> OpenIdleConnectionAsync(string path)
	{
		var connectionString = new SqliteConnectionStringBuilder
		{
			DataSource = path,
			Mode = SqliteOpenMode.ReadWrite,
			Pooling = false
		}.ToString();
		var deadline = DateTimeOffset.UtcNow + WaitLimit;
		while (true)
		{
			var connection = new SqliteConnection(connectionString);
			try
			{
				connection.Open();
				using var command = connection.CreateCommand();
				command.CommandText = "SELECT count(*) FROM spectrum";
				command.ExecuteScalar();
				return connection;
			}
			catch (SqliteException) when (DateTimeOffset.UtcNow < deadline)
			{
				connection.Dispose();
				await Task.Delay(10, TestContext.Current.CancellationToken);
			}
		}
	}
}
