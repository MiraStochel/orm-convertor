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
    /// What any consumer project sees of the runtime. An NHibernate entity is a plain
    /// class, so this is all it gets.
    /// </summary>
    public static readonly IReadOnlyList<MetadataReference> NHibernateConsumerReferences =
        MetadataReferenceProvider.Create(
            [typeof(object).Assembly],
            ["netstandard.dll", "System.Runtime.dll", "System.Collections.dll"]);

    /// <summary>
    /// An EF Core entity additionally carries data annotations and the EF Core attributes
    /// ([PrimaryKey], [Keyless], [Precision] live in the Abstractions assembly).
    /// </summary>
    public static readonly IReadOnlyList<MetadataReference> EFCoreConsumerReferences =
        MetadataReferenceProvider.Create(
            [
                typeof(object).Assembly,
                typeof(System.ComponentModel.DataAnnotations.KeyAttribute).Assembly,
                typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly,
                typeof(Microsoft.EntityFrameworkCore.PrimaryKeyAttribute).Assembly,
            ],
            ["netstandard.dll", "System.Runtime.dll", "System.Collections.dll", "System.ComponentModel.DataAnnotations.dll"]);

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
