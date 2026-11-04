using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using ReactiveUI.Avalonia;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Tests.Unit.UI.Link;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Logging;
using SpectrumVisualizer.UI.MainWindow;
using SpectrumVisualizer.UI.Messages;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;

using MainWindowView = SpectrumVisualizer.UI.MainWindow.MainWindow;

namespace SpectrumVisualizer.Tests.Unit.UI.MainWindow;

/// <summary>
/// The desktop UI checklist for the connection row and the status bar. The headless text stub gives no real width, so
/// a row's width is its realised chrome plus its texts measured in real faces (<see cref="RenderedText"/>).
/// </summary>
[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class MainWindowLayoutTests : IDisposable
{
	private const string PortName = "COM3";

	// Windows numbers serial ports up to COM256.
	private const string WidestPortName = "COM256";

	// A day of ATP-8000 spectra at 1.3 per second is about 112 000, so six digits cover a day with every spectrum lost.
	private const long WidestLostCount = 999_999;

	private const double LargeWidth = 2560;

	private static readonly TimeSpan _waitLimit = TimeSpan.FromSeconds(5);

	private static readonly string[] _connectionRowParts = ["PortLabel", "PortList", "RefreshPorts", "Connect", "Disconnect"];

	private static readonly string[] _statusSegments = ["LinkText", "PortText", "Atp2000Text", "Atp8000Text", "LostSpectraText"];

	private readonly TestScheduler _linkScheduler = new();
	private readonly FakeSerialPortProvider _portProvider = new(PortName);
	private readonly FakePortStream _port = new();
	private readonly MessagePanelViewModel _messagePanel = new(TimeProvider.System);
	private readonly SerialLink _serialLink;
	private ConnectionBarViewModel? _connectionBar;
	private AppStatusBarViewModel? _statusBar;
	private MainWindowViewModel? _viewModel;
	private MainWindowView? _window;

	public MainWindowLayoutTests()
	{
		_serialLink = new SerialLink(_portProvider, _linkScheduler, NullLogger<SerialLink>.Instance);
	}

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
			+ Measure(Resources.FormatStatusLostSpectraFormat(WidestLostCount.ToString(CultureInfo.CurrentCulture)));

		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{culture}: faces {string.Join(", ", RenderedText.InstalledFamilies(family))} + Inter at {fontSize} px; "
			+ $"connection row {connectionWidth:0.#} px, status bar {statusWidth:0.#} px, "
			+ $"window minimum {MainWindowView.MinimumWidth} px");
		window.MinWidth.Should().Be(MainWindowView.MinimumWidth);
		connectionWidth.Should().BeLessThanOrEqualTo(MainWindowView.MinimumWidth, "the whole connection row shows");
		statusWidth.Should().BeLessThanOrEqualTo(MainWindowView.MinimumWidth, "every status segment shows whole");
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
			- placeholder.Margin.Left - placeholder.Margin.Right;
		var width = RenderedText.Width(Resources.ConnectionPortPlaceholder, placeholder.FontFamily, placeholder.FontSize);

		TestContext.Current.TestOutputHelper?.WriteLine($"{culture}: placeholder {width:0.#} px in a {slot:0.#} px slot");
		portList.Bounds.Width.Should().Be(240, "a choice field is 240 px wide");
		placeholder.Text.Should().Be(Resources.ConnectionPortPlaceholder);
		placeholder.TextWrapping.Should().Be(TextWrapping.NoWrap);
		width.Should().BeLessThanOrEqualTo(slot);
	}

	[AvaloniaFact]
	public void BelowTheMinimumTheWindowStopsAtItAndEveryFixedControlKeepsItsWidth()
	{
		var window = Realise();
		var opening = PartWidths(window);

		window.Width = 400;
		Dispatcher.UIThread.RunJobs();

		window.Bounds.Width.Should().Be(MainWindowView.MinimumWidth);
		Named<Border>(window, "ConnectionRow").Bounds.Width.Should().Be(MainWindowView.MinimumWidth);
		Named<Border>(window, "StatusBar").Bounds.Width.Should().Be(MainWindowView.MinimumWidth);
		PartWidths(window).Should().Equal(opening, "no control of either row shrinks at the minimum");
	}

	[AvaloniaFact]
	public void AtALargeWidthBothRowsStayLeftAlignedWithTheirGapsUnchanged()
	{
		var window = Realise();
		var opening = PartLefts(window);

		window.Width = LargeWidth;
		Dispatcher.UIThread.RunJobs();

		window.Bounds.Width.Should().Be(LargeWidth);
		PartLefts(window).Should().Equal(opening, "only the rows' empty right end grows");
	}

	[AvaloniaFact]
	public async Task EveryLinkStateKeepsTheWindowAndBothRowsAtTheirSize()
	{
		var window = Realise();
		var shapes = new List<(LinkState State, RowShape Shape)> { (_serialLink.Status.State, ShapeOf(window)) };
		async Task Record()
		{
			await SettleAsync();
			window.UpdateLayout();
			shapes.Add((_serialLink.Status.State, ShapeOf(window)));
		}

		_portProvider.QueueOpenFailure(new PortBusyError(PortName));
		_serialLink.Connect(PortName);
		await WaitUntilAsync(() => _serialLink.Status.State == LinkState.PortUnavailable);
		await Record();
		_portProvider.QueueOpen(_port);
		_linkScheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks);
		await WaitForReadAsync();
		await Record();
		await AtAsync(2, () => _port.PushBytes(FrameEncoder.Encode(Heartbeat(1))));
		await Record();
		await AtAsync(12, _port.PushTimeout);
		await Record();
		await AtAsync(13, () => _port.PushBytes(Corrupted(Spectrum(Spectrometer.Atp2000, 2))));
		await AtAsync(18, () => _port.PushBytes(Corrupted(Spectrum(Spectrometer.Atp2000, 3))));
		await AtAsync(23, _port.PushTimeout);
		await Record();
		await _serialLink.DisconnectAsync().WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
		await Record();

		shapes.Select(shape => shape.State).Should().Equal(
			LinkState.Disconnected,
			LinkState.PortUnavailable,
			LinkState.Waiting,
			LinkState.Receiving,
			LinkState.NotResponding,
			LinkState.OtherDevice,
			LinkState.Disconnected);
		shapes.Select(shape => shape.Shape).Should().AllBeEquivalentTo(shapes[0].Shape);
	}

	public void Dispose()
	{
		_window?.Close();
		_viewModel?.Dispose();
		_connectionBar?.Dispose();
		_statusBar?.Dispose();
		_serialLink.Dispose();
		_port.Dispose();
		_messagePanel.Dispose();
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

	private static byte[] Corrupted(BoardFrame frame)
	{
		var bytes = FrameEncoder.Encode(frame);
		bytes[^1] ^= 0xFF;
		return bytes;
	}

	private static T Named<T>(Control root, string name)
		where T : Control
	{
		return root.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name)
			?? throw new InvalidOperationException($"No control named {name}.");
	}

	/// <summary>Every text the row draws: its text blocks and the access text inside each button.</summary>
	private static IEnumerable<TextBlock> Texts(Visual row)
	{
		return row.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible);
	}

	/// <summary>The row's natural width less its texts' stub widths: padding, margins, spacing, borders, the fixed controls.</summary>
	private static double ChromeWidth(Border row, IReadOnlyCollection<TextBlock> texts)
	{
		row.DesiredSize.Width.Should().BeLessThanOrEqualTo(row.Bounds.Width, "nothing in the row is clipped at the opening width");

		return row.DesiredSize.Width - texts.Sum(text => text.Bounds.Width);
	}

	private static IEnumerable<Control> Parts(Window window)
	{
		return _connectionRowParts.Concat(_statusSegments).Select(name => Named<Control>(window, name));
	}

	private static List<double> PartWidths(Window window)
	{
		return [.. Parts(window).Select(part => part.Bounds.Width)];
	}

	private static List<double> PartLefts(Window window)
	{
		return [.. Parts(window).Select(part => part.TranslatePoint(default, window)!.Value.X)];
	}

	private static RowShape ShapeOf(Window window)
	{
		static string[] VisibleNames(Control row)
		{
			return [.. row.GetVisualDescendants().OfType<Control>()
				.Where(control => control.IsEffectivelyVisible && !string.IsNullOrEmpty(control.Name))
				.Select(control => control.Name!)];
		}

		var connectionRow = Named<Border>(window, "ConnectionRow");
		var statusRow = Named<Border>(window, "StatusBar");

		return new RowShape(
			window.Bounds.Size,
			connectionRow.Bounds.Height,
			statusRow.Bounds.Height,
			VisibleNames(connectionRow),
			VisibleNames(statusRow));
	}

	private MainWindowView Realise()
	{
		_connectionBar = new ConnectionBarViewModel(_portProvider, _serialLink, AvaloniaScheduler.Instance);
		_statusBar = new AppStatusBarViewModel(
			_serialLink,
			_messagePanel,
			AvaloniaScheduler.Instance,
			NullLogger<AppStatusBarViewModel>.Instance);
		_viewModel = new MainWindowViewModel(
			_messagePanel,
			_connectionBar,
			_statusBar,
			new LogLevelControl(),
			NullLogger<MainWindowViewModel>.Instance);
		_window = new MainWindowView { DataContext = _viewModel };
		_window.Show();
		Dispatcher.UIThread.RunJobs();

		return _window;
	}

	/// <summary>Moves virtual time first, so the link stamps the step with the given second.</summary>
	private async Task AtAsync(double seconds, Action push)
	{
		_linkScheduler.AdvanceTo(TimeSpan.FromSeconds(seconds).Ticks);
		push();
		await WaitForReadAsync();
	}

	private Task WaitForReadAsync()
	{
		return _port.WaitForPendingReadAsync().WaitAsync(_waitLimit, TestContext.Current.CancellationToken);
	}

	/// <summary>The link raises its status off the UI thread, so the bar catches up once the dispatcher runs the hop.</summary>
	private Task SettleAsync()
	{
		return WaitUntilAsync(() => ReferenceEquals(_statusBar!.Status, _serialLink.Status));
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		using var limit = new CancellationTokenSource(_waitLimit);
		while (true)
		{
			Dispatcher.UIThread.RunJobs();
			if (condition())
			{
				return;
			}

			await Task.Delay(10, limit.Token);
		}
	}

	private sealed record RowShape(
		Size Window,
		double ConnectionRowHeight,
		double StatusBarHeight,
		string[] ConnectionRowParts,
		string[] StatusBarParts);
}
