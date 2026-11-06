using System.Reactive;

using SemiSpec.Core.Protocol;

namespace SemiSpec.UI.Spectrum;

/// <summary>
/// The spectrum the chart and the signal pane show, with the time the link received it; historical when read back for
/// a past moment.
/// </summary>
public sealed record DisplayedSpectrum(
	SpectrumMeasurement Measurement,
	DateTimeOffset ReceivedAt,
	bool IsHistorical = false);

/// <summary>Where the shown spectrum comes from.</summary>
public interface IDisplayedSpectrumSource
{
	/// <summary>The spectrum to show; null until there is one. One spectrum keeps one measurement instance.</summary>
	DisplayedSpectrum? Current { get; }

	/// <summary>Emits on any thread after Current changes; the reader reads Current after the signal.</summary>
	IObservable<Unit> CurrentChanged { get; }
}
