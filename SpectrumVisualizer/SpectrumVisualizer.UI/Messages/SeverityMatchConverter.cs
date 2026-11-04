using System.Globalization;

using Avalonia.Data;
using Avalonia.Data.Converters;

namespace SpectrumVisualizer.UI.Messages;

/// <summary>
/// Answers whether an entry carries the severity named by the converter parameter, so the three
/// severity classes of the panel's row template share one rule instead of three mirror properties.
/// </summary>
public sealed class SeverityMatchConverter : IValueConverter
{
	public static readonly SeverityMatchConverter Instance = new();

	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return value is MessageSeverity severity
			&& parameter is string name
			&& Enum.TryParse<MessageSeverity>(name, out var expected)
			&& severity == expected;
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return BindingOperations.DoNothing;
	}
}
