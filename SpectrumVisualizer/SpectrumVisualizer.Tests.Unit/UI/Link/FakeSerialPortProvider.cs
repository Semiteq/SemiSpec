using FluentResults;

using SpectrumVisualizer.UI.Link;

namespace SpectrumVisualizer.Tests.Unit.UI.Link;

internal sealed class FakeSerialPortProvider : ISerialPortProvider
{
	// The test thread and the link's session thread both reach every field below; _gate guards them.
	private readonly Lock _gate = new();
	private readonly Queue<Result<Stream>> _openResults = new();
	private readonly List<string> _portNames = [];
	private int _openCount;
	private int _portListReads;
	private IError? _portNamesFailure;

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

	public int PortListReads
	{
		get
		{
			lock (_gate)
			{
				return _portListReads;
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

	public void FailPortNames(IError failure)
	{
		lock (_gate)
		{
			_portNamesFailure = failure;
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

	public Result<IReadOnlyList<string>> PortNames()
	{
		lock (_gate)
		{
			_portListReads++;

			return _portNamesFailure is { } failure
				? Result.Fail<IReadOnlyList<string>>(failure)
				: Result.Ok<IReadOnlyList<string>>([.. _portNames]);
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
