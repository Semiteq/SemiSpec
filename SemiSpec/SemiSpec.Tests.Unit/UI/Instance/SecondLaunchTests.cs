using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;

using AwesomeAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;

using SemiSpec.Tests.Unit.UI.Messages;
using SemiSpec.UI.Instance;
using SemiSpec.UI.Localization;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Instance;

[Trait("Component", "UI")]
[Trait("Area", "Instance")]
[Trait("Category", "Unit")]
public sealed class SecondLaunchTests
{
	private static readonly TimeSpan _noAnswerLimit = TimeSpan.FromMilliseconds(200);
	private static readonly TimeSpan _shortBudget = TimeSpan.FromMilliseconds(600);

	private readonly string _name = $"SemiSpec-test-{Guid.NewGuid()}";

	[Fact]
	public void AnAnsweredSignalExitsWithZeroLogsOneLineAndShowsNothing()
	{
		using var scheduler = new EventLoopScheduler();
		using var primary = Claim();
		var secondary = Claim();
		primary.Listen(scheduler, () => { }, NullLogger.Instance);
		var shown = new List<string>();
		var logger = new FakeLogger();

		var outcome = SecondLaunch.Run(
			secondary, Claim, SecondLaunch.AnswerLimit, SecondLaunch.Budget, shown.Add, logger);

		outcome.Should().Be(SecondLaunchOutcome.Exit(0));
		logger.Levels().Should().Equal(LogLevel.Information);
		shown.Should().BeEmpty();
	}

	[Theory]
	[InlineData("en")]
	[InlineData("ru")]
	public async Task NoAnswerWithinTheBudgetShowsTheNotRespondingTextAndExitsWithOne(string culture)
	{
		var text = Resources.ResourceManager.GetString(
			nameof(Resources.SingleInstanceNotResponding),
			CultureInfo.GetCultureInfo(culture));
		var scheduler = new DroppingScheduler(dropped: int.MaxValue);
		using var primary = Claim();
		var secondary = Claim();
		primary.Listen(scheduler, () => { }, NullLogger.Instance);
		var shown = new List<string>();
		var logger = new FakeLogger();
		var elapsed = Stopwatch.StartNew();

		SecondLaunchOutcome outcome;
		using (new CultureScope(culture))
		{
			outcome = SecondLaunch.Run(secondary, Claim, _noAnswerLimit, _shortBudget, shown.Add, logger);
		}

		outcome.Should().Be(SecondLaunchOutcome.Exit(1));
		elapsed.Elapsed.Should().BeGreaterThanOrEqualTo(_shortBudget);
		shown.Should().Equal(text);
		logger.Levels().Should().Equal(LogLevel.Warning);
		await Waits.UntilAsync(() => scheduler.Calls >= 2);
	}

	[Fact]
	public void TheMessageShowsWithEveryHandleOfTheLaunchClosed()
	{
		var scheduler = new DroppingScheduler(dropped: int.MaxValue);
		using var primary = Claim();
		var secondary = Claim();
		primary.Listen(scheduler, () => { }, NullLogger.Instance);
		bool? restartIsPrimary = null;

		// The operator's remedy, run while the message is up: the hung copy is killed, SemiSpec started again.
		void KillThePrimaryAndStartAgain(string _)
		{
			primary.Release();
			using var restart = Claim();
			restartIsPrimary = restart.IsPrimary;
		}

		var outcome = SecondLaunch.Run(
			secondary, Claim, _noAnswerLimit, _shortBudget, KillThePrimaryAndStartAgain, NullLogger.Instance);

		outcome.Should().Be(SecondLaunchOutcome.Exit(1));
		restartIsPrimary.Should().BeTrue();
	}

	[Fact]
	public void ANameFreedAfterAnUnansweredAskContinuesAsThePrimary()
	{
		using var primary = Claim();
		var secondary = Claim();
		// A closing copy has released its claim; the secondary's own handles still keep the name until it re-claims.
		primary.Release();
		var shown = new List<string>();
		var logger = new FakeLogger();

		var outcome = SecondLaunch.Run(secondary, Claim, _noAnswerLimit, _shortBudget, shown.Add, logger);

		using var continued = outcome.Primary;
		continued.Should().NotBeNull();
		continued!.IsPrimary.Should().BeTrue();
		logger.Levels().Should().Equal(LogLevel.Information);
		shown.Should().BeEmpty();
	}

	[Fact]
	public void AnAnswerInALaterRoundExitsWithZero()
	{
		var scheduler = new DroppingScheduler(dropped: 1);
		using var primary = Claim();
		var secondary = Claim();
		var activations = 0;
		primary.Listen(scheduler, () => Interlocked.Increment(ref activations), NullLogger.Instance);
		var shown = new List<string>();
		var logger = new FakeLogger();

		var outcome = SecondLaunch.Run(secondary, Claim, _noAnswerLimit, SecondLaunch.Budget, shown.Add, logger);

		outcome.Should().Be(SecondLaunchOutcome.Exit(0));
		scheduler.Calls.Should().Be(2);
		Volatile.Read(ref activations).Should().Be(1);
		logger.Levels().Should().Equal(LogLevel.Information);
		shown.Should().BeEmpty();
	}

	[Fact]
	public void TwoSecondLaunchesAtOnceBothGetAnAnswerByTheSecondRound()
	{
		// Coalesced asks or a reset that clears the other launch's answer leave one launch unanswered in its first
		// round. A budget of two answer limits allows exactly two unanswered rounds, so both exit codes of 0 prove
		// that the second round recovers the lost answer.
		var twoRounds = SecondLaunch.AnswerLimit * 2;
		using var scheduler = new EventLoopScheduler();
		using var primary = Claim();
		var launchClaims = new[] { Claim(), Claim() };
		primary.Listen(scheduler, () => { }, NullLogger.Instance);
		var shown = new ConcurrentQueue<string>();
		var loggers = new[] { new FakeLogger(), new FakeLogger() };
		var outcomes = new SecondLaunchOutcome?[2];
		using var start = new Barrier(2);
		var launches = launchClaims
			.Select((instance, index) => new Thread(() =>
			{
				start.SignalAndWait();
				outcomes[index] = SecondLaunch.Run(
					instance, Claim, SecondLaunch.AnswerLimit, twoRounds, shown.Enqueue, loggers[index]);
			}))
			.ToArray();

		foreach (var launch in launches)
		{
			launch.Start();
		}

		foreach (var launch in launches)
		{
			launch.Join();
		}

		outcomes.Should().AllSatisfy(outcome => outcome.Should().Be(SecondLaunchOutcome.Exit(0)));
		shown.Should().BeEmpty();
		loggers.Should().AllSatisfy(logger => logger.Levels().Should().Equal(LogLevel.Information));
	}

	private SingleInstance Claim()
	{
		return SingleInstance.Claim(_name, NullLogger.Instance);
	}

	/// <summary>Drops the first scheduled activations, as a hung UI thread would, and runs the rest at once.</summary>
	private sealed class DroppingScheduler(int dropped) : IScheduler
	{
		private int _calls;

		public int Calls => Volatile.Read(ref _calls);

		public DateTimeOffset Now => DateTimeOffset.Now;

		public IDisposable Schedule<TState>(TState state, Func<IScheduler, TState, IDisposable> action)
		{
			return Interlocked.Increment(ref _calls) <= dropped ? Disposable.Empty : action(this, state);
		}

		public IDisposable Schedule<TState>(TState state, TimeSpan dueTime, Func<IScheduler, TState, IDisposable> action)
		{
			throw new NotSupportedException();
		}

		public IDisposable Schedule<TState>(
			TState state,
			DateTimeOffset dueTime,
			Func<IScheduler, TState, IDisposable> action)
		{
			throw new NotSupportedException();
		}
	}
}
