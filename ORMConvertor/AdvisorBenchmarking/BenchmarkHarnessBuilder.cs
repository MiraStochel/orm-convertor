using System;
using System.Collections.Generic;
using DatabaseCatalog;
using Microsoft.Data.SqlClient;
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

        return generator(sources, WithExecutionTimeout(connectionString), catalogReader);
    }

    /// <summary>
    /// The connection string a harness opens its connection with: the caller's, with the
    /// time limit of one execution (decision 146) as its <c>Command Timeout</c>, which every
    /// command created over the connection takes as its default - Dapper, EF Core and
    /// NHibernate alike, none of which sets a timeout of its own unless configured to.
    /// </summary>
    internal static string WithExecutionTimeout(string connectionString)
        => new SqlConnectionStringBuilder(connectionString) { CommandTimeout = BenchmarkPolicy.ExecutionTimeoutSeconds }.ConnectionString;
}

