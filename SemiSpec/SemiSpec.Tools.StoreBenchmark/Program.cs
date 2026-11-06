namespace SemiSpec.Tools.StoreBenchmark;

public static class Program
{
	public static Task<int> Main(string[] arguments)
	{
		return BenchmarkCommand.Build((options, cancellationToken) =>
			BenchmarkRun.RunAsync(options, Console.Out, cancellationToken)).Parse(arguments).InvokeAsync();
	}
}
