using FluentResults;

using SpectrumVisualizer.UI.Link;

namespace SpectrumVisualizer.Tests.Unit.UI.Link;

/// <summary>Hands out the open results a test queues, counts opens and lists the ports a test sets.</summary>
internal sealed class FakeSerialPortProvider : ISerialPortProvider
{
	// The test thread and the link's session thread both reach every field below; _gate guards them.
	private readonly Lock _gate = new();
	private readonly Queue<Result<Stream>> _openResults = new();
	private readonly List<string> _portNames = [];
	private int _openCount;

	public FakeSerialPortProvider(params string[] portNames)
	{
		_portNames.AddRange(portNames);
	}

	public int OpenCount
	{
		get
		{
			lock (_gate)
			{
				return _openCount;
			}
		}
	}

	public void SetPorts(params string[] portNames)
	{
		lock (_gate)
		{
			_portNames.Clear();
			_portNames.AddRange(portNames);
		}
	}

	public void QueueOpen(Stream stream)
	{
		QueueOpen(Result.Ok(stream));
	}

	public void QueueOpenFailure(IError error)
	{
		QueueOpen(Result.Fail<Stream>(error));
	}

	public IReadOnlyList<string> PortNames()
	{
		lock (_gate)
		{
			return [.. _portNames];
		}
	}

	public Result<Stream> Open(string portName)
	{
		lock (_gate)
		{
			_openCount++;

			return _openResults.TryDequeue(out var result)
				? result
				: throw new InvalidOperationException($"No open result is queued for {portName}.");
		}
	}

	private void QueueOpen(Result<Stream> result)
	{
		lock (_gate)
		{
			_openResults.Enqueue(result);
		}
	}
}
