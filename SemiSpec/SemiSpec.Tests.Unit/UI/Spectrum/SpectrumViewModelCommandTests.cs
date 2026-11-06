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
	public async Task AutoscaleTogglesBetweenFollowingAndFrozen()
	{
		_source.Current = Atp8000Peak;
		SignalAndRun();

		await ViewModel.AutoscaleCommand.Execute();
		ViewModel.Chart.IsFollowing.Should().BeFalse("Autoscale off holds the scale where it stands");

		await ViewModel.AutoscaleCommand.Execute();
		ViewModel.Chart.IsFollowing.Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task TypedBoundsFreezeTheScale()
	{
		_source.Current = Atp8000Peak;
		SignalAndRun();
		ViewModel.VerticalForm.Seed();

		ViewModel.VerticalForm.MinimumText = "0";
		ViewModel.VerticalForm.MaximumText = "50000";
		await ViewModel.VerticalForm.CommitCommand.Execute();

		ViewModel.Chart.IsFollowing.Should().BeFalse();
		var limits = ViewModel.Chart.Plot.Axes.GetLimits();
		(limits.Bottom, limits.Top).Should().Be((0.0, 50000.0));
	}

	[AvaloniaFact]
	public async Task FromZeroFollowsFromZero()
	{
		_source.Current = Atp8000Peak;
		SignalAndRun();
		ViewModel.Chart.TakeManualScale();

		await ViewModel.ToggleFromZeroCommand.Execute();

		ViewModel.IsFromZero.Should().BeTrue();
		ViewModel.Chart.IsFollowing.Should().BeTrue("a mode toggle returns the scale to following");
		ViewModel.Chart.Plot.Axes.GetLimits().Bottom.Should().Be(0);
	}

	[AvaloniaFact]
	public async Task TheTogglesReachTheChart()
	{
		_source.Current = Atp8000Peak;
		SignalAndRun();

		await ViewModel.ToggleLogarithmicCommand.Execute();
		ViewModel.Chart.Plot.Axes.Left.TickGenerator.Should().BeOfType<LogTickGenerator>();

		await ViewModel.ToggleLogarithmicCommand.Execute();
		ViewModel.Chart.Plot.Axes.Left.TickGenerator.Should().BeOfType<NumericAutomatic>();

		ViewModel.Chart.TakeManualScale();
		await ViewModel.ToggleInvertCommand.Execute();
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
	public void TheStateLineShowsTheMomentInLocalTime()
	{
		ViewModel.StateText.Should().BeEmpty("nothing is shown yet");

		_switching.ShowMoment(Atp8000Peak.Measurement, _moment);
		Dispatcher.UIThread.RunJobs();

		var localTime = _moment.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
		ViewModel.StateText.Should().Be(Resources.FormatSpectrumHistoricalFormat(localTime));

		_switching.ReturnToLive();
		Dispatcher.UIThread.RunJobs();

		ViewModel.StateText.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task TheStateLineNamesStaleDataAndTheModes()
	{
		_source.Current = Atp8000Peak;
		SignalAndRun();
		await ViewModel.ToggleLogarithmicCommand.Execute();

		var receivedAt = ClockText.TimeOfDay(Atp8000Peak.ReceivedAt);
		ViewModel.StateText.Should().Be(
			Resources.FormatSpectrumStaleFormat(receivedAt) + Resources.ChartStateSeparator + Resources.SpectrumStateLog,
			"the link is not receiving");
	}
}
