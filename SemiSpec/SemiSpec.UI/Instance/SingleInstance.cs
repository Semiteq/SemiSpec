using System.Reactive.Concurrency;

using Microsoft.Extensions.Logging;

namespace SemiSpec.UI.Instance;

/// <summary>
/// The claim on one running copy per Windows session, and the named ask and answer events through which a
/// second launch has the first copy activate its window.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
	public const string ProductionName = "SemiSpec-f1d02707-fb8a-4db5-81ea-a5249a5b36e3";

	private const string SessionNamespace = @"Local\";

	// Guards the handles below: Release runs on the UI thread, the answer on the primary's UI scheduler.
	private readonly Lock _gate = new();

	private Mutex? _claim;
	private EventWaitHandle? _ask;
	private EventWaitHandle? _answer;
	private RegisteredWaitHandle? _listener;

	private SingleInstance(Mutex? claim, EventWaitHandle? ask, EventWaitHandle? answer, bool isPrimary)
	{
		_claim = claim;
		_ask = ask;
		_answer = answer;
		IsPrimary = isPrimary;
	}

	public bool IsPrimary { get; }

	/// <summary>A claim the OS refuses runs as an unguarded primary: a lost guard beats an app that cannot start.</summary>
	public static SingleInstance Claim(string name, ILogger logger)
	{
		Mutex? claim = null;
		EventWaitHandle? ask = null;
		try
		{
			claim = new Mutex(false, SessionNamespace + name, out var createdNew);
			ask = new EventWaitHandle(false, EventResetMode.AutoReset, SessionNamespace + name + "-activate");
			var answer = new EventWaitHandle(false, EventResetMode.AutoReset, SessionNamespace + name + "-activated");

			return new SingleInstance(claim, ask, answer, createdNew);
		}
		catch (Exception exception) when (exception is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
		{
			ask?.Dispose();
			claim?.Dispose();
			logger.LogWarning(exception, "Claiming the single-instance name {Name} failed; this copy runs unguarded", name);

			return new SingleInstance(null, null, null, isPrimary: true);
		}
	}

	/// <summary>Each ask runs activate on the UI scheduler, then answers, so the answer proves the UI thread runs.</summary>
	public void Listen(IScheduler uiScheduler, Action activate, ILogger logger)
	{
		lock (_gate)
		{
			if (_ask is not { } ask)
			{
				return;
			}

			_listener = ThreadPool.RegisterWaitForSingleObject(
				ask,
				(_, _) => uiScheduler.Schedule(() => ActivateAndAnswer(activate, logger)),
				state: null,
				Timeout.Infinite,
				executeOnlyOnce: false);
		}
	}

	private void ActivateAndAnswer(Action activate, ILogger logger)
	{
		try
		{
			activate();
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Activating the window for a second launch failed");
		}

		lock (_gate)
		{
			_answer?.Set();
		}
	}

	/// <summary>
	/// Clears any late answer to an earlier ask first, so only an answer to this ask counts. A claim with no
	/// events, unguarded or released, has no primary to ask.
	/// </summary>
	public bool AskPrimary(TimeSpan limit)
	{
		EventWaitHandle answer;
		lock (_gate)
		{
			if (_ask is null || _answer is null)
			{
				return false;
			}

			answer = _answer;
			answer.Reset();
			_ask.Set();
		}

		return answer.WaitOne(limit);
	}

	/// <summary>Stops answering and gives up the name, so a later launch claims it as a new primary.</summary>
	public void Release()
	{
		lock (_gate)
		{
			_listener?.Unregister(null);
			_listener = null;
			_ask?.Dispose();
			_ask = null;
			_answer?.Dispose();
			_answer = null;
			_claim?.Dispose();
			_claim = null;
		}
	}

	public void Dispose()
	{
		Release();
	}
}
