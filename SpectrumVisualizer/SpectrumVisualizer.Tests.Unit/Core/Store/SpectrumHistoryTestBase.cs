using System.Runtime.InteropServices;

using AwesomeAssertions;

using Microsoft.Data.Sqlite;

using SpectrumVisualizer.Core.Calibration;
using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Tests.Unit.Core.Protocol;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.Core.Store;

/// <summary>A history over a store file in a fresh temp folder, filled through SpectrumDatabase.</summary>
public abstract class SpectrumHistoryTestBase : IDisposable
{
	protected const int TestTimeoutMs = 30_000;

	protected SpectrumHistoryTestBase()
	{
		StorePath = Folder.FileIn("spectra-1.db");
		History = new SpectrumHistory(StorePath);
	}

	protected static DateTimeOffset Start { get; } = StoreSamples.Start;

	/// <summary>The busy timeout is 2 s; a read or a commit that waited on a lock would take at least that long.</summary>
	protected static TimeSpan WellUnderTheBusyTimeout { get; } = TimeSpan.FromSeconds(1);

	private protected TempFolder Folder { get; } = new();

	protected string StorePath { get; }

	protected SpectrumHistory History { get; }

	protected static CancellationToken Token => TestContext.Current.CancellationToken;

	public void Dispose()
	{
		Folder.Dispose();
		GC.SuppressFinalize(this);
	}

	protected void Store(params ReceivedFrame[] frames)
	{
		using var database = SpectrumDatabase.Open(StorePath).Value;
		database.Write(frames).IsSuccess.Should().BeTrue();
	}

	protected async Task<byte?> SeqAtAsync(DateTimeOffset moment)
	{
		var result = await History.SpectrumAtAsync(moment, Token);
		result.IsSuccess.Should().BeTrue();
		return result.Value?.Seq;
	}

	protected async Task<IntensitySeries> SeriesAsync(ushort nm, byte delta, int columns)
	{
		var result = await History.IntensitySeriesAsync(Query(nm, delta, Start, Start.AddSeconds(10), columns), Token);
		result.IsSuccess.Should().BeTrue();
		return result.Value;
	}

	protected static IntensityQuery Query(ushort nm, byte delta, DateTimeOffset from, DateTimeOffset to, int columns)
	{
		return IntensityQuery.Create(nm, delta, from, to, columns).Value;
	}

	/// <summary>An ATP-8000 sample spectrum whose every pixel is the ramp plus the offset.</summary>
	protected static ReceivedFrame Offset(DateTimeOffset receivedAt, ushort offset)
	{
		var pixels = ProtocolSamples.Ramp(Spectrometer.Atp8000);
		for (var i = 0; i < pixels.Length; i++)
		{
			pixels[i] += offset;
		}

		return StoreSamples.Spectrum(receivedAt, Spectrometer.Atp8000, pixels);
	}

	protected static double RampMean(Spectrometer device, ushort nm, byte delta)
	{
		var window = SpectrometerCalibration.For(device).Window(nm, delta);
		var pixels = ProtocolSamples.Ramp(device).AsSpan(window.First, window.Last - window.First + 1);
		return QueryWindows.MeanOf(MemoryMarshal.AsBytes(pixels));
	}

	protected static void Execute(SqliteConnection connection, string sql)
	{
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}
}
