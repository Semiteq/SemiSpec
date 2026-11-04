using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using SpectrumVisualizer.UI.Spectrum;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.UI.Spectrum.SpectrumSamples;

namespace SpectrumVisualizer.Tests.Unit.UI.Spectrum;

[Trait("Component", "UI")]
[Trait("Area", "Spectrum")]
[Trait("Category", "Unit")]
public sealed class SpectrumViewModelSignalTests : SpectrumViewModelTestBase
{
	[AvaloniaFact]
	public void ThreeQueuedSignalsApplyTheLatestSpectrumOnce()
	{
		var redraws = CountRedraws();
		var latest = Atp8000Peak;
		_source.Current = Atp2000Peak;
		using var signalled = new ManualResetEventSlim();

		// A blocking wait, not an await: an await lets the dispatcher run the first signal before the last one.
		_ = Task.Run(() =>
		{
			_source.Signal();
			_source.Signal();
			_source.Current = latest;
			_source.Signal();
			signalled.Set();
		});
		signalled.Wait(Waits.Limit).Should().BeTrue("a throw in the signalling task never sets the event");
		Dispatcher.UIThread.RunJobs();

		redraws().Should().Be(1);
		ViewModel.SignalInfo.Spectrum.Should().BeSameAs(latest);
	}

	[AvaloniaFact]
	public void TheSameMeasurementIsAppliedOnceByReference()
	{
		var redraws = CountRedraws();
		var shown = Atp8000Peak;
		var sameMeasurement = shown with { ReceivedAt = shown.ReceivedAt.AddSeconds(1) };
		var equalCopy = Atp8000Peak;
		equalCopy.Measurement.Should().Be(shown.Measurement).And.NotBeSameAs(shown.Measurement);

		_source.Current = shown;
		SignalAndRun();
		_source.Current = sameMeasurement;
		SignalAndRun();
		redraws().Should().Be(1, "a new record around the same measurement is the same spectrum");

		_source.Current = equalCopy;
		SignalAndRun();
		redraws().Should().Be(2);
		ViewModel.SignalInfo.Spectrum.Should().BeSameAs(equalCopy);
	}

	[AvaloniaFact]
	public void ASpectrumPresentAtConstructionShowsAtOnce()
	{
		var present = Atp2000Peak;
		_source.Current = present;

		ViewModel.Chart.HasSpectrum.Should().BeTrue();
		ViewModel.SignalInfo.Spectrum.Should().BeSameAs(present);
	}

	[AvaloniaFact]
	public void AThrowingShowReportsOnceAndTheNextSpectrumStillShows()
	{
		var throwOnce = true;
		ViewModel.Chart.RedrawRequested.Subscribe(_ =>
		{
			if (throwOnce)
			{
				throwOnce = false;
				throw new InvalidOperationException("the chart failed");
			}
		});

		_source.Current = Atp2000Peak;
		SignalAndRun();
		var next = Atp8000Peak;
		_source.Current = next;
		SignalAndRun();

		ShouldHaveReportedOneError();
		ViewModel.SignalInfo.Spectrum.Should().BeSameAs(next);
	}

	[AvaloniaFact]
	public void AThrowingCurrentOnALaterSignalReportsAndThePipelineGoesOn()
	{
		using var failing = new FailingSpectrumSource();
		using var switching = new SwitchingSpectrumSource(failing);
		_viewModel = NewViewModel(switching);
		Dispatcher.UIThread.RunJobs();

		failing.Fails = true;
		failing.Signal();
		Dispatcher.UIThread.RunJobs();
		var next = Atp8000Peak;
		failing.Fails = false;
		failing.Spectrum = next;
		failing.Signal();
		Dispatcher.UIThread.RunJobs();

		ShouldHaveReportedOneError();
		failing.Reads.Should().Be(3, "the failed read leaves the subscription alive");
		ViewModel.SignalInfo.Spectrum.Should().BeSameAs(next);
	}

	[AvaloniaFact]
	public void DisposeEndsTheChangedSubscription()
	{
		_ = ViewModel;
		_source.HasSubscribers.Should().BeTrue();

		ViewModel.Dispose();

		_source.HasSubscribers.Should().BeFalse();
	}
}
