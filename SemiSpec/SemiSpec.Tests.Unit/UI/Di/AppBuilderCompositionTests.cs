using System.Reactive;

using Avalonia.Logging;

using AwesomeAssertions;

using SemiSpec.UI;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Di;

/// <summary>
/// Holds <see cref="App.BuildAvaloniaApp"/> on Win32, Skia and a text shaper. <c>LogToTrace()</c> writes
/// <c>Logger.Sink</c>, so the sink is restored around the call.
/// </summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class AppBuilderCompositionTests
{
	[Fact]
	public void BuildAvaloniaAppRegistersWin32SkiaAndTextShaping()
	{
		var sink = Logger.Sink;

		try
		{
			var builder = App.BuildAvaloniaApp(Observer.Create<Exception>(_ => { }));

			builder.TextShapingSubsystemInitializer.Should().NotBeNull();
			builder.WindowingSubsystemName.Should().Be("Win32");
			builder.RenderingSubsystemName.Should().Be("Skia");
		}
		finally
		{
			Logger.Sink = sink;
		}
	}
}
