using System.Globalization;
using System.Reactive.Linq;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using ScottPlot.TickGenerators;

using SemiSpec.UI.Chart;
using SemiSpec.UI.Localization;

using Xunit;

using static SemiSpec.Tests.Unit.UI.Spectrum.SpectrumSamples;

namespace SemiSpec.Tests.Unit.UI.Spectrum;

[Trait("Component", "UI")]
[Trait("Area", "Spectrum")]
[Trait("Category", "Unit")]
public sealed class SpectrumViewModelCommandTests : SpectrumViewModelTestBase
{
	private static readonly DateTimeOffset _moment = new(2026, 11, 4, 8, 30, 15, TimeSpan.Zero);

	[AvaloniaFact]
	public void StickToZeroIsEnabledOnceASpectrumIsShown()
	{
		CanExecute(ViewModel.StickToZeroCommand).Should().BeFalse();

		_source.Current = Atp8000Peak;
		SignalAndRun();

		CanExecute(ViewModel.StickToZeroCommand).Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task ResetScaleShowsTheFrozenState()
	{
		CanExecute(ViewModel.ResetScaleCommand).Should().BeFalse("there is no spectrum yet");
		ViewModel.Chart.TakeManualScale();
		CanExecute(ViewModel.ResetScaleCommand).Should().BeFalse("there is no spectrum yet");

		_source.Current = Atp8000Peak;
		SignalAndRun();
		ViewModel.Chart.ResetScale();
		CanExecute(ViewModel.ResetScaleCommand).Should().BeFalse("the scale follows the data");

		ViewModel.Chart.TakeManualScale();
		CanExecute(ViewModel.ResetScaleCommand).Should().BeTrue();

		await ViewModel.ResetScaleCommand.Execute();

		ViewModel.Chart.IsFollowing.Should().BeTrue();
		CanExecute(ViewModel.ResetScaleCommand).Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task StickToZeroFreezesTheScale()
	{
		_source.Current = Atp8000Peak;
		SignalAndRun();

		await ViewModel.StickToZeroCommand.Execute();

		ViewModel.Chart.IsFollowing.Should().BeFalse();
		ViewModel.Chart.Plot.Axes.GetLimits().Bottom.Should().Be(0);
	}

	[AvaloniaFact]
	public void TheCheckboxesReachTheChart()
	{
		_source.Current = Atp8000Peak;
		SignalAndRun();

		ViewModel.IsLogarithmic = true;
		ViewModel.Chart.Plot.Axes.Left.TickGenerator.Should().BeOfType<LogTickGenerator>();

		ViewModel.IsLogarithmic = false;
		ViewModel.Chart.Plot.Axes.Left.TickGenerator.Should().BeOfType<NumericAutomatic>();

		ViewModel.Chart.TakeManualScale();
		ViewModel.IsInverted = true;
		ViewModel.Chart.IsFollowing.Should().BeTrue("an inversion toggle returns the scale to following");
	}

	[AvaloniaFact]
	public async Task BackToLiveIsEnabledOnlyWhileHistorical()
	{
		var live = Atp2000Peak;
		_source.Current = live;
		SignalAndRun();
		CanExecute(ViewModel.BackToLiveCommand).Should().BeFalse("the live spectrum is shown");

		_switching.ShowMoment(Atp8000Peak.Measurement, _moment);
		Dispatcher.UIThread.RunJobs();
		CanExecute(ViewModel.BackToLiveCommand).Should().BeTrue();
		ViewModel.SignalInfo.Spectrum!.IsHistorical.Should().BeTrue();

		await ViewModel.BackToLiveCommand.Execute();
		Dispatcher.UIThread.RunJobs();

		CanExecute(ViewModel.BackToLiveCommand).Should().BeFalse();
		_switching.ShownMoment.Should().BeNull();
		ViewModel.SignalInfo.Spectrum.Should().BeSameAs(live);
	}

	[AvaloniaFact]
	public void TheHistoricalLineShowsTheMomentInLocalTime()
	{
		ViewModel.Chart.HistoricalText.Should().BeEmpty("the live spectrum is shown");

		_switching.ShowMoment(Atp8000Peak.Measurement, _moment);

		var localTime = _moment.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
		ViewModel.Chart.HistoricalText.Should().Be(Resources.FormatSpectrumHistoricalFormat(localTime));

		_switching.ReturnToLive();

		ViewModel.Chart.HistoricalText.Should().BeEmpty();
	}
}
