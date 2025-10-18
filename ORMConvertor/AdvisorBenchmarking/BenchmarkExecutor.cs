using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        string connectionString)
    {
        logger?.LogInformation("Benchmark start for framework {Framework} with {SourceCount} sources.", framework, sources.Count);

        var benchmarkSource = BenchmarkHarnessBuilder.Build(framework, sources, connectionString);
        logger?.LogDebug("Generated benchmark source (first 10000 chars): {SourceSnippet}", Truncate(benchmarkSource.Source, 10000));

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

        const int iterations = 5;
        long memoryBefore = 0;
        long memoryAfter = 0;
        var stopwatch = new Stopwatch();

        try
        {
            setup?.Invoke(instance, null);
            logger?.LogDebug("Setup invoked.");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
            stopwatch.Start();

            for (int i = 0; i < iterations; i++)
            {
                execute.Invoke(instance, null);
            }

            stopwatch.Stop();
            memoryAfter = GC.GetTotalMemory(forceFullCollection: false);
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

        double meanMilliseconds = stopwatch.Elapsed.TotalMilliseconds / iterations;
        long allocatedBytes = Math.Max(0, memoryAfter - memoryBefore);

        logger?.LogInformation("Benchmark finished: mean {Mean} ms, allocated {Allocated} bytes.", meanMilliseconds, allocatedBytes);

        return new BenchmarkMeasurement(meanMilliseconds, allocatedBytes);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";
}
