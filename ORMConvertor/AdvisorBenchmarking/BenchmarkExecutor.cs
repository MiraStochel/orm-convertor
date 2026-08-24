using System;
using System.Collections.Generic;
using System.Diagnostics;
using DatabaseCatalog;
using Microsoft.Extensions.Logging;
using Model;
using System.Reflection;

namespace AdvisorBenchmarking;

public sealed class BenchmarkExecutor : IBenchmarkExecutor
{
    private readonly RoslynBenchmarkCompiler compiler = new();
    private readonly IReadOnlyList<Microsoft.CodeAnalysis.MetadataReference> references = BenchmarkReferenceProvider.GetStandardReferences();
    private readonly ILogger<BenchmarkExecutor>? logger;

    public BenchmarkExecutor(ILogger<BenchmarkExecutor>? logger = null)
    {
        this.logger = logger;
    }

    public BenchmarkMeasurement Execute(
        ORMEnum framework,
        IReadOnlyList<ConversionSource> sources,
        string connectionString,
        ICatalogReader catalogReader)
    {
        logger?.LogInformation("Benchmark start for framework {Framework} with {SourceCount} sources.", framework, sources.Count);

        var benchmarkSource = BenchmarkHarnessBuilder.Build(framework, sources, connectionString, catalogReader);
        // logger?.LogDebug("Generated benchmark source (first 10000 chars): {SourceSnippet}", Truncate(benchmarkSource.Source, 10000));

        var assemblyName = $"DynamicBenchmarks_{Guid.NewGuid():N}";

        using var compilation = compiler.Compile(benchmarkSource.Source, references, assemblyName);
        logger?.LogDebug("Compilation succeeded for assembly {AssemblyName}.", assemblyName);

        var benchmarkType = compilation.Assembly.GetType($"{benchmarkSource.Namespace}.{benchmarkSource.TypeName}")
            ?? throw new InvalidOperationException("Generated benchmark type could not be located.");

        var setup = benchmarkType.GetMethod("Setup");
        var cleanup = benchmarkType.GetMethod("Cleanup");
        var execute = benchmarkType.GetMethod("Query") ?? benchmarkType.GetMethod("Execute");

        if (execute == null)
        {
            throw new InvalidOperationException("Benchmark harness does not expose a Query/Execute method.");
        }

        var instance = Activator.CreateInstance(benchmarkType)
            ?? throw new InvalidOperationException("Failed to instantiate benchmark harness.");

        // Warm-up and measurement configuration
        const int warmupIterations = 2;         // non-measured iterations to stabilize JIT/caches
        const int minIterations = 3;            // ensure some averaging
        const int maxIterations = 20;           // avoid overlong DB runs
        const double targetTotalMs = 500;       // aim for ~0.5s total per benchmark

        int iterations = minIterations;
        long allocatedBefore = 0;
        long allocatedAfter = 0;
        var stopwatch = new Stopwatch();

        try
        {
            setup?.Invoke(instance, null);
            logger?.LogDebug("Setup invoked.");

            // Warm-ups (non-measured) to stabilize caches/JIT.
            for (int i = 0; i < warmupIterations; i++)
            {
                execute.Invoke(instance, null);
            }

            // Pilot run to estimate time/op and decide iteration count.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var pilotWatch = Stopwatch.StartNew();
            var pilotAllocBefore = GC.GetAllocatedBytesForCurrentThread();
            execute.Invoke(instance, null);
            var pilotAllocAfter = GC.GetAllocatedBytesForCurrentThread();
            pilotWatch.Stop();

            var pilotMs = Math.Max(0.01, pilotWatch.Elapsed.TotalMilliseconds);
            var suggestedIterations = (int)Math.Ceiling(targetTotalMs / pilotMs);
            if (suggestedIterations < minIterations) suggestedIterations = minIterations;
            if (suggestedIterations > maxIterations) suggestedIterations = maxIterations;
            iterations = suggestedIterations;
            logger?.LogDebug("Pilot: {PilotMs} ms/op, {PilotAlloc} bytes, chosen iterations {Iterations}.", pilotMs, Math.Max(0, pilotAllocAfter - pilotAllocBefore), iterations);

            // Final measured run
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            stopwatch.Start();

            for (int i = 0; i < iterations; i++)
            {
                execute.Invoke(instance, null);
            }

            stopwatch.Stop();
            allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            logger?.LogError(tie.InnerException, "Benchmark execution failed: {Message}", tie.InnerException.Message);
            throw new InvalidOperationException("Benchmark execution failed. See inner exception for details.", tie.InnerException);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Benchmark execution failed: {Message}", ex.Message);
            throw;
        }
        finally
        {
            try
            {
                cleanup?.Invoke(instance, null);
                logger?.LogDebug("Cleanup invoked.");
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Cleanup threw an exception.");
            }
        }

        double meanMilliseconds = stopwatch.Elapsed.TotalMilliseconds / Math.Max(1, iterations);
        long allocatedBytesPerOp = 0;
        try
        {
            var delta = allocatedAfter - allocatedBefore;
            if (iterations > 0 && delta > 0)
            {
                allocatedBytesPerOp = delta / iterations;
            }
        }
        catch
        {
            allocatedBytesPerOp = 0;
        }

        logger?.LogInformation("Benchmark finished: {Iterations} iters, mean {Mean} ms/op, allocated {Allocated} bytes/op.", iterations, meanMilliseconds, allocatedBytesPerOp);

        return new BenchmarkMeasurement(meanMilliseconds, allocatedBytesPerOp);
    }
}
