using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive;

using ReactiveUI;

namespace SpectrumVisualizer.UI.Messages;

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
	}

	public ReadOnlyObservableCollection<MessageEntry> Entries { get; }

	/// <summary>Whether the panel row is on screen: from the first entry until Clear empties the list.</summary>
	public bool HasEntries => _entries.Count > 0;

	public ReactiveCommand<Unit, Unit> ClearCommand { get; }

	/// <summary>
	/// Adds the failure, or counts it against the newest entry when that entry carries the same view, and
	/// answers which of the two it did.
	/// </summary>
	public bool Report(MessageView view)
	{
		// The operator's zone comes from the clock, not from TimeZoneInfo.Local, so the row a test reads back
		// does not depend on the host running it.
		var seenAt = _timeProvider.GetLocalNow();

		if (Coalesces(view))
		{
			_entries[0].Restamp(seenAt);

			return true;
		}

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
	}

	private bool Coalesces(MessageView view)
	{
		return _entries.Count > 0 && _entries[0].View == view;
	}

	private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs arguments)
	{
		this.RaisePropertyChanged(nameof(HasEntries));
	}
}
