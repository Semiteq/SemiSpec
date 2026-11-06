using System.Reactive.Concurrency;
using System.Threading.Channels;

using Microsoft.Extensions.Logging.Abstractions;

using SemiSpec.Core.Protocol;
using SemiSpec.UI.Link;
using SemiSpec.UI.Messages;
using SemiSpec.UI.Store;

namespace SemiSpec.Tests.Unit.UI.Store;

/// <summary>
/// A store nobody starts, for view models that only read its drop count: it opens no file and counts 0.
/// </summary>
internal sealed class UnstartedStore : IDisposable
{
	private readonly FrameDistributor _distributor;

	public UnstartedStore(MessagePanelViewModel messagePanel)
	{
		_distributor = new FrameDistributor(
			Channel.CreateUnbounded<ReceivedFrame>().Reader,
			messagePanel,
			ImmediateScheduler.Instance,
			NullLogger<FrameDistributor>.Instance);
		Store = new SpectrumStore(
			_distributor,
			new StoreFile(Path.Combine(Path.GetTempPath(), "semispec-never-opened.db")),
			messagePanel,
			ImmediateScheduler.Instance,
			TimeProvider.System,
			NullLogger<SpectrumStore>.Instance);
	}

	public SpectrumStore Store { get; }

	public void Dispose()
	{
		Store.Dispose();
		_distributor.Dispose();
	}
}
