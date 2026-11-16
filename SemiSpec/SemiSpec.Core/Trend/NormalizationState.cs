namespace SemiSpec.Core.Trend;

/// <summary>
/// Whether the trend is normalized, and to which factor; EndedByKeyChange marks the off state a wavelength or delta
/// change caused, as opposed to the operator's own press.
/// </summary>
public sealed record NormalizationState(int? Factor, bool EndedByKeyChange = false)
{
	public static NormalizationState Off { get; } = new(Factor: null);

	public bool IsOn => Factor is not null;
}
