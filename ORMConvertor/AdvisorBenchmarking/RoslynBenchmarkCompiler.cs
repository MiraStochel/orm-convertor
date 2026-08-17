using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Common.Compilation;
using Microsoft.CodeAnalysis;

namespace AdvisorBenchmarking;

/// <summary>
/// The benchmarking consumer of the shared compilation step (decision 016): a benchmark
/// cannot run without an assembly, so here a failed compilation is an error and the emitted
/// assembly is loaded into a collectible context right away. The verification of generated
/// artifacts uses the same step the other way round - diagnostics, no loading.
/// </summary>
internal sealed class RoslynBenchmarkCompiler
{
    public CompiledAssembly Compile(string source, IEnumerable<MetadataReference> references, string assemblyName)
    {
        var result = CSharpSourceCompiler.Compile(assemblyName, [source], references);

        if (!result.Success)
        {
            throw new InvalidOperationException("Compilation failed. " + string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
        }

        using var peStream = new MemoryStream(result.Assembly!);
        using var pdbStream = new MemoryStream(result.Symbols!);

        var context = new AssemblyLoadContext(assemblyName, isCollectible: true);
        var assembly = context.LoadFromStream(peStream, pdbStream);
        return new CompiledAssembly(assembly, context);
    }

    public sealed class CompiledAssembly : IDisposable
    {
        public CompiledAssembly(Assembly assembly, AssemblyLoadContext loadContext)
        {
            Assembly = assembly;
            loadContextRef = loadContext;
        }

        public Assembly Assembly { get; }

        private readonly AssemblyLoadContext loadContextRef;

        public void Dispose() => loadContextRef.Unload();
    }
}
