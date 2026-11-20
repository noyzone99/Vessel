using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using Vessel.Benchmark;

var full = args.Contains("--full", StringComparer.Ordinal);
if (full && args.Contains("--quick", StringComparer.Ordinal))
{
    Console.Error.WriteLine("Choose either --quick or --full.");
    return 2;
}

var benchmarkArgs = args.Where(arg => arg is not ("--full" or "--quick")).ToList();
if (!benchmarkArgs.Any(arg => arg is "--filter" or "-f" or "--list" or "--help" or "-h" ||
    arg.StartsWith("--filter=", StringComparison.Ordinal)))
    benchmarkArgs.AddRange(["--filter", "*"]);

var config = ManualConfig.Create(DefaultConfig.Instance);
if (!full)
{
    config.AddJob(Job.ShortRun
        .WithId("Quick")
        .WithToolchain(InProcessNoEmitToolchain.Default)
        .WithWarmupCount(1)
        .WithIterationCount(3)
        .WithInvocationCount(1)
        .WithUnrollFactor(1));
}

var type = full ? typeof(FullBenchmarks) : typeof(QuickBenchmarks);
var summaries = BenchmarkSwitcher.FromTypes([type]).Run(benchmarkArgs.ToArray(), config).ToArray();
return summaries.Any(summary => summary.HasCriticalValidationErrors || summary.Reports.Any(report => !report.Success)) ? 1 : 0;
