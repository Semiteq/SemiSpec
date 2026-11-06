using System.Globalization;

using FluentResults;

using Microsoft.Extensions.Logging;

using RJCP.IO.Ports;

namespace SemiSpec.UI.Link;

/// <summary>Opens the board's port at 115200 baud, 8N1, with 1 s read timeouts that throw.</summary>
public sealed class RjcpSerialPortProvider(ILogger<RjcpSerialPortProvider> logger) : ISerialPortProvider
{
	private const int BaudRate = 115200;
	private const int DataBits = 8;
	private const int ReadTimeoutMilliseconds = 1000;
	private const int SharingViolation = 32;
	private const string ComPrefix = "COM";

	public Result<IReadOnlyList<string>> PortNames()
	{
		try
		{
			using var enumerator = new SerialPortStream();

			return Result.Ok(SortPortNames(enumerator.GetPortNames()));
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			logger.LogWarning(exception, "Listing the serial ports failed");
			return Result.Fail<IReadOnlyList<string>>(new ExceptionalError(exception));
		}
	}

	/// <summary>Every failure, of the constructor included, is a retryable link error.</summary>
	public Result<Stream> Open(string portName)
	{
		SerialPortStream? port = null;
		try
		{
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

	internal static IReadOnlyList<string> SortPortNames(IEnumerable<string> names)
	{
		return
		[
			.. names
				.OrderBy(name => ComNumber(name) ?? int.MaxValue)
				.ThenBy(name => name, StringComparer.Ordinal)
		];
	}

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
			&& int.TryParse(
				name.AsSpan(ComPrefix.Length),
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out var number))
		{
			return number;
		}

		return null;
	}
}
