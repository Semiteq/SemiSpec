using System.CommandLine.Parsing;
using System.Globalization;

using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.Tools;

/// <summary>
/// The option parsing both tools share; each tool project compiles this file as its own internal copy.
/// </summary>
internal static class ToolCommandLine
{
	/// <summary>The --device and --failed values: the spectrometer's model number.</summary>
	public static readonly IReadOnlyDictionary<string, Spectrometer> Models = new Dictionary<string, Spectrometer>
	{
		["2000"] = Spectrometer.Atp2000,
		["8000"] = Spectrometer.Atp8000,
	};

	public static Spectrometer ToSpectrometer(int model)
	{
		return Models[model.ToString(CultureInfo.InvariantCulture)];
	}

	public static int ParseWhole(ArgumentResult result, string name, int minimum, int maximum)
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
