using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AdvisorBenchmarking;

internal sealed class RoslynBenchmarkCompiler
{
    public CompiledAssembly Compile(string source, IEnumerable<MetadataReference> references, string assemblyName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var peStream = new MemoryStream();
        using var pdbStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream, pdbStream);

        if (!emitResult.Success)
        {
            throw new InvalidOperationException("Compilation failed. " + string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));
        }

        peStream.Seek(0, SeekOrigin.Begin);
        pdbStream.Seek(0, SeekOrigin.Begin);

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
