using Avalonia.Controls;

namespace SemiSpec.UI.Trend;

/// <summary>
/// A radio button that never checks itself: a click only runs its command, and IsChecked shows the bound state, so a
/// press the state's owner refuses leaves the segment as it was.
/// </summary>
public sealed class MirroredRadioButton : RadioButton
{
	protected override Type StyleKeyOverride => typeof(RadioButton);

	protected override void Toggle()
	{
	}
}
