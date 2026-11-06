using System.Globalization;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using SemiSpec.UI.Localization;
using SemiSpec.UI.Trend;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Trend;

[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TimeWindowPanelViewModelTests : TrendChartTestBase
{
	private readonly TimeWindowPanelViewModel _panel;

	public TimeWindowPanelViewModelTests()
	{
		_panel = new TimeWindowPanelViewModel(Chart);
		AddSpectra(0, 1200);
		Sample();
		_panel.Seed();
	}

	[AvaloniaFact]
	public void TheSeedShowsTheWindowInLocalTimeAndItsWidth()
	{
		_panel.StartTimeText.Should().Be(Clock(Chart.From));
		_panel.EndTimeText.Should().Be(Clock(Chart.To));
		_panel.StartDate.Should().Be(Chart.From.ToLocalTime().Date);
		_panel.WidthText.Should().Be("00:10:00");
		_panel.Message.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void AnUntouchedSeedKeepsFollowingOnClose()
	{
		AddSpectra(1200, 5);
		Sample();

		_panel.Close();

		Chart.IsFollowing.Should().BeTrue("the fields show the window of the opening, which the operator did not change");
	}

	[AvaloniaFact]
	public void TypedEdgesShowThatWindowAndStopFollowing()
	{
		_panel.StartTimeText = Clock(At(300));
		_panel.EndTimeText = Clock(At(600));

		_panel.Commit();

		Chart.From.Should().Be(At(300));
		Chart.To.Should().Be(At(600));
		Chart.IsFollowing.Should().BeFalse();
		_panel.Message.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void AnEndNotAfterTheStartShowsTheMessageAndKeepsTheWindow()
	{
		var from = Chart.From;
		_panel.StartTimeText = Clock(At(600));
		_panel.EndTimeText = Clock(At(300));

		_panel.Commit();

		_panel.Message.Should().Be(Resources.TimeWindowEndNotAfterStart);
		Chart.From.Should().Be(from);
		Chart.IsFollowing.Should().BeTrue();
	}

	[AvaloniaFact]
	public void AnUnreadableWidthShowsTheMessage()
	{
		_panel.WidthText = "ten";

		_panel.Commit();

		_panel.Message.Should().Be(
			Resources.FormatTimeWindowWidthUnreadableFormat(Resources.TimeWindowClockPattern));
	}

	[AvaloniaFact]
	public void ATypedWidthKeepsTheRightEdgeAndFollowing()
	{
		_panel.WidthText = "00:01:00";

		_panel.Commit();

		Chart.Width.Should().Be(TimeSpan.FromMinutes(1));
		Chart.IsFollowing.Should().BeTrue();
	}

	private static string Clock(DateTimeOffset moment)
	{
		return moment.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
	}
}
