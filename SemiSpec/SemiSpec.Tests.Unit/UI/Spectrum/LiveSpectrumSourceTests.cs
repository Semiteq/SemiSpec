using System.Collections.Concurrent;
using System.Reactive.Concurrency;
using System.Threading.Channels;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Testing;

using SemiSpec.Core.Protocol;
using SemiSpec.UI.Link;
using SemiSpec.UI.Messages;
using SemiSpec.UI.Spectrum;

using Xunit;

using static SemiSpec.Tests.Unit.Core.Protocol.ProtocolSamples;

namespace SemiSpec.Tests.Unit.UI.Spectrum;

[Trait("Component", "UI")]
[Trait("Area", "Spectrum")]
[Trait("Category", "Unit")]
public sealed class LiveSpectrumSourceTests : IAsyncLifetime
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
	private readonly FrameDistributor _distributor;
	private readonly LiveSpectrumSource _source;

	public LiveSpectrumSourceTests()
	{
		_distributor = new FrameDistributor(
			_channel.Reader,
			_panel,
			ImmediateScheduler.Instance,
			new FakeLogger<FrameDistributor>());
		_source = new LiveSpectrumSource(_distributor);
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

	[Fact]
	public void CurrentIsNullBeforeASpectrum()
	{
		_source.Current.Should().BeNull();
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task AHeartbeatChangesNeitherCurrentNorCurrentChanged()
	{
		var changes = 0;
		using var changed = _source.CurrentChanged.Subscribe(_ => Interlocked.Increment(ref changes));
		var delivered = CollectFrames();
		_distributor.Start();
		Write(Received(SpectrumFrame(Spectrometer.Atp8000, 1)));
		await Waits.UntilAsync(() => delivered.Count == 1);
		var before = _source.Current;

		Write(Received(Heartbeat(2)));

		await Waits.UntilAsync(() => delivered.Count == 2);
		Volatile.Read(ref changes).Should().Be(1);
		_source.Current!.Measurement.Should().BeSameAs(before!.Measurement);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task ASpectrumFrameUpdatesCurrentAndSignalsCurrentChanged()
	{
		var changes = 0;
		using var changed = _source.CurrentChanged.Subscribe(_ => Interlocked.Increment(ref changes));
		var frame = SpectrumFrame(Spectrometer.Atp2000, 1);
		var receivedAt = _receivedAt.AddSeconds(3);
		_distributor.Start();

		Write(new ReceivedFrame(frame, receivedAt));

		await Waits.UntilAsync(() => Volatile.Read(ref changes) == 1);
		_source.Current.Should().NotBeNull();
		_source.Current!.Measurement.Should().BeSameAs(frame.Measurement);
		_source.Current.ReceivedAt.Should().Be(receivedAt);
	}

	[Fact(Timeout = TestTimeoutMs)]
	public async Task TwoReadsOfOneFrameCarryTheSameMeasurement()
	{
		var delivered = CollectFrames();
		_distributor.Start();
		Write(Received(SpectrumFrame(Spectrometer.Atp2000, 1)));
		await Waits.UntilAsync(() => delivered.Count == 1);

		var first = _source.Current;
		var second = _source.Current;

		first.Should().NotBeNull();
		second!.Measurement.Should().BeSameAs(first!.Measurement);
	}

	private static ReceivedFrame Received(BoardFrame frame)
	{
		return new ReceivedFrame(frame, _receivedAt);
	}

	/// <summary>Subscribes after the source, so a frame counted here has already passed the source's filter.</summary>
	private ConcurrentQueue<ReceivedFrame> CollectFrames()
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
