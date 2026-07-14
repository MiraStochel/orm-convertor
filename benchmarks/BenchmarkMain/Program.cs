using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using DapperPerformance;
using EF6Performance;
using EFCorePerformance;
using Linq2dbPerformance;
using NHibernatePerformance;
using PetaPocoPerformance;
using RepoDBPerformance;

namespace BenchmarkMain;

internal class Program
{
    static void Main(string[] args)
    {
        var config = DefaultConfig.Instance;

        if (args.Contains("--testb") || false) // TESTING change to true
        {
            config = config.AddJob(
                Job.Default
                    .WithWarmupCount(1)
                    .WithIterationCount(3)
                    .WithEvaluateOverhead(false)
            );
        }

        config = config
            .AddExporter(CsvMeasurementsExporter.Default)
            .AddExporter(RPlotExporter.Default)
            .WithOption(ConfigOptions.JoinSummary, true)
            .AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByMethod);

        BenchmarkSwitcher
            .FromTypes([
                typeof(DapperBenchmark),
                typeof(PetaPocoBenchmark),
                typeof(Linq2dbBenchmarks),
                typeof(RepoDBBenchmark),
                typeof(EFCoreBenchmarks),
                typeof(EF6Benchmarks),
                typeof(NHibernateBenchmarks)
            ])
            .Run(args, config);
    }
}
