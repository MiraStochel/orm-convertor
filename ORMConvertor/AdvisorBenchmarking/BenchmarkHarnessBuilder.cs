using System;
using System.Collections.Generic;
using DatabaseCatalog;
using Model;

namespace AdvisorBenchmarking;

internal static class BenchmarkHarnessBuilder
{
    private static readonly IReadOnlyDictionary<ORMEnum, Func<IReadOnlyList<ConversionSource>, string, ICatalogReader, BenchmarkSource>> Generators =
        new Dictionary<ORMEnum, Func<IReadOnlyList<ConversionSource>, string, ICatalogReader, BenchmarkSource>>
        {
            [ORMEnum.Dapper] = DapperBenchmarkHarnessBuilder.Build,
            [ORMEnum.EFCore] = EFCoreBenchmarkHarnessBuilder.Build
        };

    public static BenchmarkSource Build(
        ORMEnum framework,
        IReadOnlyList<ConversionSource> sources,
        string connectionString,
        ICatalogReader catalogReader)
    {
        if (!Generators.TryGetValue(framework, out var generator))
        {
            throw new NotSupportedException($"Benchmark harness for framework {framework} is not implemented yet.");
        }

        return generator(sources, connectionString, catalogReader);
    }
}

