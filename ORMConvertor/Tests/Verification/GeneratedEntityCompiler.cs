using Common.Compilation;
using Microsoft.CodeAnalysis;

namespace Tests.Verification;

/// <summary>
/// Second verification level of decision 016 for C# artifacts: the generated entity has to
/// compile. The compilation environment mirrors the consumer project the artifact is meant
/// for - an SDK project with ImplicitUsings enabled that references the target framework's
/// package - so the implicit usings and the references supplied here are the consumer's
/// contribution, not the artifact's.
/// </summary>
internal static class GeneratedEntityCompiler
{
    /// <summary>
    /// The part of the SDK's implicit usings the generated entities actually draw on
    /// (DateTime, List&lt;T&gt;). The full SDK set would only drag in references no
    /// generated artifact needs.
    /// </summary>
    private const string ConsumerImplicitUsings = """
        global using System;
        global using System.Collections.Generic;
        """;

    /// <summary>
    /// What a consumer project of the target sees (decision 207, <see cref="ConsumerReferences"/>):
    /// the shared framework and the pinned package with its closure - one set per target, the
    /// same an entity and a query compile with.
    /// </summary>
    public static readonly IReadOnlyList<MetadataReference> NHibernateConsumerReferences = ConsumerReferences.NHibernate;

    public static readonly IReadOnlyList<MetadataReference> EFCoreConsumerReferences = ConsumerReferences.EFCore;

    public static CSharpCompilationResult Compile(
        string assemblyName,
        IEnumerable<string> generatedSources,
        IReadOnlyList<MetadataReference> consumerReferences)
        => CSharpSourceCompiler.Compile(
            assemblyName,
            generatedSources.Prepend(ConsumerImplicitUsings),
            consumerReferences);

    /// <summary>Compiles and turns a failure into a test failure listing the compiler errors.</summary>
    public static byte[] CompileOrFail(
        string assemblyName,
        IEnumerable<string> generatedSources,
        IReadOnlyList<MetadataReference> consumerReferences)
    {
        var result = Compile(assemblyName, generatedSources, consumerReferences);

        Assert.True(result.Success, "Generated code does not compile:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, result.Errors.Select(e => e.ToString())));

        return result.Assembly!;
    }
}
