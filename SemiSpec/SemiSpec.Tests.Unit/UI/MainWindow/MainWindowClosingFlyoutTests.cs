using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using ScottPlot.Avalonia;

using SemiSpec.UI.Spectrum;
using SemiSpec.UI.Trend;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.MainWindow;

[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowClosingFlyoutTests : MainWindowLayoutTestBase
{
	[AvaloniaFact]
	public void ClosingTheWindowClosesTheTimeWindowFlyoutAndItsOpenCalendar()
	{
		var window = Realise();
		var flyout = OpenFlyout<TrendChartView>(window, "TimeWindowFlyout");
		var picker = Nested<CalendarDatePicker>(flyout);
		picker.IsDropDownOpen = true;
		Dispatcher.UIThread.RunJobs();

		var close = () => CloseWindow(window);

		close.Should().NotThrow();
		picker.IsDropDownOpen.Should().BeFalse();
		flyout.IsOpen.Should().BeFalse();
	}

	[AvaloniaFact]
	public void ClosingTheWindowClosesTheOpenPresetList()
	{
		var window = Realise();
		var flyout = OpenFlyout<TrendChartView>(window, "TimeWindowFlyout");
		var presets = Nested<DropDownButton>(flyout).Flyout!;
		presets.ShowAt(Nested<DropDownButton>(flyout));
		Dispatcher.UIThread.RunJobs();

		var close = () => CloseWindow(window);

		close.Should().NotThrow();
		presets.IsOpen.Should().BeFalse();
		flyout.IsOpen.Should().BeFalse();
	}

	[AvaloniaFact]
	public void ClosingTheWindowClosesTheSpectrumAxisFlyout()
	{
		var window = Realise();
		var flyout = OpenFlyout<SpectrumChartView>(window, "IntensityAxisFlyout");

		var close = () => CloseWindow(window);

		close.Should().NotThrow();
		flyout.IsOpen.Should().BeFalse();
	}

	private static Flyout OpenFlyout<TView>(Window window, string key)
		where TView : Control
	{
		var view = window.GetVisualDescendants().OfType<TView>().Single();
		var flyout = (Flyout)view.Resources[key]!;
		flyout.ShowAt(view.GetVisualDescendants().OfType<AvaPlot>().Single(), true);
		Dispatcher.UIThread.RunJobs();

		return flyout;
	}

	private static T Nested<T>(Flyout flyout)
		where T : Control
	{
		return ((Control)flyout.Content!).GetSelfAndLogicalDescendants().OfType<T>().First();
	}

	private static void CloseWindow(Window window)
	{
		window.Close();
		Dispatcher.UIThread.RunJobs();
	}
}
