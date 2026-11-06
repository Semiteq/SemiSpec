using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ReactiveUI;

using SemiSpec.UI.Chart;
using SemiSpec.UI.Localization;

namespace SemiSpec.UI.Trend;

/// <summary>
/// The time axis flyout: the window by its local start and end or by its width, and the presets; ported from
/// SemiPlot's time window panel without its archive calendar.
/// </summary>
public sealed class TimeWindowPanelViewModel : ReactiveObject, IAxisFlyoutForm, IDisposable
{
	private const string ClockFormat = "HH:mm:ss";

	private readonly TrendChartViewModel _chart;
	private readonly Subject<Unit> _closeRequests = new();
	private DateTime _seededStart;
	private DateTime _seededEnd;
	private TimeSpan _seededWidth;
	private bool _isCancelled;

	public TimeWindowPanelViewModel(TrendChartViewModel chart)
	{
		_chart = chart;
		CommitCommand = ReactiveCommand.Create(Commit);
		CancelCommand = ReactiveCommand.Create(Cancel);
		SetWidthCommand = ReactiveCommand.Create<TrendPreset>(SetWidth);
	}

	public IObservable<Unit> CloseRequests => _closeRequests.AsObservable();

	public IReadOnlyList<TrendPreset> Presets { get; } = TrendPresets.All;

	public string StartTimeText
	{
		get;
		set => this.RaiseAndSetIfChanged(ref field, value);
	} = string.Empty;

	public DateTime? StartDate
	{
		get;
		set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public string EndTimeText
	{
		get;
		set => this.RaiseAndSetIfChanged(ref field, value);
	} = string.Empty;

	public DateTime? EndDate
	{
		get;
		set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public string WidthText
	{
		get;
		set => this.RaiseAndSetIfChanged(ref field, value);
	} = string.Empty;

	/// <summary>Why the last commit wrote nothing; empty after a seed.</summary>
	public string Message
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = string.Empty;

	public ReactiveCommand<Unit, Unit> CommitCommand { get; }

	public ReactiveCommand<Unit, Unit> CancelCommand { get; }

	public ReactiveCommand<TrendPreset, Unit> SetWidthCommand { get; }

	public void Seed()
	{
		_isCancelled = false;
		_seededStart = _chart.From.ToLocalTime().DateTime;
		_seededEnd = _chart.To.ToLocalTime().DateTime;
		_seededWidth = _chart.Width;
		StartTimeText = _seededStart.ToString(ClockFormat, CultureInfo.InvariantCulture);
		StartDate = _seededStart.Date;
		EndTimeText = _seededEnd.ToString(ClockFormat, CultureInfo.InvariantCulture);
		EndDate = _seededEnd.Date;
		WidthText = WidthTextOf(_seededWidth);
		Message = string.Empty;
	}

	/// <summary>Typed edges show that exact window and stop following; a typed width keeps the right edge.</summary>
	public void Commit()
	{
		if (!TryReadEdge(StartTimeText, StartDate, out var start))
		{
			Message = Resources.FormatTimeWindowStartUnreadableFormat(Resources.TimeWindowClockPattern);
			return;
		}

		if (!TryReadEdge(EndTimeText, EndDate, out var end))
		{
			Message = Resources.FormatTimeWindowEndUnreadableFormat(Resources.TimeWindowClockPattern);
			return;
		}

		if (!TryReadWidth(WidthText, out var width))
		{
			Message = Resources.FormatTimeWindowWidthUnreadableFormat(Resources.TimeWindowClockPattern);
			return;
		}

		var edgesChanged = !IsSeededSecond(start, _seededStart) || !IsSeededSecond(end, _seededEnd);
		if (edgesChanged)
		{
			if (end <= start)
			{
				Message = Resources.TimeWindowEndNotAfterStart;
				return;
			}

			_chart.SetWindow(new DateTimeOffset(start), new DateTimeOffset(end));
			Seed();
			return;
		}

		if (Math.Abs((width - _seededWidth).TotalSeconds) >= 1.0)
		{
			_chart.SetWidth(width);
			Seed();
		}
	}

	public void Close()
	{
		if (!_isCancelled)
		{
			Commit();
		}
	}

	public void Dispose()
	{
		CommitCommand.Dispose();
		CancelCommand.Dispose();
		SetWidthCommand.Dispose();
		_closeRequests.Dispose();
	}

	private static string WidthTextOf(TimeSpan width)
	{
		return string.Create(
			CultureInfo.InvariantCulture,
			$"{(int)width.TotalHours:00}:{width.Minutes:00}:{width.Seconds:00}");
	}

	private static bool TryReadEdge(string timeText, DateTime? date, out DateTime edge)
	{
		edge = default;
		if (date is not { } day
			|| !TimeOnly.TryParseExact(timeText.Trim(), ClockFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
		{
			return false;
		}

		edge = day.Date + time.ToTimeSpan();
		return true;
	}

	private static bool TryReadWidth(string text, out TimeSpan width)
	{
		width = default;
		var parts = text.Trim().Split(':');
		if (parts.Length != 3
			|| !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
			|| !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
			|| !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
			|| minutes > 59 || seconds > 59)
		{
			return false;
		}

		width = new TimeSpan(hours, minutes, seconds);
		return true;
	}

	/// <summary>The fields show whole seconds, so an untouched edge equals its seed to the second.</summary>
	private static bool IsSeededSecond(DateTime typed, DateTime seeded)
	{
		return Math.Abs((typed - seeded).TotalSeconds) < 1.0;
	}

	private void SetWidth(TrendPreset preset)
	{
		_chart.SetWidth(preset.Width);
		Seed();
	}

	private void Cancel()
	{
		_isCancelled = true;
		_closeRequests.OnNext(Unit.Default);
	}
}
