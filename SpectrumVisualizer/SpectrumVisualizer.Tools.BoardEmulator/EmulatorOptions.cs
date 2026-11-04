using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;

using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.Tools.BoardEmulator;

/// <summary>What the emulated board sends; Rate is in frames per second, 0 sends heartbeats only.</summary>
public sealed record EmulatorOptions(
	string Port,
	Spectrometer Device,
	double Rate,
	ushort WavelengthNm,
	byte Delta,
	int BadCrcEvery)
{
	private const double MinimumRate = 0.001;
	private const byte MaximumDelta = 100;

	private static readonly Option<string> _port = new("--port")
	{
		Description = "Serial port to write frames to, for example COM5.",
		Required = true,
	};

	private static readonly Option<int> _device = new Option<int>("--device")
	{
		Description = "Spectrometer to emulate: 2000 (ATP-2000) or 8000 (ATP-8000).",
		DefaultValueFactory = _ => 8000,
	}.AcceptOnlyFromAmong("2000", "8000");

	private static readonly Option<double> _rate = new("--rate")
	{
		Description = "Spectrum frames per second, at least 0.001; 0 sends only a heartbeat every 3 s. At 115200 "
					  + "baud an ATP-2000 frame takes about 0.36 s, so the port caps the rate near 2.8.",
		DefaultValueFactory = _ => 1.0,
		CustomParser = ParseRate,
	};

	private static readonly Option<ushort?> _wavelength = new("--wavelength")
	{
		Description = "Wavelength of the peak and of the measurement, in nm. Defaults to 650 for ATP-2000 and "
					  + "1550 for ATP-8000.",
	};

	private static readonly Option<byte> _delta = new("--delta")
	{
		Description = "Half-width of the measurement window, in nm, from 0 to 100.",
		DefaultValueFactory = _ => 10,
		CustomParser = result => (byte)ParseWhole(result, "--delta", MaximumDelta),
	};

	private static readonly Option<int> _badCrcEvery = new("--bad-crc-every")
	{
		Description = "Corrupt the CRC of every nth frame sent, heartbeats included; 0 never.",
		CustomParser = result => ParseWhole(result, "--bad-crc-every", int.MaxValue),
	};

	public static RootCommand BuildCommand(Func<EmulatorOptions, CancellationToken, Task<int>> run)
	{
		var root = new RootCommand("Writes board-to-PC protocol frames to a serial port.")
		{
			_port, _device, _rate, _wavelength, _delta, _badCrcEvery,
		};
		root.SetAction((parsed, cancellationToken) => run(From(parsed), cancellationToken));
		return root;
	}

	public static ushort DefaultWavelength(Spectrometer device)
	{
		return device == Spectrometer.Atp2000 ? (ushort)650 : (ushort)1550;
	}

	private static EmulatorOptions From(ParseResult parsed)
	{
		var device = parsed.GetValue(_device) == 2000 ? Spectrometer.Atp2000 : Spectrometer.Atp8000;
		return new EmulatorOptions(
			parsed.GetRequiredValue(_port),
			device,
			parsed.GetValue(_rate),
			parsed.GetValue(_wavelength) ?? DefaultWavelength(device),
			parsed.GetValue(_delta),
			parsed.GetValue(_badCrcEvery));
	}

	private static double ParseRate(ArgumentResult result)
	{
		var text = result.Tokens[^1].Value;
		if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate)
			|| !double.IsFinite(rate)
			|| rate < 0
			|| rate is > 0 and < MinimumRate)
		{
			result.AddError($"Option '--rate' expects 0 or a finite number of at least 0.001, got '{text}'.");
			return 0;
		}

		return rate;
	}

	private static int ParseWhole(ArgumentResult result, string name, int maximum)
	{
		var text = result.Tokens[^1].Value;
		if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
			|| value < 0
			|| value > maximum)
		{
			result.AddError($"Option '{name}' expects a whole number from 0 to {maximum}, got '{text}'.");
			return 0;
		}

		return value;
	}
}
