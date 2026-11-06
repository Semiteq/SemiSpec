using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive;

using ReactiveUI;

using SemiSpec.UI.Localization;

namespace SemiSpec.UI.Messages;

/// <summary>
/// The one place a failure reaches the operator: a bounded, newest-first list of timestamped entries.
/// </summary>
public sealed class MessagePanelViewModel : ReactiveObject, IDisposable
{
	public const int MaximumEntries = 200;

	private readonly ObservableCollection<MessageEntry> _entries = [];
	private readonly TimeProvider _timeProvider;

	public MessagePanelViewModel(TimeProvider timeProvider)
	{
		_timeProvider = timeProvider;
		Entries = new ReadOnlyObservableCollection<MessageEntry>(_entries);

		_entries.CollectionChanged += OnEntriesChanged;

		ClearCommand = ReactiveCommand.Create(_entries.Clear);
		ToggleCommand = ReactiveCommand.Create(() => { IsVisible = !IsVisible; });
	}

	public ReadOnlyObservableCollection<MessageEntry> Entries { get; }

	public bool HasEntries => _entries.Count > 0;

	/// <summary>
	/// Whether the panel row is on screen, an empty list included. This view model is its only writer, and the View
	/// menu reads back this same flag.
	/// </summary>
	public bool IsVisible
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public MessageEntry? NewestEntry => _entries.Count > 0 ? _entries[0] : null;

	/// <summary>"N more" for the entries under the newest; empty while there are none.</summary>
	public string MoreText => _entries.Count > 1
		? Resources.FormatMessagePanelMoreFormat((_entries.Count - 1).ToString(Resources.Culture))
		: string.Empty;

	public ReactiveCommand<Unit, Unit> ClearCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleCommand { get; }

	/// <summary>
	/// Adds the failure, or counts it against the newest entry when that entry carries the same view, and
	/// answers which of the two it did.
	/// </summary>
	public bool Report(MessageView view)
	{
		var seenAt = _timeProvider.GetLocalNow();

		if (Coalesces(view))
		{
			_entries[0].RecordRepeat(seenAt);

			return true;
		}

		// A new entry opens the row, so nothing new lands off screen; a repeat of the entry on top does not, so a
		// panel closed during an outage stays closed.
		IsVisible = true;

		_entries.Insert(0, new MessageEntry(view, seenAt));

		while (_entries.Count > MaximumEntries)
		{
			_entries.RemoveAt(_entries.Count - 1);
		}

		return false;
	}

	public void Dispose()
	{
		ClearCommand.Dispose();
		ToggleCommand.Dispose();
	}

	private bool Coalesces(MessageView view)
	{
		return _entries.Count > 0 && _entries[0].View == view;
	}

	private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs arguments)
	{
		this.RaisePropertyChanged(nameof(HasEntries));
		this.RaisePropertyChanged(nameof(NewestEntry));
		this.RaisePropertyChanged(nameof(MoreText));
	}
}
