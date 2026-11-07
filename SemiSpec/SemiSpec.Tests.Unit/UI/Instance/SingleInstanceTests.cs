using System.Diagnostics;
using System.Reactive.Concurrency;

using AwesomeAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Reactive.Testing;

using SemiSpec.Tests.Unit.UI.Messages;
using SemiSpec.UI.Instance;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Instance;

// Two claims on one thread behave as two processes: the claim reads createdNew and never waits on the mutex.
[Trait("Component", "UI")]
[Trait("Area", "Instance")]
[Trait("Category", "Unit")]
public sealed class SingleInstanceTests
{
	private const string ListenerThreadName = "primary UI";

	private static readonly TimeSpan _answerLimit = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan _noAnswerLimit = TimeSpan.FromMilliseconds(200);

	private readonly string _name = $"SemiSpec-test-{Guid.NewGuid()}";

	[Fact]
	public void TheFirstClaimOfANameIsPrimary()
	{
		using var primary = SingleInstance.Claim(_name, NullLogger.Instance);

		primary.IsPrimary.Should().BeTrue();
	}

	[Fact]
	public void ASecondClaimOfAHeldNameIsSecondary()
	{
		using var primary = SingleInstance.Claim(_name, NullLogger.Instance);
		using var secondary = SingleInstance.Claim(_name, NullLogger.Instance);

		secondary.IsPrimary.Should().BeFalse();
	}

	[Fact]
	public void AClaimTheOsRefusesRunsAsAPrimaryWithOneWarning()
	{
		// An event already holding the mutex name makes the Mutex constructor throw.
		using var occupant = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + _name);
		var logger = new FakeLogger();

		using var instance = SingleInstance.Claim(_name, logger);

		instance.IsPrimary.Should().BeTrue();
		logger.Levels().Should().Equal(LogLevel.Warning);
		logger.Failures().Should().ContainSingle().Which.Should().BeOfType<WaitHandleCannotBeOpenedException>();
	}

	[Fact]
	public void AClaimAfterThePrimaryAndTheSecondaryReleasedIsPrimary()
	{
		using var primary = SingleInstance.Claim(_name, NullLogger.Instance);
		using var secondary = SingleInstance.Claim(_name, NullLogger.Instance);
		primary.Release();
		secondary.Dispose();

		using var next = SingleInstance.Claim(_name, NullLogger.Instance);

		next.IsPrimary.Should().BeTrue();
	}

	[Fact]
	public void AClaimWhileASecondaryStillHoldsTheNameIsSecondary()
	{
		// The claim is the existence of a handle, so an open secondary keeps the name after the primary released it.
		using var primary = SingleInstance.Claim(_name, NullLogger.Instance);
		using var secondary = SingleInstance.Claim(_name, NullLogger.Instance);
		primary.Release();

		using var next = SingleInstance.Claim(_name, NullLogger.Instance);

		next.IsPrimary.Should().BeFalse();
	}

	[Fact]
	public void AReleasedPrimaryNoLongerAnswers()
	{
		using var scheduler = new EventLoopScheduler();
		using var primary = SingleInstance.Claim(_name, NullLogger.Instance);
		using var secondary = SingleInstance.Claim(_name, NullLogger.Instance);
		var activations = 0;
		primary.Listen(scheduler, () => Interlocked.Increment(ref activations), NullLogger.Instance);

		primary.Release();

		secondary.AskPrimary(_noAnswerLimit).Should().BeFalse();
		Volatile.Read(ref activations).Should().Be(0);
	}

	[Fact]
	public void ReleaseTwiceIsANoOp()
	{
		using var primary = SingleInstance.Claim(_name, NullLogger.Instance);
		primary.Release();

		var releaseAgain = primary.Release;

		releaseAgain.Should().NotThrow();
	}

	[Fact]
	public void ASignalRunsTheActivationOnThePrimarySchedulerAndIsAnswered()
	{
		using var scheduler = new EventLoopScheduler(start => new Thread(start) { Name = ListenerThreadName });
		using var primary = SingleInstance.Claim(_name, NullLogger.Instance);
		using var secondary = SingleInstance.Claim(_name, NullLogger.Instance);
		var activationThreads = new List<string?>();
		primary.Listen(scheduler, () => activationThreads.Add(Thread.CurrentThread.Name), NullLogger.Instance);
		var elapsed = Stopwatch.StartNew();

		var answered = secondary.AskPrimary(_answerLimit);

		answered.Should().BeTrue();
		elapsed.Elapsed.Should().BeLessThan(_answerLimit);
		activationThreads.Should().Equal(ListenerThreadName);
	}

	[Fact]
	public void APrimaryWhoseSchedulerNeverRunsIsNoAnswer()
	{
		var scheduler = new TestScheduler();
		using var primary = SingleInstance.Claim(_name, NullLogger.Instance);
		using var secondary = SingleInstance.Claim(_name, NullLogger.Instance);
		var activations = 0;
		primary.Listen(scheduler, () => activations++, NullLogger.Instance);

		secondary.AskPrimary(_noAnswerLimit).Should().BeFalse();
		activations.Should().Be(0);
	}

	[Fact]
	public async Task AStaleAnswerDoesNotAnswerTheNextSignal()
	{
		using var scheduler = new EventLoopScheduler();
		using var primary = SingleInstance.Claim(_name, NullLogger.Instance);
		using var secondary = SingleInstance.Claim(_name, NullLogger.Instance);
		// Each activation hangs until the test lets one through, as a busy UI thread would.
		using var proceed = new SemaphoreSlim(0);
		var activations = 0;
		primary.Listen(
			scheduler,
			() =>
			{
				proceed.Wait();
				Interlocked.Increment(ref activations);
			},
			NullLogger.Instance);
		secondary.AskPrimary(_noAnswerLimit).Should().BeFalse();
		proceed.Release();
		await Waits.UntilAsync(() => Volatile.Read(ref activations) == 1);
		// The scheduler runs in order, so once this job ran the late activation has set the answer.
		using var answerSet = new ManualResetEventSlim();
		scheduler.Schedule(answerSet.Set);
		answerSet.Wait(Waits.Limit, TestContext.Current.CancellationToken).Should().BeTrue();

		var answered = secondary.AskPrimary(_noAnswerLimit);

		answered.Should().BeFalse();
		Volatile.Read(ref activations).Should().Be(1);
		proceed.Release();
	}

	[Fact]
	public void AnActivationThatThrowsIsStillAnswered()
	{
		using var scheduler = new EventLoopScheduler();
		using var primary = SingleInstance.Claim(_name, NullLogger.Instance);
		using var secondary = SingleInstance.Claim(_name, NullLogger.Instance);
		var logger = new FakeLogger();
		var failure = new InvalidOperationException("the window refused activation");
		primary.Listen(scheduler, () => throw failure, logger);

		var answered = secondary.AskPrimary(_answerLimit);

		answered.Should().BeTrue();
		logger.Levels().Should().Equal(LogLevel.Error);
		logger.Failures().Should().Equal(failure);
	}
}
