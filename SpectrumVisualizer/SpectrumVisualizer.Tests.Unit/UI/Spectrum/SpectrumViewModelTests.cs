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
	private readonly FakeDisplayedSpectrumSource _source = new();
	private readonly MessagePanelViewModel _messagePanel = new(TimeProvider.System);
	private readonly FakeLogger<SpectrumViewModel> _logger = new();
	private SpectrumViewModel? _viewModel;

	private SpectrumViewModel ViewModel => _viewModel ??= NewViewModel(_source);

	[AvaloniaFact]
	public async Task ThreeQueuedSignalsApplyTheLatestSpectrumOnce()
	{
		var redraws = CountRedraws();
		var latest = Atp8000Peak;
		_source.Current = Atp2000Peak;

		await Task.Run(() =>
		{
			_source.Signal();
			_source.Signal();
			_source.Current = latest;
			_source.Signal();
		});
		Dispatcher.UIThread.RunJobs();

		redraws().Should().Be(1);
		ViewModel.SignalInfo.Spectrum.Should().BeSameAs(latest);
	}

	[AvaloniaFact]
	public void TheSameCachedSpectrumIsAppliedOnceByReference()
	{
		var redraws = CountRedraws();
		var cached = Atp8000Peak;
		var equalCopy = Atp8000Peak;
		equalCopy.Should().Be(cached).And.NotBeSameAs(cached);

		_source.Current = cached;
		SignalAndRun();
		SignalAndRun();
		redraws().Should().Be(1);

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
	public void AThrowingCurrentAddsOneErrorEntryAndEndsTheSubscription()
	{
		using var throwing = new ThrowingSpectrumSource();
		_viewModel = NewViewModel(throwing);
		var redraws = CountRedraws();

		Dispatcher.UIThread.RunJobs();
		throwing.Signal();
		Dispatcher.UIThread.RunJobs();

		_messagePanel.Entries.Should().ContainSingle();
		_messagePanel.Entries[0].View.Severity.Should().Be(MessageSeverity.Error);
		_messagePanel.Entries[0].View.Detail.Should().Contain(nameof(InvalidOperationException));
		_logger.Levels().Should().Equal(LogLevel.Error);
		throwing.Reads.Should().Be(1, "the failure ends the subscription");
		redraws().Should().Be(0);
	}

	public void Dispose()
	{
		_viewModel?.Dispose();
		_source.Dispose();
		_messagePanel.Dispose();
	}

	private static bool CanExecute(ICommand command)
	{
		return command.CanExecute(null);
	}

	private SpectrumViewModel NewViewModel(IDisplayedSpectrumSource source)
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

	/// <summary>A source whose Current throws on every read.</summary>
	private sealed class ThrowingSpectrumSource : IDisplayedSpectrumSource, IDisposable
	{
		private readonly Subject<RxUnit> _changed = new();

		public int Reads { get; private set; }

		public DisplayedSpectrum? Current
		{
			get
			{
				Reads++;
				throw new InvalidOperationException("the source failed");
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
