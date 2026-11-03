using ReactiveUI;

namespace SpectrumVisualizer.UI.Messages;

/// <summary>
/// One row of the message panel: the mapped failure plus how often and how recently it repeated. The
/// stamp already carries the operator's zone, so the view formats LastSeen as it stands.
/// </summary>
public sealed class MessageEntry : ReactiveObject
{
	public MessageEntry(MessageView view, DateTimeOffset seenAt)
	{
		View = view;
		LastSeen = seenAt;
		RepeatCount = 1;
	}

	public MessageView View { get; }

	public DateTimeOffset LastSeen
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public int RepeatCount
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public bool HasRepeated => RepeatCount > 1;

	/// <summary>Counts one more occurrence of the same failure and moves the last-seen stamp.</summary>
	public void Restamp(DateTimeOffset seenAt)
	{
		RepeatCount++;
		LastSeen = seenAt;
		this.RaisePropertyChanged(nameof(HasRepeated));
	}
}
