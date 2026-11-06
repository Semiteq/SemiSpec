using SemiSpec.Core.Protocol;
using SemiSpec.UI.Link;

namespace SemiSpec.UI.Spectrum;

/// <summary>Whether the shown live spectrum is old: the link is not receiving, or no spectrum came for 10 s.</summary>
public static class SpectrumStaleness
{
	/// <summary>False before the first live spectrum, so an empty chart is never stale.</summary>
	public static bool IsStale(LinkState state, DateTimeOffset? lastLiveSpectrumAt, DateTimeOffset now)
	{
		if (lastLiveSpectrumAt is not { } receivedAt)
		{
			return false;
		}

		return state != LinkState.Receiving || now - receivedAt > LinkTiming.SilenceLimit;
	}
}
