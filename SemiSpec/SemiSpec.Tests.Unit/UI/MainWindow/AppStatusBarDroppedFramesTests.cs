using System.ComponentModel;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using SemiSpec.UI.Link;
using SemiSpec.UI.Localization;
using SemiSpec.UI.MainWindow;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.MainWindow;

/// <summary>The Dropped frames segment: the link's channel drops plus the store's.</summary>
[Trait("Component", "UI")]
[Trait("Area", "MainWindow")]
[Trait("Category", "Unit")]
public sealed class AppStatusBarDroppedFramesTests : AppStatusBarTestBase
{
	[AvaloniaFact]
	public void AFreshAppShowsZeroDroppedFramesNotTheNoValueDash()
	{
		ViewModel.DroppedFramesText.Should().Be(Resources.FormatStatusDroppedFramesFormat(0));
		ViewModel.LostSpectraText.Should().Be(
			Resources.FormatStatusLostSpectraFormat(Resources.StatusNoValue),
			"only the lost count describes the current connection");
	}

	[AvaloniaFact]
	public async Task TheDroppedSegmentSumsTheLinkAndTheStore()
	{
		await StartStoreAsync();
		await FailOneStoreBatchAsync();
		await ConnectAsync();

		await OverflowTheLinkChannelAsync(1);

		await Waits.UntilAsync(
			() => ViewModel.DroppedFramesText == Resources.FormatStatusDroppedFramesFormat(LinkOverflow + 1),
			pumpDispatcher: true);
		SerialLink.Status.DroppedFrames.Should().Be(LinkOverflow);
		Store.DroppedFrames.Should().Be(1);
	}

	[AvaloniaFact]
	public async Task TheDroppedCountStaysAfterADisconnect()
	{
		await ConnectAsync();
		await OverflowTheLinkChannelAsync(1);

		await DisconnectAsync();

		ViewModel.LinkText.Should().Be(Resources.StatusNotConnected);
		ViewModel.DroppedFramesText.Should().Be(Resources.FormatStatusDroppedFramesFormat(LinkOverflow));
	}

	[AvaloniaFact]
	public async Task AStoreDropOnAnotherThreadReachesTheTextOnTheUiScheduler()
	{
		var changedOnUiThread = new List<bool>();
		void OnChanged(object? sender, PropertyChangedEventArgs arguments)
		{
			if (arguments.PropertyName == nameof(AppStatusBarViewModel.DroppedFramesText))
			{
				changedOnUiThread.Add(Dispatcher.UIThread.CheckAccess());
			}
		}

		ViewModel.PropertyChanged += OnChanged;
		await StartStoreAsync();

		await FailOneStoreBatchAsync();

		await Waits.UntilAsync(
			() => ViewModel.DroppedFramesText == Resources.FormatStatusDroppedFramesFormat(1),
			pumpDispatcher: true);
		ViewModel.PropertyChanged -= OnChanged;
		changedOnUiThread.Should().NotBeEmpty().And.OnlyContain(onUiThread => onUiThread);
	}
}
