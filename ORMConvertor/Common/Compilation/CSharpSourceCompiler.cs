using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Common.Compilation;

/// <summary>
/// The single Roslyn compilation step of the solution (decision 016). Two consumers ask the
/// same question - "does this generated C# compile?" - and want different things done with
/// the answer: the verification of generated artifacts needs the diagnostics and no loading,
/// the benchmarking harness needs the emitted assembly loaded and run. Both build on this
/// method; neither throwing nor loading happens here.
/// </summary>
public static class CSharpSourceCompiler
{
    public static CSharpCompilationResult Compile(
        string assemblyName,
        IEnumerable<string> sources,
        IEnumerable<MetadataReference> references)
    {
        var syntaxTrees = sources.Select(source => CSharpSyntaxTree.ParseText(source)).ToList();

        // Nullable context on, because every project of this solution - and therefore every
        // consumer project the generated code is meant for - compiles with <Nullable>enable</Nullable>.
        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var peStream = new MemoryStream();
        using var pdbStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream, pdbStream);

        return new CSharpCompilationResult
        {
            Success = emitResult.Success,
            Diagnostics = emitResult.Diagnostics,
            Assembly = emitResult.Success ? peStream.ToArray() : null,
            Symbols = emitResult.Success ? pdbStream.ToArray() : null,
        };
    }
}
