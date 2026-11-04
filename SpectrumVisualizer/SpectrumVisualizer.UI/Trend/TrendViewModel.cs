using System.Reactive;
using System.Reactive.Linq;

using ReactiveUI;

namespace SpectrumVisualizer.UI.Trend;

/// <summary>The trend section: the chart, the overview, the presets, Follow live and the click to a moment.</summary>
public sealed class TrendViewModel : ReactiveObject, IDisposable
{
	private readonly TrendFeed _feed;
	private TrendPreset _selectedPreset;

	public TrendViewModel(TrendChartViewModel chart, OverviewViewModel overview, TrendFeed feed)
	{
		Chart = chart;
		Overview = overview;
		_feed = feed;
		Presets = TrendPresets.All;
		_selectedPreset = Presets.First(preset => preset.Width == TrendPresets.Default.Width);
		FollowLiveCommand = ReactiveCommand.Create(
			chart.FollowLive,
			chart.WhenAnyValue(trend => trend.IsFollowing).Select(isFollowing => !isFollowing));
	}

	public TrendChartViewModel Chart { get; }

	public OverviewViewModel Overview { get; }

	public IReadOnlyList<TrendPreset> Presets { get; }

	/// <summary>A new preset sets the chart's width and keeps its right edge and following.</summary>
	public TrendPreset SelectedPreset
	{
		get => _selectedPreset;
		set
		{
			if (value is null || value == _selectedPreset)
			{
				return;
			}

			this.RaiseAndSetIfChanged(ref _selectedPreset, value);
			Chart.SetWidth(value.Width);
		}
	}

	/// <summary>Jumps to the newest point and follows it; enabled only while not following.</summary>
	public ReactiveCommand<Unit, Unit> FollowLiveCommand { get; }

	/// <summary>Shows the spectrum of the clicked moment on the spectrum chart.</summary>
	public void ShowMoment(DateTimeOffset moment)
	{
		_feed.ReadMoment(moment);
	}

	public void Dispose()
	{
		FollowLiveCommand.Dispose();
	}
}
