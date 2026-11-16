using System.Reactive.Linq;
using System.Windows.Input;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using SemiSpec.Core.Protocol;
using SemiSpec.Core.Spectrum;
using SemiSpec.Core.Store;
using SemiSpec.Core.Trend;
using SemiSpec.Tests.Unit.Core.Store;
using SemiSpec.UI.Link;
using SemiSpec.UI.Localization;
using SemiSpec.UI.Messages;
using SemiSpec.UI.Trend;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Trend;

[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendNormalizationViewModelTests : TrendChartTestBase
{
	private readonly TrendNormalizationViewModel _normalization;

	public TrendNormalizationViewModelTests()
	{
		_normalization = NewNormalization();
	}

	[AvaloniaFact]
	public void BeforeTheFirstSpectrumNeitherButtonCanBePressed()
	{
		_normalization.CanPress.Should().BeFalse();
		((ICommand)_normalization.NormalizeSmallCommand).CanExecute(null).Should().BeFalse();
		_normalization.Factor.Should().BeNull();
	}

	[AvaloniaFact]
	public async Task APressNormalizesToItsFactorAndChecksItsButtonOnly()
	{
		AddSpectra(0, 5);
		Tick();

		await _normalization.NormalizeSmallCommand.Execute();

		_normalization.Factor.Should().Be(100);
		_normalization.IsSmallFactor.Should().BeTrue();
		_normalization.IsLargeFactor.Should().BeFalse();
		Cache.Normalization.Should().Be(new NormalizationState(100));
	}

	[AvaloniaFact]
	public async Task APressOnTheActiveSegmentChangesNothingAndOffSwitchesItOff()
	{
		AddSpectra(0, 5);
		Tick();
		await _normalization.NormalizeSmallCommand.Execute();
		var requests = Cache.Normalization;

		await _normalization.NormalizeSmallCommand.Execute();

		_normalization.Factor.Should().Be(100, "the active segment is no switch-off");
		Cache.Normalization.Should().Be(requests);

		await _normalization.TurnOffCommand.Execute();

		_normalization.Factor.Should().BeNull();
		_normalization.IsOff.Should().BeTrue();
		Cache.Normalization.IsOn.Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task OffStaysEnabledWhileTheFactorSegmentsAreDisabledAndTheirTipSaysWhy()
	{
		AddSpectra(0, 5);
		Tick();
		((ICommand)_normalization.TurnOffCommand).CanExecute(null).Should().BeFalse("nothing to switch off yet");
		_normalization.UnavailableReason.Should().BeNull();
		await _normalization.NormalizeSmallCommand.Execute();

		LinkStatuses.OnNext(new LinkStatus(LinkState.NotResponding, "COM3", null, 0, null));

		((ICommand)_normalization.NormalizeLargeCommand).CanExecute(null).Should().BeFalse();
		((ICommand)_normalization.TurnOffCommand).CanExecute(null).Should().BeTrue();
		_normalization.UnavailableReason.Should().Be(Resources.TrendNormalizeUnavailableTip);
	}

	[AvaloniaFact]
	public async Task AFlatSignalReadsTheFactorAfterAPress()
	{
		AddFlat(0, 500);
		Tick();

		await _normalization.NormalizeLargeCommand.Execute();
		AddFlat(1, 250);

		var series = Cache.Fold(IntensityQueryAt(1)).Columns;
		series.Single(column => column.Count > 0).Min.Should().BeApproximately(500.0, 1e-9, "250 / 500 x 1000");
	}

	[AvaloniaFact]
	public async Task ThePressOnTheOtherButtonFixesANewReferenceWithItsFactor()
	{
		AddFlat(0, 500);
		Tick();
		await _normalization.NormalizeSmallCommand.Execute();
		AddFlat(1, 250);

		await _normalization.NormalizeLargeCommand.Execute();

		_normalization.Factor.Should().Be(1000);
		_normalization.IsSmallFactor.Should().BeFalse();
		var series = Cache.Fold(IntensityQueryAt(1)).Columns;
		series.Single(column => column.Count > 0).Min.Should().BeApproximately(1000.0, 1e-9, "the new reference is 250");
	}

	[AvaloniaFact]
	public async Task ADarkNewestSpectrumIsRefusedWithAWarningAndTheButtonStaysReleased()
	{
		AddSpectra(0, 3);
		Add(StoreSamples.Dark(At(3)));
		Tick();

		await _normalization.NormalizeSmallCommand.Execute();

		_normalization.Factor.Should().BeNull();
		_normalization.IsSmallFactor.Should().BeFalse();
		MessagePanel.NewestEntry!.View.Should().Be(new MessageView(
			Resources.NormalizationDarkTitle,
			Resources.NormalizationDarkDetail,
			Resources.NormalizationDarkRemedy,
			MessageSeverity.Warning));
	}

	[AvaloniaFact]
	public async Task AKeyChangeReleasesTheButtonsAndAddsOneInfoLine()
	{
		AddSpectra(0, 5);
		Tick();
		await _normalization.NormalizeSmallCommand.Execute();

		Add(StoreSamples.Spectrum(At(5), nm: 1400, seq: NextSeq()));
		Tick();

		_normalization.Factor.Should().BeNull();
		MessagePanel.Entries.Should().ContainSingle();
		MessagePanel.NewestEntry!.View.Should().Be(new MessageView(
			Resources.NormalizationResetTitle,
			Resources.NormalizationResetDetail,
			string.Empty,
			MessageSeverity.Info));
	}

	[AvaloniaFact]
	public void TheButtonsAreDisabledWhileTheLinkIsNotReceiving()
	{
		AddSpectra(0, 5);
		Tick();
		_normalization.CanPress.Should().BeTrue();

		LinkStatuses.OnNext(new LinkStatus(LinkState.NotResponding, "COM3", null, 0, null));

		_normalization.CanPress.Should().BeFalse();
		((ICommand)_normalization.NormalizeLargeCommand).CanExecute(null).Should().BeFalse();
	}

	[AvaloniaFact]
	public void TheButtonsAreDisabledOnceTheNewestSpectrumIsOlderThanTenSeconds()
	{
		AddSpectra(0, 5);
		Tick();

		Time.Advance(TimeSpan.FromSeconds(11));
		Tick();

		_normalization.CanPress.Should().BeFalse();
	}

	[AvaloniaFact]
	public void TheButtonsAreDisabledWhileTheSpectrometerDoesNotCoverTheWavelength()
	{
		AddSpectra(0, 5);
		Add(StoreSamples.Spectrum(At(5), Spectrometer.Atp2000, seq: NextSeq()));
		Tick();

		_normalization.CanPress.Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task AToggleDropsAHeldRangeAndNamesTheFactorInTheLabel()
	{
		AddSpectra(0, 5);
		Tick();
		Chart.SetVerticalRange(new AxisRange(0, 50_000));

		await _normalization.NormalizeLargeCommand.Execute();
		Scheduler.AdvanceBy(1);

		Chart.ManualVerticalRange.Should().BeNull();
		Chart.Plot.Axes.Left.Label.Text.Should().Be(
			Resources.FormatTrendAxisNormalizedFormat(1000, StoreSamples.SampleNm, StoreSamples.SampleDelta));
	}

	[AvaloniaFact]
	public async Task WhileAPastMomentIsShownThePressStillFixesTheNewestLiveSpectrum()
	{
		AddFlat(0, 500);
		AddFlat(1, 300);
		Tick();
		var past = StoreSamples.Spectrum(At(0), Spectrometer.Atp8000, Flat(500)).Frame.Measurement!;
		Switching.ShowMoment(past, At(0));

		await _normalization.NormalizeLargeCommand.Execute();
		AddFlat(2, 300);

		var series = Cache.Fold(IntensityQueryAt(2)).Columns;
		series.Single(column => column.Count > 0).Min.Should().BeApproximately(1000.0, 1e-9, "the reference is 300");
	}

	[AvaloniaFact]
	public async Task AKeyChangeResetDropsAHeldRange()
	{
		AddSpectra(0, 5);
		Tick();
		await _normalization.NormalizeSmallCommand.Execute();
		Tick();
		Chart.SetVerticalRange(new AxisRange(0, 150));

		Add(StoreSamples.Spectrum(At(5), nm: 1400, seq: NextSeq()));
		Tick();

		Chart.ManualVerticalRange.Should().BeNull("raw values would fall outside a normalized range");
	}

	protected override void Dispose(bool disposing)
	{
		_normalization.Dispose();
		base.Dispose(disposing);
	}

	private static ushort[] Flat(ushort value)
	{
		return [.. Enumerable.Repeat(value, Spectrometer.Atp8000.PixelCount)];
	}

	/// <summary>Lets the one-second availability sample run.</summary>
	private void Tick()
	{
		Scheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks);
	}

	private static IntensityQuery IntensityQueryAt(double second)
	{
		return IntensityQuery.Create(
			StoreSamples.SampleNm,
			StoreSamples.SampleDelta,
			At(second),
			At(second + 1),
			TimeSpan.FromSeconds(1)).Value;
	}
}
