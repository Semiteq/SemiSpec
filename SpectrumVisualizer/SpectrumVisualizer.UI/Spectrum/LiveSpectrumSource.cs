using System.Reactive;
using System.Reactive.Linq;

using SpectrumVisualizer.UI.Link;

namespace SpectrumVisualizer.UI.Spectrum;

/// <summary>Shows the latest spectrum frame the distributor has read.</summary>
public sealed class LiveSpectrumSource(FrameDistributor frameDistributor) : IDisplayedSpectrumSource
{
	private readonly Lock _cacheGate = new();

	// Read and replaced under _cacheGate, with Current's backing field, by any thread that reads Current.
	private ReceivedFrame? _cachedFrame;

	/// <summary>The latest spectrum frame; the same instance on every read until the next spectrum frame.</summary>
	public DisplayedSpectrum? Current
	{
		get
		{
			var latest = frameDistributor.LatestSpectrum;
			if (latest?.Frame.Measurement is not { } measurement)
			{
				return null;
			}

			lock (_cacheGate)
			{
				if (!ReferenceEquals(latest, _cachedFrame))
				{
					_cachedFrame = latest;
					field = new DisplayedSpectrum(measurement, latest.ReceivedAt);
				}

				return field;
			}
		}
	}

	public IObservable<Unit> Changed { get; } = frameDistributor.Frames
		.Where(received => received.Frame.Measurement is not null)
		.Select(_ => Unit.Default);
}
