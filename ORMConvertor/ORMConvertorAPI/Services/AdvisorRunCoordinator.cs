using System;
using System.Linq;
using Model;
using AdvisorNamespace = Advisor.Advisor;
using OrmConvertor;
using ORMConvertorAPI.Dtos.Advisor;

namespace ORMConvertorAPI.Services;

public class AdvisorRunCoordinator : IAdvisorRunCoordinator
{
    private static readonly ORMEnum[] KnownFrameworks =
    [
        ORMEnum.Dapper,
        ORMEnum.NHibernate,
        ORMEnum.EFCore
    ];

    /// <summary>
    /// Validates the request, resolves target frameworks, and prepares translated artifacts.
    /// Benchmark execution and optimisation will be plugged in subsequently.
    /// </summary>
    public Task<AdvisorRunResult> RunAsync(
        AdvisorRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Entities);
        ArgumentNullException.ThrowIfNull(request.Queries);

        if (request.Queries.Count == 0)
        {
            throw new ArgumentException("At least one query is required", nameof(request));
        }

        var targetFrameworks = ResolveTargetFrameworks(request);
        if (targetFrameworks.Count == 0)
        {
            throw new InvalidOperationException("No target frameworks resolved for advisor run.");
        }

        var translations = BuildTranslations(
            request,
            targetFrameworks,
            cancellationToken);

        var measurements = RunMockBenchmarks(
            request,
            targetFrameworks,
            translations,
            cancellationToken);

        var result = ExecuteAdvisor(
            request,
            targetFrameworks,
            measurements);

        return Task.FromResult(result);
    }

    /// <summary>
    /// Returns explicitly requested frameworks or falls back to the default supported list.
    /// </summary>
    private static IReadOnlyList<ORMEnum> ResolveTargetFrameworks(AdvisorRunRequest request)
    {
        if (request.TargetFrameworks is { Count: > 0 } explicitTargets)
        {
            return explicitTargets
                .Distinct()
                .ToArray();
        }

        return KnownFrameworks;
    }

    /// <summary>
    /// Produces per-query conversion outputs for each target framework using the existing converter.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<ORMEnum, IReadOnlyList<ConversionSource>>> BuildTranslations(
        AdvisorRunRequest request,
        IReadOnlyList<ORMEnum> targetFrameworks,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<ORMEnum, IReadOnlyList<ConversionSource>>>(StringComparer.Ordinal);

        foreach (var query in request.Queries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var perFramework = new Dictionary<ORMEnum, IReadOnlyList<ConversionSource>>();

            foreach (var framework in targetFrameworks)
            {
                IReadOnlyList<ConversionSource> artifacts;
                if (framework == request.SourceOrm)
                {
                    artifacts = ComposeSources(request.Entities, query.Query);
                }
                else
                {
                    var sources = ComposeSources(request.Entities, query.Query);
                    artifacts = ConversionHandler.Convert(
                        request.SourceOrm,
                        framework,
                        sources);
                }

                perFramework[framework] = artifacts;
            }

            result[query.Id] = perFramework;
        }

        return result;
    }

    /// <summary>
    /// Clones the shared entity inputs and appends the query so each conversion has its own copy.
    /// </summary>
    private static List<ConversionSource> ComposeSources(
        IReadOnlyList<ConversionSource> entities,
        ConversionSource query)
    {
        var combined = new List<ConversionSource>(entities.Count + 1);
        foreach (var entity in entities)
        {
            combined.Add(Clone(entity));
        }

        combined.Add(Clone(query));
        return combined;
    }

    /// <summary>
    /// Creates a defensive copy of the provided conversion source.
    /// </summary>
    private static ConversionSource Clone(ConversionSource source) =>
        new()
        {
            ContentType = source.ContentType,
            Content = source.Content
        };

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<ORMEnum, MockBenchmarkResult>> RunMockBenchmarks(
        AdvisorRunRequest request,
        IReadOnlyList<ORMEnum> targetFrameworks,
        IReadOnlyDictionary<string, IReadOnlyDictionary<ORMEnum, IReadOnlyList<ConversionSource>>> translations,
        CancellationToken cancellationToken)
    {
        var rng = Random.Shared;
        var results = new Dictionary<string, IReadOnlyDictionary<ORMEnum, MockBenchmarkResult>>(StringComparer.Ordinal);

        foreach (var query in request.Queries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var perFramework = new Dictionary<ORMEnum, MockBenchmarkResult>();
            foreach (var framework in targetFrameworks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Produce deterministic-ish values seeded by query/framework to keep runs predictable.
                var hash = HashCode.Combine(query.Id, (int)framework);
                var duration = 5.0 + Math.Abs(hash % 2000) / 10.0; // 5ms to ~205ms
                var memory = 1024L * (10 + Math.Abs(hash % 512));  // Between ~10KB and ~522KB

                perFramework[framework] = new MockBenchmarkResult(duration, memory);
            }

            results[query.Id] = perFramework;
        }

        return results;
    }

    private static AdvisorRunResult ExecuteAdvisor(
        AdvisorRunRequest request,
        IReadOnlyList<ORMEnum> targetFrameworks,
        IReadOnlyDictionary<string, IReadOnlyDictionary<ORMEnum, MockBenchmarkResult>> measurements)
    {
        int queryCount = request.Queries.Count;
        int frameworkCount = targetFrameworks.Count;

        var cost = new double[queryCount * frameworkCount];
        var mem = new long[queryCount * frameworkCount];
        var weights = new int[queryCount];

        for (int qi = 0; qi < queryCount; qi++)
        {
            var query = request.Queries[qi];
            weights[qi] = Math.Max(1, query.Weight);
            var queryMeasurements = measurements[query.Id];

            for (int fi = 0; fi < frameworkCount; fi++)
            {
                var framework = targetFrameworks[fi];
                var m = queryMeasurements[framework];
                int index = (qi * frameworkCount) + fi;
                cost[index] = m.MeanDurationMilliseconds;
                mem[index] = m.AllocatedBytes;
            }
        }

        int[] selected = new int[frameworkCount];
        int[] assignment = new int[queryCount];

        int status = AdvisorNamespace.Solve(
            mem,
            cost,
            weights,
            request.MaxMemoryBytes,
            request.MaxFrameworksToSelect,
            queryCount,
            frameworkCount,
            out int objective,
            selected,
            assignment);

        if (status != 0)
        {
            throw new InvalidOperationException($"Advisor solver failed with status code {status}.");
        }

        var chosenFrameworks = new List<ORMEnum>();
        for (int fi = 0; fi < frameworkCount; fi++)
        {
            if (selected[fi] > 0)
            {
                chosenFrameworks.Add(targetFrameworks[fi]);
            }
        }

        var assignments = new Dictionary<string, ORMEnum>(StringComparer.Ordinal);
        for (int qi = 0; qi < queryCount; qi++)
        {
            int frameworkIndex = assignment[qi];
            if (frameworkIndex < 0 || frameworkIndex >= frameworkCount)
            {
                continue;
            }

            assignments[request.Queries[qi].Id] = targetFrameworks[frameworkIndex];
        }

        return new AdvisorRunResult(objective, chosenFrameworks, assignments);
    }

    private sealed record MockBenchmarkResult(double MeanDurationMilliseconds, long AllocatedBytes);
}
