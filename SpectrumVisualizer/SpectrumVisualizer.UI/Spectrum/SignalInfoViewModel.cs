using System.Globalization;

using ReactiveUI;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.UI.Localization;

namespace SpectrumVisualizer.UI.Spectrum;

/// <summary>The signal pane's texts for the shown spectrum; every row reads "-" until there is one.</summary>
public sealed class SignalInfoViewModel(TimeProvider timeProvider) : ReactiveObject
{
	private const string UpdatedFormat = "HH:mm:ss.fff";
	private const string BoardValueFormat = "G6";

	public DisplayedSpectrum? Spectrum
	{
		get;
		set
		{
			// By reference: record equality would compare every pixel of the frame.
			if (ReferenceEquals(field, value))
			{
				return;
			}

			field = value;
			this.RaisePropertyChanged();
			this.RaisePropertyChanged(nameof(SpectrometerText));
			this.RaisePropertyChanged(nameof(WavelengthText));
			this.RaisePropertyChanged(nameof(WindowText));
			this.RaisePropertyChanged(nameof(AmplitudeText));
			this.RaisePropertyChanged(nameof(SnrText));
			this.RaisePropertyChanged(nameof(QText));
			this.RaisePropertyChanged(nameof(UpdatedText));
		}
	}

	public string SpectrometerText => Measurement?.Device.DisplayName ?? Resources.StatusNoValue;

	public string WavelengthText => Measurement is { } measurement
		? Resources.FormatSignalWavelengthFormat(measurement.WavelengthNm)
		: Resources.StatusNoValue;

	public string WindowText => Measurement is { } measurement
		? Resources.FormatSignalWindowFormat(measurement.Delta)
		: Resources.StatusNoValue;

	public string AmplitudeText => BoardValueText(Measurement?.Amplitude);

	public string SnrText => BoardValueText(Measurement?.Snr);

	public string QText => BoardValueText(Measurement?.Q);

	public string UpdatedText => Spectrum is { } spectrum
		? TimeZoneInfo.ConvertTime(spectrum.ReceivedAt, timeProvider.LocalTimeZone)
			.ToString(UpdatedFormat, CultureInfo.InvariantCulture)
		: Resources.StatusNoValue;

	private SpectrumMeasurement? Measurement => Spectrum?.Measurement;

	private static string BoardValueText(float? value)
	{
		return value is { } number && float.IsFinite(number)
			? number.ToString(BoardValueFormat, CultureInfo.CurrentCulture)
			: Resources.StatusNoValue;
	}
}
