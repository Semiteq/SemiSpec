namespace SemiSpec.Core.Protocol;

/// <summary>The link's time limits shared by the link health rules and the trend's continuity rule.</summary>
public static class LinkTiming
{
	/// <summary>A link without a valid frame this long reports a silent board, per OptoskyProj#20.</summary>
	public static readonly TimeSpan SilenceLimit = TimeSpan.FromSeconds(10);
}
