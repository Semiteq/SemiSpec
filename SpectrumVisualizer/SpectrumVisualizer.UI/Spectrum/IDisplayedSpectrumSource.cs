using System.Reactive;

using SpectrumVisualizer.Core.Protocol;

namespace SpectrumVisualizer.UI.Spectrum;

/// <summary>The spectrum the chart and the signal pane show, with the time the link received it.</summary>
public sealed record DisplayedSpectrum(SpectrumMeasurement Measurement, DateTimeOffset ReceivedAt);

/// <summary>Where the shown spectrum comes from.</summary>
public interface IDisplayedSpectrumSource
{
	/// <summary>The spectrum to show; null until there is one.</summary>
	DisplayedSpectrum? Current { get; }

	/// <summary>Emits on any thread after Current changes; the reader reads Current after the signal.</summary>
	IObservable<Unit> Changed { get; }
}
