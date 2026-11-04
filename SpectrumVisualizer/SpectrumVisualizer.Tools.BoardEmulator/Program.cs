using RJCP.IO.Ports;

namespace SpectrumVisualizer.Tools.BoardEmulator;

public static class Program
{
	private const int BaudRate = 115200;

	private static readonly TimeSpan _tickInterval = TimeSpan.FromMilliseconds(10);

	public static Task<int> Main(string[] arguments)
	{
		return EmulatorCommand.Build(RunAsync).Parse(arguments).InvokeAsync();
	}

	private static async Task<int> RunAsync(EmulatorOptions options, CancellationToken cancellationToken)
	{
		using var port = new SerialPortStream(options.Port, BaudRate);
		try
		{
			port.Open();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			Console.Error.WriteLine($"Cannot open {options.Port}: {exception.Message}");
			return 1;
		}

		Console.WriteLine(
			$"{options.Port}: {options.Device}, {options.Rate} frames/s, {options.WavelengthNm} nm "
			+ $"+/- {options.Delta} nm, bad CRC every {options.BadCrcEvery} (0 = never). Ctrl+C stops.");

		var schedule = new FrameSchedule(options, TimeProvider.System);
		using var timer = new PeriodicTimer(_tickInterval);
		try
		{
			while (await timer.WaitForNextTickAsync(cancellationToken))
			{
				if (schedule.Next() is { } bytes)
				{
					await port.WriteAsync(bytes, cancellationToken);
				}
			}
		}
		catch (OperationCanceledException)
		{
			Console.WriteLine("stopped");
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			Console.Error.WriteLine($"Cannot write to {options.Port}: {exception.Message}");
			return 1;
		}

		return 0;
	}
}
