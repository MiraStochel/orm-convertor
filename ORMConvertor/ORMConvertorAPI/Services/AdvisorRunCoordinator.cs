using System;
using System.Collections.Generic;
using System.Linq;
using AdvisorBenchmarking;
using DatabaseCatalog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Model;
using AdvisorNamespace = Advisor.Advisor;
using OrmConvertor;
using ORMConvertorAPI.Dtos.Advisor;

namespace ORMConvertorAPI.Services;

public class AdvisorRunCoordinator : IAdvisorRunCoordinator
{
    private readonly IBenchmarkExecutor benchmarkExecutor;
    private readonly ILogger<AdvisorRunCoordinator> logger;
    private readonly string connectionString;

    private static readonly ORMEnum[] KnownFrameworks =
    [
        ORMEnum.Dapper,
        ORMEnum.NHibernate,
        ORMEnum.EFCore
    ];

    private static readonly ORMEnum[] SupportedFrameworks =
    [
        ORMEnum.Dapper,
        ORMEnum.EFCore
    ];

    public AdvisorRunCoordinator(
        IBenchmarkExecutor benchmarkExecutor,
        IConfiguration configuration,
        ILogger<AdvisorRunCoordinator> logger)
    {
        this.benchmarkExecutor = benchmarkExecutor ?? throw new ArgumentNullException(nameof(benchmarkExecutor));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        connectionString = configuration.GetConnectionString("AdvisorDatabase")
            ?? configuration["Advisor:ConnectionString"]
            ?? string.Empty;
    }

    /// <summary>
    /// Validates the request, resolves target frameworks, translates the workload,
    /// benchmarks it against the configured database, and runs the ILP optimization.
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

        foreach (var query in request.Queries)
        {
            if (string.IsNullOrWhiteSpace(query.Query?.Content))
            {
                throw new ArgumentException($"Query '{query.Id}' has no content.", nameof(request));
            }
        }

        logger.LogInformation("Advisor run received with {QueryCount} queries from source ORM {SourceOrm}.", request.Queries.Count, request.SourceOrm);

        var targetFrameworks = ResolveTargetFrameworks(request);
        if (targetFrameworks.Count == 0)
        {
            throw new InvalidOperationException("No supported target frameworks resolved for advisor run. Currently supported: Dapper, EFCore.");
        }
        logger.LogInformation("Target frameworks resolved: {Frameworks}.", targetFrameworks);

        var translations = BuildTranslations(
            request,
            targetFrameworks,
            cancellationToken);
        logger.LogInformation("Translations built for all queries.");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Advisor database connection string is not configured. " +
                "Set 'ConnectionStrings:AdvisorDatabase' (or 'Advisor:ConnectionString') " +
                "in appsettings.json or via environment variables (ConnectionStrings__AdvisorDatabase).");
        }

        // One caching reader for the whole run: every (query, framework) pair qualifies
        // the same entity set, so without the cache the same catalog batch would run
        // Q x F times (decision 015 is the seam, the cache its per-run lifetime).
        IReadOnlyDictionary<string, IReadOnlyDictionary<ORMEnum, BenchmarkMeasurement>> measurements;
        using (var catalogReader = new CachingCatalogReader(new SqlServerCatalogReader(connectionString)))
        {
            measurements = RunBenchmarks(
                request,
                targetFrameworks,
                translations,
                catalogReader,
                cancellationToken);
        }

        logger.LogInformation("Benchmarks completed for {QueryCount} queries.", request.Queries.Count);

        var result = ExecuteAdvisor(
            request,
            targetFrameworks,
            measurements,
            translations);

        return Task.FromResult(result);
    }

    /// <summary>
    /// Returns explicitly requested frameworks or falls back to the default supported list.
    /// </summary>
    private static IReadOnlyList<ORMEnum> ResolveTargetFrameworks(AdvisorRunRequest request)
    {
        IEnumerable<ORMEnum> candidates = request.TargetFrameworks is { Count: > 0 } explicitTargets
            ? explicitTargets
            : KnownFrameworks;

        var filtered = candidates
            .Where(f => SupportedFrameworks.Contains(f))
            .Distinct()
            .ToArray();

        return filtered;
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
                // Always run through ConversionHandler to normalize entities into EntityMaps
                // and emit one entity per source for the target framework (even if same as source).
                var sources = ComposeSources(request.Entities, query.Query);
                artifacts = ConversionHandler.Convert(
                    request.SourceOrm,
                    framework,
                    sources).Sources;

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

    private IReadOnlyDictionary<string, IReadOnlyDictionary<ORMEnum, BenchmarkMeasurement>> RunBenchmarks(
        AdvisorRunRequest request,
        IReadOnlyList<ORMEnum> targetFrameworks,
        IReadOnlyDictionary<string, IReadOnlyDictionary<ORMEnum, IReadOnlyList<ConversionSource>>> translations,
        ICatalogReader catalogReader,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, IReadOnlyDictionary<ORMEnum, BenchmarkMeasurement>>(StringComparer.Ordinal);

        foreach (var query in request.Queries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogDebug("Running benchmarks for query {QueryId}.", query.Id);

            if (!translations.TryGetValue(query.Id, out var frameworkSources))
            {
                throw new InvalidOperationException($"Missing translations for query '{query.Id}'.");
            }

            var perFramework = new Dictionary<ORMEnum, BenchmarkMeasurement>();
            foreach (var framework in targetFrameworks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!frameworkSources.TryGetValue(framework, out var sources))
                {
                    throw new InvalidOperationException($"Missing translation for query '{query.Id}' and framework '{framework}'.");
                }

                var measurement = benchmarkExecutor.Execute(framework, sources, connectionString, catalogReader);
                perFramework[framework] = measurement;
                logger.LogInformation("Benchmark {QueryId} on {Framework}: mean {Mean} ms, memory {Memory} bytes.", query.Id, framework, measurement.MeanDurationMilliseconds, measurement.AllocatedBytes);
            }

            results[query.Id] = perFramework;
        }

        return results;
    }

    private static AdvisorRunResult ExecuteAdvisor(
        AdvisorRunRequest request,
        IReadOnlyList<ORMEnum> targetFrameworks,
        IReadOnlyDictionary<string, IReadOnlyDictionary<ORMEnum, BenchmarkMeasurement>> measurements,
        IReadOnlyDictionary<string, IReadOnlyDictionary<ORMEnum, IReadOnlyList<ConversionSource>>> translations)
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

        long maxMemory = request.MaxMemoryBytes > 0
            ? request.MaxMemoryBytes
            : long.MaxValue;

        int status = AdvisorNamespace.Solve(
            mem,
            cost,
            weights,
            maxMemory,
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

        // Map measurements to DTO-friendly structure
        var dtoMeasurements = new Dictionary<string, IReadOnlyDictionary<ORMEnum, BenchmarkMeasurementDto>>(StringComparer.Ordinal);
        foreach (var (qid, perFramework) in measurements)
        {
            var map = new Dictionary<ORMEnum, BenchmarkMeasurementDto>();
            foreach (var (framework, m) in perFramework)
            {
                map[framework] = new BenchmarkMeasurementDto(m.MeanDurationMilliseconds, m.AllocatedBytes);
            }
            dtoMeasurements[qid] = map;
        }

        return new AdvisorRunResult(chosenFrameworks, assignments, dtoMeasurements, GroupTranslations(request, targetFrameworks, translations));
    }

    /// <summary>
    /// The translations of phase one regrouped for the response (decision 059): the
    /// entity artifacts once per framework - every query's conversion of the same entity
    /// inputs emits the same ones, which is the determinism of S2, not hope - and each
    /// query's own artifacts under the id its measurements use.
    /// </summary>
    private static IReadOnlyDictionary<ORMEnum, AdvisorTranslationsDto> GroupTranslations(
        AdvisorRunRequest request,
        IReadOnlyList<ORMEnum> targetFrameworks,
        IReadOnlyDictionary<string, IReadOnlyDictionary<ORMEnum, IReadOnlyList<ConversionSource>>> translations)
    {
        var result = new Dictionary<ORMEnum, AdvisorTranslationsDto>();

        foreach (var framework in targetFrameworks)
        {
            var entities = translations[request.Queries[0].Id][framework]
                .Where(s => !s.ContentType.IsQuery())
                .ToList();

            var queries = new Dictionary<string, IReadOnlyList<ConversionSource>>(StringComparer.Ordinal);
            foreach (var query in request.Queries)
            {
                queries[query.Id] = translations[query.Id][framework]
                    .Where(s => s.ContentType.IsQuery())
                    .ToList();
            }

            result[framework] = new AdvisorTranslationsDto(entities, queries);
        }

        return result;
    }
}
