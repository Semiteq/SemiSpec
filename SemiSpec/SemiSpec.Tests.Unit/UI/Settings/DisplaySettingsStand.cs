using Microsoft.Extensions.Logging.Abstractions;

using SemiSpec.UI.Messages;
using SemiSpec.UI.Settings;

namespace SemiSpec.Tests.Unit.UI.Settings;

/// <summary>Display settings over a file in a temp folder the stand owns and deletes on dispose.</summary>
internal sealed class DisplaySettingsStand : IDisposable
{
	private readonly TempFolder _folder = new();

	public DisplaySettingsStand(MessagePanelViewModel messagePanel)
	{
		Display = new DisplaySettingsViewModel(
			new SettingsFile(_folder.FileIn(SettingsFile.FileName)),
			messagePanel,
			NullLogger<DisplaySettingsViewModel>.Instance);
	}

	public DisplaySettingsViewModel Display { get; }

	public void Dispose()
	{
		Display.Dispose();
		_folder.Dispose();
	}
}
