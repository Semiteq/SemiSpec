using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using ReactiveUI;

using SemiSpec.UI.Chart;
using SemiSpec.UI.Localization;

namespace SemiSpec.UI.Trend;

/// <summary>
/// The trend: the chart, the overview, the axis flyouts' forms, From zero, Follow live and the click to a moment.
/// </summary>
public sealed class TrendViewModel : ReactiveObject, IDisposable
{
	private readonly TrendFeed _feed;
	private readonly ObservableAsPropertyHelper<string> _stateText;
	private readonly CompositeDisposable _disposables = [];

	public TrendViewModel(TrendChartViewModel chart, OverviewViewModel overview, TrendFeed feed)
	{
		Chart = chart;
		Overview = overview;
		_feed = feed;
		_disposables.Add(TimeWindow = new TimeWindowPanelViewModel(chart));
		_disposables.Add(VerticalForm = new AxisRangeForm(chart.VerticalRange, chart.SetVerticalRange));

		_disposables.Add(chart
			.WhenAnyValue(
				trend => trend.Width,
				trend => trend.IsFollowing,
				trend => trend.IsVerticalAutoscaled,
				StateTextOf)
			.ToProperty(this, trend => trend.StateText, out _stateText));
		_disposables.Add(FollowLiveCommand = ReactiveCommand.Create(
			chart.FollowLive,
			chart.WhenAnyValue(trend => trend.IsFollowing).Select(isFollowing => !isFollowing)));
		_disposables.Add(ToggleFromZeroCommand = ReactiveCommand.Create(() =>
		{
			chart.SetFromZero(!chart.IsFromZero);
			VerticalForm.Seed();
		}));
		_disposables.Add(VerticalAutoscaleCommand = ReactiveCommand.Create(() =>
		{
			chart.ToggleVerticalAutoscale();
			VerticalForm.Seed();
		}));
	}

	public TrendChartViewModel Chart { get; }

	public OverviewViewModel Overview { get; }

	/// <summary>The time axis flyout.</summary>
	public TimeWindowPanelViewModel TimeWindow { get; }

	/// <summary>The intensity axis flyout's Maximum and Minimum.</summary>
	public AxisRangeForm VerticalForm { get; }

	/// <summary>Y follows the data again, or holds where it stands while it follows.</summary>
	public ReactiveCommand<Unit, Unit> VerticalAutoscaleCommand { get; }

	/// <summary>The window width, then "Not following" and "Scale frozen" while they apply.</summary>
	public string StateText => _stateText.Value;

	/// <summary>Jumps to now and follows it; enabled only while not following.</summary>
	public ReactiveCommand<Unit, Unit> FollowLiveCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleFromZeroCommand { get; }

	/// <summary>Shows the spectrum of the clicked moment on the spectrum chart.</summary>
	public void ShowMoment(DateTimeOffset moment)
	{
		_feed.ReadMoment(moment);
	}

	public void Dispose()
	{
		_disposables.Dispose();
	}

	/// <summary>A preset's own label, or hours, minutes and seconds of an off-preset width.</summary>
	public static string WidthText(TimeSpan width)
	{
		if (TrendPresets.All.FirstOrDefault(preset => preset.Width == width) is { } preset)
		{
			return preset.Label;
		}

		var culture = Resources.Culture;
		var parts = new List<string>();
		if (width.Hours > 0 || width.Days > 0)
		{
			parts.Add(Resources.FormatTrendPresetHoursFormat(((int)width.TotalHours).ToString(culture)));
		}

		if (width.Minutes > 0)
		{
			parts.Add(Resources.FormatTrendPresetMinutesFormat(width.Minutes.ToString(culture)));
		}

		if (width.TotalHours < 1.0 && (width.Seconds > 0 || parts.Count == 0))
		{
			parts.Add(Resources.FormatTrendWidthSecondsFormat(width.Seconds.ToString(culture)));
		}

		return string.Join(" ", parts);
	}

	private static string StateTextOf(TimeSpan width, bool isFollowing, bool isVerticalAutoscaled)
	{
		List<string> parts = [WidthText(width)];
		if (!isFollowing)
		{
			parts.Add(Resources.TrendStateNotFollowing);
		}

		if (!isVerticalAutoscaled)
		{
			parts.Add(Resources.SpectrumStateFrozen);
		}

		return string.Join(Resources.ChartStateSeparator, parts);
	}
}
