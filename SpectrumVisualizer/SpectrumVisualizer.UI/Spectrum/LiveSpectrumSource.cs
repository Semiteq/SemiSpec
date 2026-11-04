using System.Reactive;
using System.Reactive.Linq;

using SpectrumVisualizer.UI.Link;

namespace SpectrumVisualizer.UI.Spectrum;

/// <summary>Shows the latest spectrum frame the distributor has read.</summary>
public sealed class LiveSpectrumSource(FrameDistributor frameDistributor) : IDisplayedSpectrumSource
{
	/// <summary>A new record on every read; the measurement is the frame's own, so a reader compares it by reference.</summary>
	public DisplayedSpectrum? Current => frameDistributor.LatestSpectrum is { Frame.Measurement: { } measurement } latest
		? new DisplayedSpectrum(measurement, latest.ReceivedAt)
		: null;

	public IObservable<Unit> Changed { get; } = frameDistributor.Frames
		.Where(received => received.Frame.Measurement is not null)
		.Select(_ => Unit.Default);
}
