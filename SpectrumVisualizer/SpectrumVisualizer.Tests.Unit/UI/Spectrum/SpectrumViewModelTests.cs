using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Input;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using ReactiveUI.Avalonia;

using ScottPlot.TickGenerators;

using SpectrumVisualizer.Tests.Unit.UI.Messages;
using SpectrumVisualizer.UI.Chart;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Messages;
using SpectrumVisualizer.UI.Spectrum;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.UI.Spectrum.SpectrumSamples;

using RxUnit = System.Reactive.Unit;

namespace SpectrumVisualizer.Tests.Unit.UI.Spectrum;

[Trait("Component", "UI")]
[Trait("Area", "Spectrum")]
[Trait("Category", "Unit")]
public sealed class SpectrumViewModelTests : IDisposable
{
	private static readonly DateTimeOffset _moment = new(2026, 11, 4, 8, 30, 15, TimeSpan.Zero);

	private readonly FakeDisplayedSpectrumSource _source = new();
	private readonly SwitchingSpectrumSource _switching;
	private readonly MessagePanelViewModel _messagePanel = new(TimeProvider.System);
	private readonly FakeLogger<SpectrumViewModel> _logger = new();
	private SpectrumViewModel? _viewModel;

	public SpectrumViewModelTests()
	{
		_switching = new SwitchingSpectrumSource(_source);
	}

	private SpectrumViewModel ViewModel => _viewModel ??= NewViewModel(_switching);

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
		ViewModel.HistoricalText.Should().BeEmpty("the live spectrum is shown");

		_switching.ShowMoment(Atp8000Peak.Measurement, _moment);

		var localTime = _moment.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
		ViewModel.HistoricalText.Should().Be(Resources.FormatSpectrumHistoricalFormat(localTime));

		_switching.ReturnToLive();

		ViewModel.HistoricalText.Should().BeEmpty();
	}

	public void Dispose()
	{
		_viewModel?.Dispose();
		_switching.Dispose();
		_source.Dispose();
		_messagePanel.Dispose();
	}

	private static bool CanExecute(ICommand command)
	{
		return command.CanExecute(null);
	}

	private SpectrumViewModel NewViewModel(SwitchingSpectrumSource source)
	{
		return new SpectrumViewModel(source, _messagePanel, TimeProvider.System, AvaloniaScheduler.Instance, _logger);
	}

	private Func<int> CountRedraws()
	{
		var redraws = 0;
		ViewModel.Chart.RedrawRequested.Subscribe(_ => redraws++);
		return () => redraws;
	}

	private void SignalAndRun()
	{
		_ = ViewModel;
		_source.Signal();
		Dispatcher.UIThread.RunJobs();
	}

	private void ShouldHaveReportedOneError()
	{
		_messagePanel.Entries.Should().ContainSingle();
		_messagePanel.Entries[0].View.Severity.Should().Be(MessageSeverity.Error);
		_messagePanel.Entries[0].View.Detail.Should().Contain(nameof(InvalidOperationException));
		_logger.Levels().Should().Equal(LogLevel.Error);
	}

	/// <summary>A source whose Current throws while Fails is set and returns Spectrum otherwise.</summary>
	private sealed class FailingSpectrumSource : IDisplayedSpectrumSource, IDisposable
	{
		private readonly Subject<RxUnit> _changed = new();

		public int Reads { get; private set; }

		public bool Fails { get; set; }

		public DisplayedSpectrum? Spectrum { get; set; }

		public DisplayedSpectrum? Current
		{
			get
			{
				Reads++;
				return Fails ? throw new InvalidOperationException("the source failed") : Spectrum;
			}
		}

		public IObservable<RxUnit> Changed => _changed.AsObservable();

		public void Signal()
		{
			_changed.OnNext(RxUnit.Default);
		}

		public void Dispose()
		{
			_changed.Dispose();
		}
	}
}
