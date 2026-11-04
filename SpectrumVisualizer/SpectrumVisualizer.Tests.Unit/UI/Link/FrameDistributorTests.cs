using System.Collections.Concurrent;
using System.Reactive.Concurrency;
using System.Threading.Channels;

using AwesomeAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using SpectrumVisualizer.Core.Protocol;
using SpectrumVisualizer.Tests.Unit.UI.Messages;
using SpectrumVisualizer.UI.Link;
using SpectrumVisualizer.UI.Messages;

using Xunit;

using static SpectrumVisualizer.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SpectrumVisualizer.Tests.Unit.UI.Link;

[Trait("Component", "UI")]
[Trait("Area", "Link")]
[Trait("Category", "Unit")]
public sealed class FrameDistributorTests : IAsyncLifetime
{
	private const int TestTimeoutMs = 10_000;

	private static readonly DateTimeOffset _receivedAt = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

	private readonly Channel<ReceivedFrame> _channel = Channel.CreateBounded<ReceivedFrame>(
		new BoundedChannelOptions(64)
		{
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleWriter = true,
			SingleReader = true
		});

	private readonly MessagePanelViewModel _panel = new(TimeProvider.System);
	private readonly FakeLogger<FrameDistributor> _logger = new();
	private readonly FrameDistributor _distributor;

	public FrameDistributorTests()
	{
		_distributor = new FrameDistributor(_channel.Reader, _panel, ImmediateScheduler.Instance, _logger);
	}

	public ValueTask InitializeAsync()
	{
		return ValueTask.CompletedTask;
	}

	public async ValueTask DisposeAsync()
	{
		await _distributor.StopAsync().WaitAsync(Waits.Limit);
		_distributor.Dispose();
		_panel.Dispose();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task EveryFrameReachesEverySubscriberInOrder()
	{
		var first = Collect();
		var second = Collect();
		ReceivedFrame[] frames = [Received(SpectrumFrame(Spectrometer.Atp2000, 1)), Received(Heartbeat(2)),
			Received(SpectrumFrame(Spectrometer.Atp8000, 3))];
		_distributor.Start();

		Write(frames);

		await Waits.UntilAsync(() => first.Count == frames.Length && second.Count == frames.Length);
		first.Should().Equal(frames);
		second.Should().Equal(frames);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task ALateSubscriberSeesOnlyLaterFrames()
	{
		var early = Collect();
		var before = Received(SpectrumFrame(Spectrometer.Atp2000, 1));
		var after = Received(SpectrumFrame(Spectrometer.Atp2000, 2));
		_distributor.Start();
		Write(before);
		await Waits.UntilAsync(() => early.Count == 1);

		var late = Collect();
		Write(after);

		await Waits.UntilAsync(() => early.Count == 2 && late.Count == 1);
		late.Should().Equal(after);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task TheLatestSpectrumSkipsHeartbeats()
	{
		var seen = Collect();
		var spectrum = Received(SpectrumFrame(Spectrometer.Atp8000, 1));
		_distributor.LatestSpectrum.Should().BeNull();
		_distributor.Start();

		Write(spectrum, Received(Heartbeat(2)));

		await Waits.UntilAsync(() => seen.Count == 2);
		_distributor.LatestSpectrum.Should().BeSameAs(spectrum);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AThrowingSubscriberDoesNotTakeTheFrameFromTheOthers()
	{
		var frame1 = Received(SpectrumFrame(Spectrometer.Atp2000, 1));
		var frame2 = Received(SpectrumFrame(Spectrometer.Atp2000, 2));
		var first = new ConcurrentQueue<ReceivedFrame>();
		using var throwing = _distributor.Frames.Subscribe(received =>
		{
			first.Enqueue(received);
			if (ReferenceEquals(received, frame1))
			{
				throw new InvalidOperationException("the subscriber failed");
			}
		});
		var second = Collect();
		_distributor.Start();

		Write(frame1, frame2);

		await Waits.UntilAsync(() => first.Count == 2 && second.Count == 2);
		first.Should().Equal(frame1, frame2);
		second.Should().Equal(frame1, frame2);
		_panel.Entries.Should().ContainSingle();
		_panel.Entries[0].View.Severity.Should().Be(MessageSeverity.Error);
		_panel.Entries[0].View.Detail.Should().Contain(nameof(InvalidOperationException));
		_logger.Levels().Should().ContainSingle(level => level == LogLevel.Error);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AnUnsubscribedObserverReceivesNothingMore()
	{
		var dropped = new ConcurrentQueue<ReceivedFrame>();
		var subscription = _distributor.Frames.Subscribe(dropped.Enqueue);
		var kept = Collect();
		_distributor.Start();
		Write(Received(SpectrumFrame(Spectrometer.Atp2000, 1)));
		await Waits.UntilAsync(() => dropped.Count == 1 && kept.Count == 1);

		subscription.Dispose();
		Write(Received(SpectrumFrame(Spectrometer.Atp2000, 2)));

		await Waits.UntilAsync(() => kept.Count == 2);
		dropped.Should().HaveCount(1);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task StopEndsTheReadLoopAndStartAfterStopDoesNothing()
	{
		var seen = Collect();
		_distributor.Start();
		Write(Received(SpectrumFrame(Spectrometer.Atp2000, 1)));
		await Waits.UntilAsync(() => seen.Count == 1);

		await _distributor.StopAsync().WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);
		_distributor.Start();
		Write(Received(SpectrumFrame(Spectrometer.Atp2000, 2)));

		_channel.Reader.Count.Should().Be(1);
		seen.Should().HaveCount(1);
		_logger.Collector.LatestRecord.Level.Should().Be(LogLevel.Debug);
		_logger.Collector.LatestRecord.Message.Should().Be("The frame distributor stopped");
		_logger.Levels().Should().NotContain(level => level >= LogLevel.Warning);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public void StopBeforeStartCompletesAtOnce()
	{
		var stop = _distributor.StopAsync();

		stop.IsCompletedSuccessfully.Should().BeTrue();
		_distributor.Start();
		Write(Received(SpectrumFrame(Spectrometer.Atp2000, 1)));
		_channel.Reader.Count.Should().Be(1);
	}

	private static ReceivedFrame Received(BoardFrame frame)
	{
		return new ReceivedFrame(frame, _receivedAt);
	}

	private ConcurrentQueue<ReceivedFrame> Collect()
	{
		var frames = new ConcurrentQueue<ReceivedFrame>();
		_distributor.Frames.Subscribe(frames.Enqueue);
		return frames;
	}

	private void Write(params ReceivedFrame[] frames)
	{
		foreach (var frame in frames)
		{
			_channel.Writer.TryWrite(frame).Should().BeTrue();
		}
	}
}
