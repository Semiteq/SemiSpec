using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging;

using SemiSpec.Core.Store;
using SemiSpec.Core.Trend;
using SemiSpec.Tests.Unit.Core.Store;

using Xunit;

using static SemiSpec.Tests.Unit.Core.Store.StoreSamples;

namespace SemiSpec.Tests.Unit.UI.Trend;

[Trait("Component", "UI")]
[Trait("Area", "Trend")]
[Trait("Category", "Unit")]
public sealed class TrendFeedStartReadTests : TrendFeedTestBase
{
	private const string NoStoredSpectrum = "The trend reads no stored history: the spectrum store holds no spectrum";
	private const string StoreNotOpened = "The trend reads no stored history: the spectrum store did not open";

	// Long enough for a start read that does not wait for the store to reach its reader.
	private static readonly TimeSpan _settle = TimeSpan.FromMilliseconds(100);

	public enum StopPhase
	{
		BeforeTheStoreOpened,
		DuringTheKeyRead
	}

	[AvaloniaFact]
	public async Task TheStartReadWaitsForTheStoreToOpen()
	{
		Feed.Start();
		await Task.Delay(_settle, TestContext.Current.CancellationToken);
		SpectrumCalls.Should().BeEmpty("the store has not opened yet");

		StoreOpened.SetResult(true);

		await Waits.UntilAsync(() => SpectrumCalls.Count == 1);
	}

	[AvaloniaFact]
	public async Task TheStartReadLandsTheStoredDayAtTheLastStoredKey()
	{
		SpectrumResult = _ => Result.Ok<StoredSpectrum?>(StoredAt(Start.AddMinutes(-3), OtherNm));
		StoreOpened.SetResult(true);
		Feed.Start();
		var plan = TrendHistoryPlan.For(Chart.From, Chart.To, Start, Start);

		await PassThrottleUntilAPointsReadAsync();

		await Waits.UntilAsync(() => PointsCalls.Count == plan.Count);
		SpectrumCalls.Single().Moment.Should().Be(Start.AddMilliseconds(-1));
		PointsCalls.Should().OnlyContain(call => call.WavelengthNm == OtherNm && call.Delta == SampleDelta);
		RangesOf(PointsCalls).Should().Equal(plan);
	}

	[AvaloniaFact]
	public async Task AnEmptyStoreAsksNoHistoryRead()
	{
		StoreOpened.SetResult(true);
		Feed.Start();

		await WaitForLogAsync(NoStoredSpectrum);
		PassThrottle();

		SpectrumCalls.Should().ContainSingle();
		PointsCalls.Should().BeEmpty();
		Requests.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task AFailedStoreOpenAsksNoHistoryRead()
	{
		StoreOpened.SetResult(false);
		Feed.Start();

		await WaitForLogAsync(StoreNotOpened);
		PassThrottle();

		SpectrumCalls.Should().BeEmpty();
		PointsCalls.Should().BeEmpty();
		Requests.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task AKeyChangeDuringTheStartReadCancelsIt()
	{
		SpectrumResult = _ => Result.Ok<StoredSpectrum?>(StoredAt(Start.AddMinutes(-3), OtherNm));
		StoreOpened.SetResult(true);
		HoldPoints();
		Feed.Start();

		await PassThrottleUntilAPointsReadAsync();
		var held = PointsCalls.Single();
		held.WavelengthNm.Should().Be(OtherNm);

		AddAt(1, ThirdNm);
		PassThrottle();

		held.Token.IsCancellationRequested.Should().BeTrue();
		await Waits.UntilAsync(() => PointsCalls.Any(call => call.WavelengthNm == ThirdNm));
	}

	[AvaloniaTheory]
	[InlineData(StopPhase.BeforeTheStoreOpened)]
	[InlineData(StopPhase.DuringTheKeyRead)]
	public async Task StopDuringTheStartReadCancelsItAndAwaitsIt(StopPhase phase)
	{
		if (phase == StopPhase.DuringTheKeyRead)
		{
			SpectrumResult = _ => Result.Ok<StoredSpectrum?>(StoredAt(Start.AddMinutes(-3), OtherNm));
			StoreOpened.SetResult(true);
			HoldSpectrum();
		}

		Feed.Start();
		if (phase == StopPhase.BeforeTheStoreOpened)
		{
			await Feed.StopAsync().WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
			StoreOpened.SetResult(true);
			await Task.Delay(_settle, TestContext.Current.CancellationToken);
			SpectrumCalls.Should().BeEmpty("the stop ended the wait for the store");
			return;
		}

		await Waits.UntilAsync(() => SpectrumCalls.Count == 1);
		var stop = Feed.StopAsync();
		await Task.Delay(_settle, TestContext.Current.CancellationToken);
		SpectrumCalls.Single().Token.IsCancellationRequested.Should().BeTrue();
		stop.IsCompleted.Should().BeFalse("the stop awaits the held key read");

		ReleaseSpectrum();

		await stop.WaitAsync(Waits.Limit, TestContext.Current.CancellationToken);
		Requests.Should().BeEmpty("the cancelled key read asks for nothing");
	}

	[AvaloniaFact]
	public async Task AHistoryReadLogsItsLandingTimes()
	{
		SpectrumResult = _ => Result.Ok<StoredSpectrum?>(StoredAt(Start.AddMinutes(-3), OtherNm));
		PointsResult = call => Result.Ok(new IntensityChunk(
			call.From, [new TrendPoint(call.From.ToUnixTimeMilliseconds(), 1)]));
		StoreOpened.SetResult(true);
		Feed.Start();
		var plan = TrendHistoryPlan.For(Chart.From, Chart.To, Start, Start);

		await PassThrottleUntilAPointsReadAsync();

		await WaitForLogAsync("The trend history read for generation 1");
		var record = FeedLogger.Collector.GetSnapshot()
			.Single(entry => entry.Message.StartsWith("The trend history read", StringComparison.Ordinal));
		record.Level.Should().Be(LogLevel.Information);
		record.Message.Should().MatchRegex(
			$@"^The trend history read for generation 1 landed {plan.Count} points: "
			+ @"visible window after [\d.,]+ ms, last chunk after [\d.,]+ ms$");
	}

	/// <summary>A stored ATP-8000 spectrum at the wavelength and the sample delta, received at the moment.</summary>
	private static StoredSpectrum StoredAt(DateTimeOffset receivedAt, ushort nm)
	{
		var frame = StoreSamples.Spectrum(receivedAt, nm: nm).Frame;
		return new StoredSpectrum(frame.Measurement!, receivedAt, frame.Seq, frame.State);
	}

	/// <summary>The start read asks from a pool thread, so the throttle is passed until a points read starts.</summary>
	private Task PassThrottleUntilAPointsReadAsync()
	{
		return Waits.UntilAsync(() =>
		{
			PassThrottle();
			return !PointsCalls.IsEmpty;
		});
	}

	private Task WaitForLogAsync(string messageStart)
	{
		return Waits.UntilAsync(() => FeedLogger.Collector.GetSnapshot()
			.Any(entry => entry.Message.StartsWith(messageStart, StringComparison.Ordinal)));
	}
}
