namespace SemiSpec.UI.Settings;

/// <summary>The display choices the operator keeps across restarts.</summary>
public sealed record DisplaySettings(bool ShowQFactor, bool TrendAsLines)
{
	/// <summary>Q factor hidden, trend drawn as dots.</summary>
	public static DisplaySettings Default { get; } = new(ShowQFactor: false, TrendAsLines: false);
}
