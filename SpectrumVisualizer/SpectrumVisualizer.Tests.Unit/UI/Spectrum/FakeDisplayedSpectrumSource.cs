using System.Reactive.Linq;
using System.Reactive.Subjects;

using SpectrumVisualizer.UI.Spectrum;

using RxUnit = System.Reactive.Unit;

namespace SpectrumVisualizer.Tests.Unit.UI.Spectrum;

/// <summary>A source whose Current the test sets and whose Changed emits on each Signal call.</summary>
internal sealed class FakeDisplayedSpectrumSource : IDisplayedSpectrumSource, IDisposable
{
	private readonly Subject<RxUnit> _changed = new();

	public DisplayedSpectrum? Current { get; set; }

	public IObservable<RxUnit> Changed => _changed.AsObservable();

	public bool HasSubscribers => _changed.HasObservers;

	public void Signal()
	{
		_changed.OnNext(RxUnit.Default);
	}

	public void Dispose()
	{
		_changed.Dispose();
	}
}
