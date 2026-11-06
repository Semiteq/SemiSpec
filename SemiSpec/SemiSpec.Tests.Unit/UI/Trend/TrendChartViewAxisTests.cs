using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;

using AwesomeAssertions;

using SemiSpec.UI.Chart;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Trend;

/// <summary>A left press on the trend's axis bands, and the time flyout's preset list, on the realised view.</summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendChartViewAxisTests : TrendChartViewTestBase
{
	private const double IntoTheBand = 10.0;

	private Flyout TimeFlyout => (Flyout)View.Resources["TimeWindowFlyout"]!;

	private Flyout IntensityFlyout => (Flyout)View.Resources["IntensityAxisFlyout"]!;

	[AvaloniaFact]
	public void AClickOnTheTimeAxisOpensTheTimeFlyoutAndShowsNoMoment()
	{
		ShowTrend();
		var press = new Point(DataRect.HorizontalCenter, DataRect.Bottom + IntoTheBand);
		ChartAxisRegion.TryCreateBottom(Chart.Plot)!.Contains(press).Should().BeTrue();

		Window.MouseDown(InWindow(press), MouseButton.Left);
		Window.MouseUp(InWindow(press), MouseButton.Left);
		Dispatcher.UIThread.RunJobs();

		TimeFlyout.IsOpen.Should().BeTrue();
		Switching.ShownMoment.Should().BeNull("a click on the axis shows no moment");
		Chart.IsFollowing.Should().BeTrue();
	}

	[AvaloniaFact]
	public void APressOnTheIntensityAxisOpensItsFlyoutAndStartsNoDrag()
	{
		ShowTrend();
		var from = Chart.From;
		var press = new Point(DataRect.Left - IntoTheBand, DataRect.VerticalCenter);
		ChartAxisRegion.TryCreateLeft(Chart.Plot)!.Contains(press).Should().BeTrue();

		Window.MouseDown(InWindow(press), MouseButton.Left);
		Window.MouseMove(InWindow(new Point(press.X + 40.0, press.Y)), RawInputModifiers.LeftMouseButton);
		Window.MouseUp(InWindow(new Point(press.X + 40.0, press.Y)), MouseButton.Left);
		Dispatcher.UIThread.RunJobs();

		IntensityFlyout.IsOpen.Should().BeTrue();
		Chart.From.Should().Be(from, "the press opened the flyout instead of a drag");
	}

	[AvaloniaFact]
	public void ARealisedPresetItemSetsTheChartWidth()
	{
		ShowTrend();
		var press = new Point(DataRect.HorizontalCenter, DataRect.Bottom + IntoTheBand);
		Window.MouseDown(InWindow(press), MouseButton.Left);
		Window.MouseUp(InWindow(press), MouseButton.Left);
		Dispatcher.UIThread.RunJobs();
		var presetButton = ((Control)TimeFlyout.Content!).GetLogicalDescendants().OfType<DropDownButton>().Single();
		var presetList = presetButton.Flyout!;

		var realised = new List<MenuItem>();
		using (Control.LoadedEvent.AddClassHandler<MenuItem>((item, _) => realised.Add(item)))
		{
			presetList.ShowAt(presetButton);
			Dispatcher.UIThread.RunJobs();
		}

		var oneHour = realised.Single(item => Equals(item.DataContext, Trend.TimeWindow.Presets[2]));
		oneHour.Command!.CanExecute(oneHour.CommandParameter).Should().BeTrue();
		oneHour.Command.Execute(oneHour.CommandParameter);
		Dispatcher.UIThread.RunJobs();

		Chart.Width.Should().Be(TimeSpan.FromHours(1));
	}
}
