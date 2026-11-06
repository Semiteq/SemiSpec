using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using AwesomeAssertions;

using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Spectrum;

using Xunit;

using MainWindowView = SpectrumVisualizer.UI.MainWindow.MainWindow;

namespace SpectrumVisualizer.Tests.Unit.UI.MainWindow;

[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowTextFitRowTests : MainWindowTextFitTestBase
{
	// Windows numbers serial ports up to COM256.
	private const string WidestPortName = "COM256";

	[AvaloniaTheory]
	[InlineData("en")]
	[InlineData("ru")]
	public void TheMinimumWidthHoldsTheConnectionRowAndTheLongestStatusTextsOnOneLine(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		var window = Realise();
		var connectionRow = Named<Border>(window, "ConnectionRow");
		var statusRow = Named<Border>(window, "StatusBar");

		var connectionTexts = Texts(connectionRow).Where(text => text.Name != "PlaceholderTextBlock").ToList();
		var statusTexts = Texts(statusRow).ToList();
		connectionTexts.Concat(statusTexts).Should().OnlyContain(
			text => text.TextWrapping == TextWrapping.NoWrap,
			"a label, a button or a status text never takes a second line");

		var family = statusTexts[0].FontFamily;
		var fontSize = statusTexts[0].FontSize;
		double Measure(string text)
		{
			return RenderedText.Width(text, family, fontSize);
		}

		var connectionWidth = ChromeWidth(connectionRow, connectionTexts)
			+ connectionTexts.Sum(text => Measure(text.Text!));
		var statusWidth = ChromeWidth(statusRow, statusTexts)
			+ LinkTexts().Max(Measure)
			+ Measure(WidestPortName)
			+ SpectrometerTexts("ATP-2000").Max(Measure)
			+ SpectrometerTexts("ATP-8000").Max(Measure)
			+ Measure(Resources.FormatStatusLostSpectraFormat(WidestLostCount.ToString(CultureInfo.CurrentCulture)))
			+ Measure(Resources.FormatStatusDroppedFramesFormat(WidestLostCount.ToString(CultureInfo.CurrentCulture)));

		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{culture}: faces {string.Join(", ", RenderedText.InstalledFamilies(family))} + Inter at {fontSize} px; "
			+ $"connection row {connectionWidth:0.#} px, status bar {statusWidth:0.#} px, "
			+ $"window minimum {MainWindowView.MinimumWidth} px, opening {MainWindowView.OpeningWidth} px");
		var faces = $"measured in {string.Join(", ", RenderedText.InstalledFamilies(family))} and Inter";
		window.MinWidth.Should().Be(MainWindowView.MinimumWidth);
		window.Bounds.Width.Should().Be(MainWindowView.OpeningWidth);
		MainWindowView.MinimumWidth.Should().BeLessThanOrEqualTo(
			MainWindowView.OpeningWidth,
			"both rows show whole at the opening width, since they fit the minimum");
		connectionWidth.Should().BeLessThanOrEqualTo(
			MainWindowView.MinimumWidth,
			$"the whole connection row shows, {faces}");
		statusWidth.Should().BeLessThanOrEqualTo(
			MainWindowView.MinimumWidth,
			$"every status segment shows whole, {faces}");
	}

	[AvaloniaTheory]
	[InlineData("en")]
	[InlineData("ru")]
	public void ThePortPlaceholderFitsThePortListOnOneLine(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		var window = Realise();
		var portList = Named<ComboBox>(window, "PortList");
		var placeholder = Texts(portList).Single(text => text.Name == "PlaceholderTextBlock");

		var slot = LayoutInformation.GetPreviousArrangeBounds(placeholder)!.Value.Width
			- placeholder.Margin.Left
			- placeholder.Margin.Right;
		var width = RenderedText.Width(
			Resources.ConnectionPortPlaceholder,
			placeholder.FontFamily,
			placeholder.FontSize);

		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{culture}: placeholder {width:0.#} px in a {slot:0.#} px slot");
		portList.Bounds.Width.Should().Be(240, "a choice field is 240 px wide");
		placeholder.Text.Should().Be(Resources.ConnectionPortPlaceholder);
		placeholder.TextWrapping.Should().Be(TextWrapping.NoWrap);
		width.Should().BeLessThanOrEqualTo(slot);
	}

	[AvaloniaFact]
	public void TheMinimumWidthHoldsTheSignalPaneAndTheChartFloor()
	{
		var window = Realise();

		window.Width = 400;
		Dispatcher.UIThread.RunJobs();

		MainWindowView.MinimumWidth.Should()
			.BeGreaterThanOrEqualTo(RoundUpTo8(SpectrumPanel.PaneWidth + ChartFloorWidth));
		Named<Border>(window, "SpectrumPane").Bounds.Width.Should().BeGreaterThanOrEqualTo(ChartFloorWidth);
		Named<Border>(window, "SignalPane").Bounds.Width.Should().Be(SpectrumPanel.PaneWidth);
	}

	private static IEnumerable<string> LinkTexts()
	{
		return
		[
			Resources.StatusNotConnected,
			Resources.StatusConnecting,
			Resources.StatusPortUnavailable,
			Resources.StatusWaiting,
			Resources.StatusReceiving,
			Resources.StatusNotResponding,
			Resources.StatusOtherDevice,
		];
	}

	private static IEnumerable<string> SpectrometerTexts(string device)
	{
		return new[]
			{
				Resources.StatusSpectrometerConnected,
				Resources.StatusSpectrometerNotConnected,
				Resources.StatusSpectrometerFailed,
				Resources.StatusSpectrometerNoData,
			}
			.Select(state => Resources.FormatStatusSpectrometerFormat(device, state));
	}

	/// <summary>
	/// The row's natural width less its texts' stub widths: padding, margins, spacing, borders, the fixed controls.
	/// </summary>
	private static double ChromeWidth(Border row, IReadOnlyCollection<TextBlock> texts)
	{
		// Avalonia clamps DesiredSize to the space offered, so only an unbounded measure gives the whole chrome.
		row.Measure(Size.Infinity);
		var naturalWidth = row.DesiredSize.Width;
		var textWidths = texts.Sum(text => text.DesiredSize.Width - text.Margin.Left - text.Margin.Right);

		return naturalWidth - textWidths;
	}
}
