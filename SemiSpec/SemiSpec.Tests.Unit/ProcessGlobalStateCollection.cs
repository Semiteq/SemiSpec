using Xunit;

namespace SemiSpec.Tests.Unit;

// A class that writes process-global state (Avalonia's Logger.Sink) joins this collection, which runs alone.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessGlobalStateCollection
{
	public const string Name = "process-global-state";
}
