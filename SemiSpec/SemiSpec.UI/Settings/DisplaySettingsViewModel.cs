using System.Reactive;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiSpec.UI.Messages;

namespace SemiSpec.UI.Settings;

/// <summary>
/// The one writer of the display choices: loads them at construction, and each toggle saves the file; a failed read or
/// write reaches the message panel and the choice still holds for the run.
/// </summary>
public sealed class DisplaySettingsViewModel : ReactiveObject, IDisposable
{
	private readonly SettingsFile _file;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly ILogger<DisplaySettingsViewModel> _logger;
	private DisplaySettings _current;

	public DisplaySettingsViewModel(
		SettingsFile file, MessagePanelViewModel messagePanel, ILogger<DisplaySettingsViewModel> logger)
	{
		_file = file;
		_messagePanel = messagePanel;
		_logger = logger;

		var loaded = file.Load();
		if (loaded.IsFailed)
		{
			messagePanel.ReportFailure(loaded.Errors[0], logger);
		}

		_current = loaded.ValueOrDefault ?? DisplaySettings.Default;
		ToggleQFactorCommand = ReactiveCommand.Create(
			() => Apply(_current with { ShowQFactor = !_current.ShowQFactor }));
		ToggleTrendAsLinesCommand = ReactiveCommand.Create(
			() => Apply(_current with { TrendAsLines = !_current.TrendAsLines }));
	}

	public bool ShowQFactor => _current.ShowQFactor;

	public bool TrendAsLines => _current.TrendAsLines;

	public ReactiveCommand<Unit, Unit> ToggleQFactorCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleTrendAsLinesCommand { get; }

	public void Dispose()
	{
		ToggleQFactorCommand.Dispose();
		ToggleTrendAsLinesCommand.Dispose();
	}

	private void Apply(DisplaySettings next)
	{
		var previous = _current;
		_current = next;
		if (previous.ShowQFactor != next.ShowQFactor)
		{
			this.RaisePropertyChanged(nameof(ShowQFactor));
		}

		if (previous.TrendAsLines != next.TrendAsLines)
		{
			this.RaisePropertyChanged(nameof(TrendAsLines));
		}

		var saved = _file.Save(next);
		if (saved.IsFailed)
		{
			_messagePanel.ReportFailure(saved.Errors[0], _logger);
		}
	}
}
