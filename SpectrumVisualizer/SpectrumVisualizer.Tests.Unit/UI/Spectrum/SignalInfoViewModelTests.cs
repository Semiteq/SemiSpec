using AwesomeAssertions;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Spectrum;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Spectrum;

[Trait("Component", "UI")]
[Trait("Area", "Spectrum")]
[Trait("Category", "Unit")]
public sealed class SignalInfoViewModelTests
{
	private static readonly TimeZoneInfo _plusThree = TimeZoneInfo.CreateCustomTimeZone(
		"Test+03", TimeSpan.FromHours(3), "Test+03", "Test+03");

	private static readonly DateTimeOffset _receivedAt = new(2026, 11, 4, 9, 15, 30, 123, TimeSpan.Zero);

	private readonly SignalInfoViewModel _viewModel = new(new FixedZoneClock(_plusThree));

	[Theory]
	[InlineData("en")]
	[InlineData("ru")]
	public void EveryRowReadsNoValueBeforeASpectrum(string cultureName)
	{
		using var culture = new CultureScope(cultureName);

		ShownTexts().Should().AllBe(Resources.StatusNoValue);
	}

	[Theory]
	[InlineData("en", "12.5", "0.000123457")]
	[InlineData("ru", "12,5", "0,000123457")]
	public void ASpectrumShowsTheBoardValuesUnderTheCurrentCulture(string cultureName, string q, string snr)
	{
		using var culture = new CultureScope(cultureName);

		_viewModel.Spectrum = Spectrum(Spectrometer.Atp8000, amplitude: 30000f, snr: 0.0001234567f, q: 12.5f);

		_viewModel.SpectrometerText.Should().Be(Spectrometer.Atp8000.DisplayName);
		_viewModel.WavelengthText.Should().Be(Resources.FormatSignalWavelengthFormat(1550));
		_viewModel.WindowText.Should().Be(Resources.FormatSignalWindowFormat(10));
		_viewModel.AmplitudeText.Should().Be("30000");
		_viewModel.SnrText.Should().Be(snr);
		_viewModel.QText.Should().Be(q);
	}

	[Theory]
	[InlineData(float.NaN)]
	[InlineData(float.PositiveInfinity)]
	[InlineData(float.NegativeInfinity)]
	public void ANonFiniteBoardValueReadsNoValue(float value)
	{
		using var culture = new CultureScope("en");

		_viewModel.Spectrum = Spectrum(Spectrometer.Atp8000, amplitude: value, snr: value, q: value);

		_viewModel.AmplitudeText.Should().Be(Resources.StatusNoValue);
		_viewModel.SnrText.Should().Be(Resources.StatusNoValue);
		_viewModel.QText.Should().Be(Resources.StatusNoValue);
		_viewModel.SpectrometerText.Should().Be(Spectrometer.Atp8000.DisplayName);
	}

	[Fact]
	public void TheUpdateTimeIsTheReceivedTimeInTheLocalZone()
	{
		_viewModel.Spectrum = Spectrum(Spectrometer.Atp8000, amplitude: 1f, snr: 1f, q: 1f);

		_viewModel.UpdatedText.Should().Be("12:15:30.123");
	}

	[Fact]
	public void SettingTheSpectrumRaisesEveryText()
	{
		var raised = new List<string?>();
		_viewModel.PropertyChanged += (_, arguments) => raised.Add(arguments.PropertyName);

		_viewModel.Spectrum = Spectrum(Spectrometer.Atp8000, amplitude: 1f, snr: 1f, q: 1f);

		raised.Should().BeEquivalentTo(
			nameof(SignalInfoViewModel.Spectrum),
			nameof(SignalInfoViewModel.SpectrometerText),
			nameof(SignalInfoViewModel.WavelengthText),
			nameof(SignalInfoViewModel.WindowText),
			nameof(SignalInfoViewModel.AmplitudeText),
			nameof(SignalInfoViewModel.SnrText),
			nameof(SignalInfoViewModel.QText),
			nameof(SignalInfoViewModel.UpdatedText));
	}

	[Fact]
	public void SettingTheSameSpectrumAgainRaisesNothing()
	{
		var spectrum = Spectrum(Spectrometer.Atp8000, amplitude: 1f, snr: 1f, q: 1f);
		_viewModel.Spectrum = spectrum;
		var raised = 0;
		_viewModel.PropertyChanged += (_, _) => raised++;

		_viewModel.Spectrum = spectrum;

		raised.Should().Be(0);
	}

	private static DisplayedSpectrum Spectrum(Spectrometer device, float amplitude, float snr, float q)
	{
		var measurement = new SpectrumMeasurement(
			device, 1550, 10, amplitude, snr, q, new ushort[device.PixelCount]);
		return new DisplayedSpectrum(measurement, _receivedAt);
	}

	private string[] ShownTexts()
	{
		return
		[
			_viewModel.SpectrometerText,
			_viewModel.WavelengthText,
			_viewModel.WindowText,
			_viewModel.AmplitudeText,
			_viewModel.SnrText,
			_viewModel.QText,
			_viewModel.UpdatedText,
		];
	}

	private sealed class FixedZoneClock(TimeZoneInfo localTimeZone) : TimeProvider
	{
		public override TimeZoneInfo LocalTimeZone => localTimeZone;
	}
}
