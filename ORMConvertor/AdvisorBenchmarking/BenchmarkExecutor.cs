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

        const int iterations = 5;
        long memoryBefore = 0;
        long memoryAfter = 0;
        var stopwatch = new Stopwatch();

        try
        {
            setup?.Invoke(instance, null);
            logger?.LogDebug("Setup invoked.");

            // Perform a non-measured preview invocation to report result count and first row.
            try
            {
                var preview = execute.Invoke(instance, null);
                LogPreviewResult(logger, preview);
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Preview invocation failed; continuing to benchmark.");
            }

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
    
    private static void LogPreviewResult(ILogger? logger, object? result)
    {
        if (logger is null)
        {
            return;
        }

        if (result is null)
        {
            logger.LogDebug("Query returned 0 rows (null result).");
            return;
        }

        // Try to compute count without re-enumerating expensive sources; harness returns lists.
        int count = result is System.Collections.ICollection coll ? coll.Count : CountEnumerable(result as System.Collections.IEnumerable);
        logger.LogDebug("Query returned {Count} rows.", count);

        if (count <= 0)
        {
            return;
        }

        var first = FirstOrDefault(result as System.Collections.IEnumerable) ?? result;
        var formatted = FormatObject(first);
        logger.LogDebug("First row: {Row}", formatted);
    }

    private static int CountEnumerable(System.Collections.IEnumerable? enumerable)
    {
        if (enumerable is null) { return 0; }
        int i = 0;
        var e = enumerable.GetEnumerator();
        try
        {
            while (e.MoveNext()) { i++; }
        }
        finally
        {
            (e as IDisposable)?.Dispose();
        }
        return i;
    }

    private static object? FirstOrDefault(System.Collections.IEnumerable? enumerable)
    {
        if (enumerable is null) { return null; }
        var e = enumerable.GetEnumerator();
        try
        {
            return e.MoveNext() ? e.Current : null;
        }
        finally
        {
            (e as IDisposable)?.Dispose();
        }
    }

    private static string FormatObject(object? obj)
    {
        if (obj is null)
        {
            return "<null>";
        }

        var type = obj.GetType();
        // For simple primitives, just ToString
        if (type.IsPrimitive || obj is string || obj is decimal || obj is DateTime || obj is Guid)
        {
            return obj is string s ? $"\"{s}\"" : Convert.ToString(obj) ?? string.Empty;
        }

        var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead)
            .Take(20) // avoid flooding
            .Select(p => $"{p.Name}={(FormatScalar(p.GetValue(obj)))}");

        return $"{type.Name} {{ {string.Join(", ", props)} }}";
    }

    private static string FormatScalar(object? value)
    {
        if (value is null) return "<null>";
        return value switch
        {
            string s => $"\"{s}\"",
            DateTime dt => dt.ToString("o"),
            _ => Convert.ToString(value) ?? string.Empty
        };
    }
}
