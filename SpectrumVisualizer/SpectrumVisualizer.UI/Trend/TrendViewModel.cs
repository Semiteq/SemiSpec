using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using ReactiveUI;

namespace SpectrumVisualizer.UI.Trend;

/// <summary>The trend section: the chart, the overview, the presets, Follow live and the click to a moment.</summary>
public sealed class TrendViewModel : ReactiveObject, IDisposable
{
	private readonly TrendFeed _feed;
	private readonly ObservableAsPropertyHelper<TrendPreset?> _selectedPreset;
	private readonly CompositeDisposable _disposables = [];

	public TrendViewModel(TrendChartViewModel chart, OverviewViewModel overview, TrendFeed feed)
	{
		Chart = chart;
		Overview = overview;
		_feed = feed;
		Presets = TrendPresets.All;
		_disposables.Add(chart
			.WhenAnyValue(trend => trend.Width)
			.Select(width => Presets.FirstOrDefault(preset => preset.Width == width))
			.ToProperty(this, trend => trend.SelectedPreset, out _selectedPreset));
		_disposables.Add(FollowLiveCommand = ReactiveCommand.Create(
			chart.FollowLive,
			chart.WhenAnyValue(trend => trend.IsFollowing).Select(isFollowing => !isFollowing)));
	}

	public TrendChartViewModel Chart { get; }

	public OverviewViewModel Overview { get; }

	public IReadOnlyList<TrendPreset> Presets { get; }

	/// <summary>
	/// The preset of the chart's width, null after a zoom off every preset; a picked preset sets the width.
	/// </summary>
	public TrendPreset? SelectedPreset
	{
		get => _selectedPreset.Value;
		set
		{
			if (value is not null && value.Width != Chart.Width)
			{
				Chart.SetWidth(value.Width);
			}
		}
	}

	/// <summary>Jumps to now and follows it; enabled only while not following.</summary>
	public ReactiveCommand<Unit, Unit> FollowLiveCommand { get; }

	/// <summary>Shows the spectrum of the clicked moment on the spectrum chart.</summary>
	public void ShowMoment(DateTimeOffset moment)
	{
		_feed.ReadMoment(moment);
	}

	public void Dispose()
	{
		_disposables.Dispose();
	}
}
