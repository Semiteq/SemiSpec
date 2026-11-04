using AwesomeAssertions;

using SpectrumVisualizer.UI.Link;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Link;

[Trait("Component", "UI")]
[Trait("Area", "Link")]
[Trait("Category", "Unit")]
public sealed class LinkHealthTests
{
	// The stamps are elapsed session time; the port opened 3 s into the session, so only differences matter.
	private static readonly TimeSpan _openedAt = TimeSpan.FromSeconds(3);

	/// <summary>Times are seconds after the port open; null means the event has not happened.</summary>
	public static TheoryData<double, double?, double?, long, LinkState> HealthRows => new()
	{
		{ 0, null, null, 0, LinkState.Waiting },
		{ 9.9, 5, null, 100, LinkState.Waiting },
		{ 12, 11, 11, 0, LinkState.Receiving },
		{ 20.9, 20, 11, 0, LinkState.Receiving },
		{ 10, null, null, 0, LinkState.NotResponding },
		{ 21, 11, 11, 0, LinkState.NotResponding },
		{ 10, 9, null, 4121, LinkState.OtherDevice },
		{ 25, 24, 11, 5000, LinkState.OtherDevice },
		{ 10, 9, null, 4120, LinkState.NotResponding },
		{ 25, 24, 11, 4120, LinkState.NotResponding },
		{ 25, 14, 11, 5000, LinkState.NotResponding },
		{ 25, 15, 11, 5000, LinkState.NotResponding },
	};

	public static TheoryData<double, double, double?, double?, bool> DrainRows => new()
	{
		{ 9.9, 0, null, 5, false },
		{ 10, 0, null, 5, true },
		{ 10, 0, null, null, false },
		{ 15, 5, null, 5, false },
		{ 15, 5, null, 6, true },
		{ 19.9, 5, 10, 18, false },
		{ 20, 5, 10, 18, true },
		{ 30, 5, 20, 29, true },
		{ 30, 5, 20, null, false },
	};

	[Theory]
	[MemberData(nameof(HealthRows))]
	public void EvaluateFollowsTheTenSecondWindowAndTheByteRule(
		double now,
		double? lastByteAt,
		double? lastFrameAt,
		long bytesSinceFrame,
		LinkState expected)
	{
		LinkHealth.Evaluate(At(now), _openedAt, At(lastByteAt), At(lastFrameAt), bytesSinceFrame)
			.Should().Be(expected);
	}

	[Fact]
	public void ABoardThatStopsMidFrameStaysNotResponding()
	{
		const double FrameAt = 2;
		const double TailAt = 2.5;
		const long TailBytes = 500;

		LinkHealth.Evaluate(At(FrameAt + 10), _openedAt, At(TailAt), At(FrameAt), TailBytes)
			.Should().Be(LinkState.NotResponding);
		LinkHealth.Evaluate(At(FrameAt + 30), _openedAt, At(TailAt), At(FrameAt), TailBytes)
			.Should().Be(LinkState.NotResponding);
	}

	[Theory]
	[MemberData(nameof(DrainRows))]
	public void ADrainIsDueTenSecondsAfterTheReferenceOrTheLastDrainWhenBytesArrived(
		double now,
		double reference,
		double? lastDrainAt,
		double? lastByteAt,
		bool expected)
	{
		LinkHealth.IsDrainDue(At(now), _openedAt + TimeSpan.FromSeconds(reference), At(lastDrainAt), At(lastByteAt))
			.Should().Be(expected);
	}

	private static TimeSpan At(double seconds)
	{
		return _openedAt + TimeSpan.FromSeconds(seconds);
	}

	private static TimeSpan? At(double? seconds)
	{
		return seconds is { } value ? At(value) : null;
	}
}
