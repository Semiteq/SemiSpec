using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ReactiveUI;

using SemiSpec.Core.Protocol;

namespace SemiSpec.UI.Spectrum;

/// <summary>
/// Shows the live source's spectrum, or a spectrum of a past moment until the operator returns to live.
/// </summary>
public sealed class SwitchingSpectrumSource : ReactiveObject, IDisplayedSpectrumSource, IDisposable
{
	private readonly IDisplayedSpectrumSource _liveSource;
	private readonly Subject<Unit> _modeChanges = new();
	private DisplayedSpectrum? _historical;

	public SwitchingSpectrumSource(IDisplayedSpectrumSource liveSource)
	{
		_liveSource = liveSource;
		CurrentChanged = liveSource.CurrentChanged.Merge(_modeChanges.AsObservable());
	}

	/// <summary>
	/// The historical spectrum while one is shown, the same record on every read; the live one otherwise.
	/// </summary>
	public DisplayedSpectrum? Current => _historical ?? _liveSource.Current;

	/// <summary>The live source's signal merged with one signal per switch between live and a past moment.</summary>
	public IObservable<Unit> CurrentChanged { get; }

	/// <summary>The receive time of the shown historical spectrum; null while live.</summary>
	public DateTimeOffset? ShownMoment
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>
	/// Shows the spectrum of a past moment on the UI thread; a new measurement around the same pixels, so a reader that
	/// compares by reference shows every moment, also the same one twice.
	/// </summary>
	public void ShowMoment(SpectrumMeasurement measurement, DateTimeOffset receivedAt)
	{
		_historical = new DisplayedSpectrum(measurement with { }, receivedAt, IsHistorical: true);
		ShownMoment = receivedAt;
		_modeChanges.OnNext(Unit.Default);
	}

	/// <summary>Returns to the live spectrum on the UI thread; no-op while live.</summary>
	public void ReturnToLive()
	{
		if (_historical is null)
		{
			return;
		}

		_historical = null;
		ShownMoment = null;
		_modeChanges.OnNext(Unit.Default);
	}

	public void Dispose()
	{
		_modeChanges.Dispose();
	}
}
