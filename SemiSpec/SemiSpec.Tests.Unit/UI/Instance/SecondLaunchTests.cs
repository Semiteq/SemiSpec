using System.Collections.Concurrent;
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

	private readonly string _name = $"SemiSpec-test-{Guid.NewGuid()}";

	[Fact]
	public void AnAnsweredSignalExitsWithZeroLogsOneLineAndShowsNothing()
	{
		using var scheduler = new EventLoopScheduler();
		using var primary = SingleInstance.Claim(_name, NullLogger.Instance);
		using var secondary = SingleInstance.Claim(_name, NullLogger.Instance);
		primary.Listen(scheduler, () => { }, NullLogger.Instance);
		var shown = new List<string>();
		var logger = new FakeLogger();

		var exitCode = SecondLaunch.Run(secondary, SecondLaunch.AnswerLimit, shown.Add, logger);

		exitCode.Should().Be(0);
		logger.Levels().Should().Equal(LogLevel.Information);
		shown.Should().BeEmpty();
	}

	[Theory]
	[InlineData("en")]
	[InlineData("ru")]
	public async Task NoAnswerAfterTheRetryShowsTheNotRespondingTextAndExitsWithOne(string culture)
	{
		var text = Resources.ResourceManager.GetString(
			nameof(Resources.SingleInstanceNotResponding),
			CultureInfo.GetCultureInfo(culture));
		var scheduler = new DroppingScheduler(dropped: int.MaxValue);
		using var primary = SingleInstance.Claim(_name, NullLogger.Instance);
		using var secondary = SingleInstance.Claim(_name, NullLogger.Instance);
		primary.Listen(scheduler, () => { }, NullLogger.Instance);
		var shown = new List<string>();
		var logger = new FakeLogger();

		int exitCode;
		using (new CultureScope(culture))
		{
			exitCode = SecondLaunch.Run(secondary, _noAnswerLimit, shown.Add, logger);
		}

		exitCode.Should().Be(1);
		shown.Should().Equal(text);
		logger.Levels().Should().Equal(LogLevel.Warning);
		await Waits.UntilAsync(() => scheduler.Calls == 2);
		scheduler.Calls.Should().Be(2);
	}

	[Fact]
	public void AnAnswerToTheRetryExitsWithZero()
	{
		var scheduler = new DroppingScheduler(dropped: 1);
		using var primary = SingleInstance.Claim(_name, NullLogger.Instance);
		using var secondary = SingleInstance.Claim(_name, NullLogger.Instance);
		var activations = 0;
		primary.Listen(scheduler, () => Interlocked.Increment(ref activations), NullLogger.Instance);
		var shown = new List<string>();
		var logger = new FakeLogger();

		var exitCode = SecondLaunch.Run(secondary, _noAnswerLimit, shown.Add, logger);

		exitCode.Should().Be(0);
		scheduler.Calls.Should().Be(2);
		Volatile.Read(ref activations).Should().Be(1);
		logger.Levels().Should().Equal(LogLevel.Information);
		shown.Should().BeEmpty();
	}

	[Fact]
	public void TwoSecondLaunchesAtOnceBothGetAnAnswer()
	{
		using var scheduler = new EventLoopScheduler();
		using var primary = SingleInstance.Claim(_name, NullLogger.Instance);
		using var first = SingleInstance.Claim(_name, NullLogger.Instance);
		using var second = SingleInstance.Claim(_name, NullLogger.Instance);
		primary.Listen(scheduler, () => { }, NullLogger.Instance);
		var shown = new ConcurrentQueue<string>();
		var loggers = new[] { new FakeLogger(), new FakeLogger() };
		var exitCodes = new int[2];
		using var start = new Barrier(2);
		var launches = new[] { first, second }
			.Select((instance, index) => new Thread(() =>
			{
				start.SignalAndWait();
				exitCodes[index] = SecondLaunch.Run(instance, SecondLaunch.AnswerLimit, shown.Enqueue, loggers[index]);
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

		exitCodes.Should().Equal(0, 0);
		shown.Should().BeEmpty();
		loggers.Should().AllSatisfy(logger => logger.Levels().Should().Equal(LogLevel.Information));
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
