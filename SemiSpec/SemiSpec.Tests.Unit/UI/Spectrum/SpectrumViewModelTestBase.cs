using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Input;

using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using ReactiveUI.Avalonia;

using SemiSpec.Tests.Unit.UI.Messages;
using SemiSpec.UI.Messages;
using SemiSpec.UI.Spectrum;

using RxUnit = System.Reactive.Unit;

namespace SemiSpec.Tests.Unit.UI.Spectrum;

public abstract class SpectrumViewModelTestBase : IDisposable
{
	private protected readonly FakeDisplayedSpectrumSource _source = new();
	private protected readonly SwitchingSpectrumSource _switching;
	private protected readonly MessagePanelViewModel _messagePanel = new(TimeProvider.System);
	private protected readonly FakeLogger<SpectrumViewModel> _logger = new();
	private protected SpectrumViewModel? _viewModel;

	protected SpectrumViewModelTestBase()
	{
		_switching = new SwitchingSpectrumSource(_source);
	}

	private protected SpectrumViewModel ViewModel => _viewModel ??= NewViewModel(_switching);

	public void Dispose()
	{
		_viewModel?.Dispose();
		_switching.Dispose();
		_source.Dispose();
		_messagePanel.Dispose();
		GC.SuppressFinalize(this);
	}

	private protected static bool CanExecute(ICommand command)
	{
		return command.CanExecute(null);
	}

	private protected SpectrumViewModel NewViewModel(SwitchingSpectrumSource source)
	{
		return new SpectrumViewModel(source, _messagePanel, TimeProvider.System, AvaloniaScheduler.Instance, _logger);
	}

	private protected Func<int> CountRedraws()
	{
		var redraws = 0;
		ViewModel.Chart.RedrawRequested.Subscribe(_ => redraws++);
		return () => redraws;
	}

	private protected void SignalAndRun()
	{
		_ = ViewModel;
		_source.Signal();
		Dispatcher.UIThread.RunJobs();
	}

	private protected void ShouldHaveReportedOneError()
	{
		_messagePanel.Entries.Should().ContainSingle();
		_messagePanel.Entries[0].View.Severity.Should().Be(MessageSeverity.Error);
		_messagePanel.Entries[0].View.Detail.Should().Contain(nameof(InvalidOperationException));
		_logger.Levels().Should().Equal(LogLevel.Error);
	}

	/// <summary>A source whose Current throws while Fails is set and returns Spectrum otherwise.</summary>
	private protected sealed class FailingSpectrumSource : IDisplayedSpectrumSource, IDisposable
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

		public IObservable<RxUnit> CurrentChanged => _changed.AsObservable();

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
