using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;

using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.Tools.BoardEmulator;

/// <summary>
/// The command line of the emulator; range rules live in option parsers, the cross-option rule in a validator.
/// </summary>
public static class EmulatorCommand
{
	private const double MinimumRate = 0.001;
	private const int MinimumWavelengthNm = 189;
	private const int MaximumWavelengthNm = 2460;
	private const int MaximumDelta = 100;

	private static readonly Dictionary<string, Spectrometer> _models = new()
	{
		["2000"] = Spectrometer.Atp2000,
		["8000"] = Spectrometer.Atp8000,
	};

	private static readonly Option<string> _port = new("--port")
	{
		Description = "Serial port to write frames to, for example COM5.",
		Required = true,
	};

	private static readonly Option<int> _device = new Option<int>("--device")
	{
		Description = "Spectrometer to emulate: 2000 (ATP-2000) or 8000 (ATP-8000).",
		DefaultValueFactory = _ => 8000,
	}.AcceptOnlyFromAmong([.. _models.Keys]);

	private static readonly Option<double> _rate = new("--rate")
	{
		Description = "Spectrum frames per second, at least 0.001; 0 sends only a heartbeat every 3 s. At 115200 "
					  + "baud an ATP-2000 frame takes about 0.36 s, so the port caps the rate near 2.8.",
		DefaultValueFactory = _ => 1.0,
		CustomParser = ParseRate,
	};

	private static readonly Option<ushort?> _wavelength = new("--wavelength")
	{
		Description = "Wavelength of the peak and of the measurement, in nm, from 189 to 2460. Defaults to 650 for "
					  + "ATP-2000 and 1550 for ATP-8000.",
		CustomParser = result =>
			(ushort)ParseWhole(result, "--wavelength", MinimumWavelengthNm, MaximumWavelengthNm),
	};

	private static readonly Option<byte> _delta = new("--delta")
	{
		Description = "Half-width of the measurement window, in nm, from 0 to 100.",
		DefaultValueFactory = _ => 10,
		CustomParser = result => (byte)ParseWhole(result, "--delta", 0, MaximumDelta),
	};

	private static readonly Option<int> _badCrcEvery = new("--bad-crc-every")
	{
		Description = "Corrupt the CRC of every nth frame sent, heartbeats included; 0 never.",
		CustomParser = result => ParseWhole(result, "--bad-crc-every", 0, int.MaxValue),
	};

	private static readonly Option<int> _darkEvery = new("--dark-every")
	{
		Description = "Send every nth spectrum as a dark measurement: all pixels, amplitude and snr zero; 0 never.",
		CustomParser = result => ParseWhole(result, "--dark-every", 0, int.MaxValue),
	};

	private static readonly Option<bool> _bothConnected = new("--both-connected")
	{
		Description = "Report both spectrometers as connected; spectra still come from --device only.",
	};

	private static readonly Option<int?> _failed = new Option<int?>("--failed")
	{
		Description = "Set the measurement-failed bit of 2000 (ATP-2000) or 8000 (ATP-8000). The board clears the "
					  + "bit of --device before each of its spectra, so that bit shows in heartbeats only. Naming "
					  + "the other spectrometer requires --both-connected.",
	}.AcceptOnlyFromAmong([.. _models.Keys]);

	public static RootCommand Build(Func<EmulatorOptions, CancellationToken, Task<int>> run)
	{
		var root = new RootCommand("Writes board-to-PC protocol frames to a serial port.")
		{
			_port, _device, _rate, _wavelength, _delta, _badCrcEvery, _darkEvery, _bothConnected, _failed,
		};
		root.Validators.Add(RequireBothConnectedForTheOtherFailedBit);
		root.SetAction((parsed, cancellationToken) => run(From(parsed), cancellationToken));
		return root;
	}

	private static void RequireBothConnectedForTheOtherFailedBit(CommandResult result)
	{
		// Command validators run before option values convert, so GetValue would throw on a value outside _models.
		if (!IsAbsentOrAModel(result, _failed) || !IsAbsentOrAModel(result, _device))
		{
			return;
		}

		if (result.GetValue(_failed) is { } failed
			&& failed != result.GetValue(_device)
			&& !result.GetValue(_bothConnected))
		{
			result.AddError(
				$"Option '--failed {failed}' names a spectrometer that '--device' does not emulate; "
				+ "add '--both-connected'.");
		}
	}

	private static bool IsAbsentOrAModel(CommandResult result, Option option)
	{
		return result.GetResult(option) is not { } optionResult
			|| optionResult.Tokens.All(token => _models.ContainsKey(token.Value));
	}

	private static EmulatorOptions From(ParseResult parsed)
	{
		var device = ToSpectrometer(parsed.GetValue(_device));
		return new EmulatorOptions(
			parsed.GetRequiredValue(_port),
			device,
			parsed.GetValue(_rate),
			parsed.GetValue(_wavelength) ?? EmulatorOptions.DefaultWavelength(device),
			parsed.GetValue(_delta),
			parsed.GetValue(_badCrcEvery),
			parsed.GetValue(_darkEvery),
			parsed.GetValue(_bothConnected),
			parsed.GetValue(_failed) is { } failed ? ToSpectrometer(failed) : null);
	}

	private static Spectrometer ToSpectrometer(int model)
	{
		return _models[model.ToString(CultureInfo.InvariantCulture)];
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

	private static int ParseWhole(ArgumentResult result, string name, int minimum, int maximum)
	{
		var text = result.Tokens[^1].Value;
		if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
			|| value < minimum
			|| value > maximum)
		{
			result.AddError($"Option '{name}' expects a whole number from {minimum} to {maximum}, got '{text}'.");
			return 0;
		}

		return value;
	}
}
