using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        var warmups = new List<double>();
        var measured = new List<double>();
        long allocatedBefore = 0;
        long allocatedAfter = 0;

        try
        {
            setup?.Invoke(instance, null);
            logger?.LogDebug("Setup invoked.");

            // Warm-up (decision 145): until two consecutive executions agree, at most
            // MaxWarmupIterations. The first execution of a query after a start of the
            // server also reads its pages from disk; later cells of the same query find them.
            do
            {
                warmups.Add(TimeOne(execute, instance));
            }
            while (warmups.Count < 2
                || (warmups.Count < BenchmarkPolicy.MaxWarmupIterations && !Agree(warmups[^2], warmups[^1])));

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Measured loop: until the accumulated time reaches the target, at least
            // MinIterations times. Each execution is timed on its own so that the median
            // of one execution can be reported.
            allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            double total = 0;
            while (measured.Count < BenchmarkPolicy.MinIterations || total < BenchmarkPolicy.TargetMilliseconds)
            {
                var ms = TimeOne(execute, instance);
                measured.Add(ms);
                total += ms;
            }

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

        double median = Median(measured);
        long allocatedBytesPerOp = Math.Max(0, allocatedAfter - allocatedBefore) / measured.Count;

        logger?.LogInformation(
            "Benchmark finished: {Warmups} warm-ups, {Iterations} iterations, median {Median} ms/op, allocated {Allocated} bytes/op.",
            warmups.Count, measured.Count, median, allocatedBytesPerOp);

        return new BenchmarkMeasurement(median, allocatedBytesPerOp, warmups.Count, measured.Count);
    }

    private static double TimeOne(MethodInfo execute, object instance)
    {
        var watch = Stopwatch.StartNew();
        execute.Invoke(instance, null);
        watch.Stop();
        return watch.Elapsed.TotalMilliseconds;
    }

    private static bool Agree(double previous, double current)
    {
        var slower = Math.Max(previous, current);
        return slower <= 0 || Math.Abs(previous - current) <= BenchmarkPolicy.WarmupTolerance * slower;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
