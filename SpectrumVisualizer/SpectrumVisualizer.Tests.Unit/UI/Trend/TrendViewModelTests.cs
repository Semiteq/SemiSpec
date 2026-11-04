using System.Reactive.Linq;
using System.Windows.Input;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging;

using SpectrumVisualizer.Core.Store;
using SpectrumVisualizer.Tests.Unit.Core.Store;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Trend;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Trend;

[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendViewModelTests : TrendFeedTestBase
{
	private readonly TrendViewModel _trend;

	public TrendViewModelTests()
	{
		_trend = new TrendViewModel(Chart, Feed);
		Feed.Start();
	}

	[AvaloniaFact]
	public void ThePresetsAreFiveWidthsWithTenMinutesSelected()
	{
		_trend.Presets.Select(preset => preset.Width).Should().Equal(
			TimeSpan.FromMinutes(1),
			TimeSpan.FromMinutes(10),
			TimeSpan.FromHours(1),
			TimeSpan.FromHours(6),
			TimeSpan.FromHours(24));
		_trend.SelectedPreset.Width.Should().Be(TimeSpan.FromMinutes(10));
	}

	[AvaloniaFact]
	public void APresetSetsTheChartWidthAndKeepsFollowing()
	{
		AddSpectra(0, 60);
		Sample();

		_trend.SelectedPreset = _trend.Presets[2];

		Chart.Width.Should().Be(TimeSpan.FromHours(1));
		Chart.To.Should().Be(At(59));
		Chart.IsFollowing.Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task FollowLiveIsEnabledOnlyWhileNotFollowing()
	{
		AddSpectra(0, 1800);
		Sample();
		Dispatcher.UIThread.RunJobs();
		CanExecute(_trend.FollowLiveCommand).Should().BeFalse("the window follows the newest point");

		Chart.PanBy(TimeSpan.FromMinutes(-5));
		Dispatcher.UIThread.RunJobs();
		CanExecute(_trend.FollowLiveCommand).Should().BeTrue();

		await _trend.FollowLiveCommand.Execute();
		Dispatcher.UIThread.RunJobs();

		Chart.IsFollowing.Should().BeTrue();
		Chart.To.Should().Be(At(1799));
		CanExecute(_trend.FollowLiveCommand).Should().BeFalse();
	}

	[AvaloniaFact]
	public void AClickInsideTheRecentSpectraNeedsNoStoreRead()
	{
		AddSpectra(0, 10);

		_trend.ShowMoment(At(5.5));

		Switching.ShownMoment.Should().Be(At(5));
		Switching.Current!.IsHistorical.Should().BeTrue();
		SpectrumCalls.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task AClickBeforeTheRecentSpectraShowsTheStoredSpectrum()
	{
		AddSpectra(0, 300);
		SpectrumResult = call => Result.Ok<StoredSpectrum?>(Stored(call.Moment));

		_trend.ShowMoment(At(10));

		await Waits.UntilAsync(() => Switching.ShownMoment == At(10));
		SpectrumCalls.Should().ContainSingle().Which.Moment.Should().Be(At(10));
	}

	[AvaloniaFact]
	public async Task ASecondClickCancelsTheFirst()
	{
		AddSpectra(0, 300);
		SpectrumResult = call => Result.Ok<StoredSpectrum?>(Stored(call.Moment));
		HoldSpectrum();
		_trend.ShowMoment(At(10));
		await Waits.UntilAsync(() => SpectrumCalls.Count == 1);
		var first = SpectrumCalls.Single();

		_trend.ShowMoment(At(20));

		first.Token.IsCancellationRequested.Should().BeTrue();
		await Waits.UntilAsync(() => SpectrumCalls.Count == 2);
		ReleaseSpectrum();
		await Waits.UntilAsync(() => Switching.ShownMoment == At(20));
		await Feed.StopAsync().WaitAsync(Waits.Limit);
		Switching.ShownMoment.Should().Be(At(20), "the cancelled read shows nothing");
	}

	[AvaloniaFact]
	public async Task NoStoredSpectrumChangesNothing()
	{
		AddSpectra(0, 300);

		_trend.ShowMoment(At(10));

		await Waits.UntilAsync(() => FeedLogger.Collector.GetSnapshot().Any(record => record.Level == LogLevel.Debug));
		Switching.ShownMoment.Should().BeNull();
		Switching.Current.Should().BeNull();
		MessagePanel.Entries.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task AFailedMomentReadReportsOnce()
	{
		AddSpectra(0, 300);
		SpectrumResult = _ => Result.Fail(new Error("the store file is locked"));

		_trend.ShowMoment(At(10));

		await Waits.UntilAsync(() => MessagePanel.Entries.Count == 1);
		await Feed.StopAsync().WaitAsync(Waits.Limit);
		var entry = MessagePanel.Entries.Should().ContainSingle().Subject;
		entry.View.Title.Should().Be(Resources.TrendMomentTitle);
		entry.View.Remedy.Should().Be(Resources.TrendMomentRemedy);
		Switching.ShownMoment.Should().BeNull();
	}

	protected override void Dispose(bool disposing)
	{
		_trend.Dispose();
		base.Dispose(disposing);
	}

	private static bool CanExecute(ICommand command)
	{
		return command.CanExecute(null);
	}

	private static StoredSpectrum Stored(DateTimeOffset receivedAt)
	{
		var frame = StoreSamples.Spectrum(receivedAt).Frame;
		return new StoredSpectrum(frame.Measurement!, receivedAt, frame.Seq, frame.State);
	}
}
