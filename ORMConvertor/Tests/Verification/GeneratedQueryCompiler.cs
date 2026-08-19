using Common.Compilation;
using Microsoft.CodeAnalysis;

namespace Tests.Verification;

/// <summary>
/// Second verification level for the query branch (decision 027): the generated query has to
/// compile alongside the entities it queries. Same idea as
/// <see cref="GeneratedEntityCompiler"/>, but a query draws on more of the consumer project
/// than an entity does - LINQ for EF Core, the data interfaces and Dapper for a Dapper
/// query - so it needs its own reference sets.
/// </summary>
internal static class GeneratedQueryCompiler
{
    /// <summary>
    /// A query method is written as a member, so the harness supplies the class and the
    /// usings around it. That is the consumer project's contribution, not the artifact's -
    /// the same division the entity compiler makes.
    /// </summary>
    private const string ConsumerPreamble = """
        global using System;
        global using System.Collections.Generic;
        global using System.Linq;
        """;

    public static readonly IReadOnlyList<MetadataReference> EFCoreConsumerReferences =
        MetadataReferenceProvider.Create(
            [
                typeof(object).Assembly,
                typeof(System.ComponentModel.DataAnnotations.KeyAttribute).Assembly,
                typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly,
                typeof(Microsoft.EntityFrameworkCore.PrimaryKeyAttribute).Assembly,
                typeof(Microsoft.EntityFrameworkCore.RelationalQueryableExtensions).Assembly,
                typeof(Queryable).Assembly,
                typeof(System.Linq.Expressions.Expression).Assembly,
            ],
            ["netstandard.dll", "System.Runtime.dll", "System.Collections.dll",
             "System.ComponentModel.DataAnnotations.dll", "System.ComponentModel.TypeConverter.dll",
             "System.Linq.dll", "System.Linq.Expressions.dll"]);

    public static readonly IReadOnlyList<MetadataReference> DapperConsumerReferences =
        MetadataReferenceProvider.Create(
            [
                typeof(object).Assembly,
                typeof(System.Data.IDbConnection).Assembly,
                typeof(global::Dapper.SqlMapper).Assembly,
                typeof(Queryable).Assembly,
            ],
            ["netstandard.dll", "System.Runtime.dll", "System.Collections.dll",
             "System.Data.Common.dll", "System.Linq.dll"]);

    public static readonly IReadOnlyList<MetadataReference> NHibernateConsumerReferences =
        MetadataReferenceProvider.Create(
            [
                typeof(object).Assembly,
                typeof(global::NHibernate.ISession).Assembly,
                typeof(Queryable).Assembly,
            ],
            ["netstandard.dll", "System.Runtime.dll", "System.Collections.dll", "System.Linq.dll"]);

    /// <summary>
    /// Wraps the query method in the class and usings a consumer project would give it, then
    /// compiles it together with the entity sources.
    /// </summary>
    public static CSharpCompilationResult Compile(
        string assemblyName,
        string queryMethod,
        IEnumerable<string> entitySources,
        IReadOnlyList<MetadataReference> consumerReferences,
        string extraUsings = "")
    {
        var holder = $$"""
            {{extraUsings}}

            public static class GeneratedQueries
            {
            {{queryMethod}}
            }
            """;

        return CSharpSourceCompiler.Compile(
            assemblyName,
            entitySources.Prepend(holder).Prepend(ConsumerPreamble),
            consumerReferences);
    }

    public static byte[] CompileOrFail(
        string assemblyName,
        string queryMethod,
        IEnumerable<string> entitySources,
        IReadOnlyList<MetadataReference> consumerReferences,
        string extraUsings = "")
    {
        var result = Compile(assemblyName, queryMethod, entitySources, consumerReferences, extraUsings);

        Assert.True(result.Success, "Generated query does not compile:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, result.Errors.Select(e => e.ToString())));

        return result.Assembly!;
    }
}
