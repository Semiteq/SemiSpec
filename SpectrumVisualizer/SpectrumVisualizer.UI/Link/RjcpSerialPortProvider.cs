using System.Globalization;

using FluentResults;

using RJCP.IO.Ports;

namespace SpectrumVisualizer.UI.Link;

/// <summary>Opens the board's port at 115200 baud, 8N1, with 1 s read timeouts that throw.</summary>
public sealed class RjcpSerialPortProvider : ISerialPortProvider
{
	private const int BaudRate = 115200;
	private const int DataBits = 8;
	private const int ReadTimeoutMilliseconds = 1000;
	private const int SharingViolation = 32;
	private const string ComPrefix = "COM";

	public IReadOnlyList<string> PortNames()
	{
		using var enumerator = new SerialPortStream();

		return SortPortNames(enumerator.GetPortNames());
	}

	/// <summary>Every failure, of the listing and the constructor included, is a retryable link error.</summary>
	public Result<Stream> Open(string portName)
	{
		SerialPortStream? port = null;
		try
		{
			if (!PortNames().Contains(portName, StringComparer.OrdinalIgnoreCase))
			{
				return Result.Fail<Stream>(new PortNotFoundError(portName));
			}

			port = new SerialPortStream(portName, BaudRate, DataBits, Parity.None, StopBits.One)
			{
				ReadTimeout = ReadTimeoutMilliseconds,
				ThrowOnReadError = true
			};
			port.Open();
			return Result.Ok<Stream>(port);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			port?.Dispose();
			return Result.Fail<Stream>(MapOpenFailure(portName, exception));
		}
	}

	/// <summary>Orders COM ports by their number, then every other name by ordinal comparison.</summary>
	internal static IReadOnlyList<string> SortPortNames(IEnumerable<string> names)
	{
		return [.. names.OrderBy(name => ComNumber(name) ?? int.MaxValue).ThenBy(name => name, StringComparer.Ordinal)];
	}

	/// <summary>A busy port is told apart; any other failure counts as a missing port and keeps its exception.</summary>
	internal static IError MapOpenFailure(string portName, Exception exception)
	{
		return exception switch
		{
			UnauthorizedAccessException => new PortBusyError(portName),
			IOException { HResult: SharingViolation } => new PortBusyError(portName),
			_ => new PortNotFoundError(portName).CausedBy(exception)
		};
	}

	private static int? ComNumber(string name)
	{
		if (name.StartsWith(ComPrefix, StringComparison.OrdinalIgnoreCase)
			&& int.TryParse(name.AsSpan(ComPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
		{
			return number;
		}

		return null;
	}
}
