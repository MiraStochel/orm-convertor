using Common.Compilation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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

                // The provider of the dialect the artifact is written for (decision 086): a
                // date difference is EF.Functions.DateDiff…, which SQL Server's provider
                // defines (decision 113), and a consumer project over SQL Server references
                // it - the Advisor's compilation does too.
                typeof(Microsoft.EntityFrameworkCore.SqlServerDbFunctionsExtensions).Assembly,
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
                // A projection materializes as List<dynamic> (decision 104), and the compiler
                // wants DynamicAttribute for a member typed dynamic; the runtime defines it in
                // System.Linq.Expressions, which every consumer project references implicitly.
                typeof(System.Linq.Expressions.Expression).Assembly,
            ],
            ["netstandard.dll", "System.Runtime.dll", "System.Collections.dll",
             "System.Data.Common.dll", "System.Linq.dll", "System.Linq.Expressions.dll"]);

    public static readonly IReadOnlyList<MetadataReference> NHibernateConsumerReferences =
        MetadataReferenceProvider.Create(
            [
                typeof(object).Assembly,
                typeof(global::NHibernate.ISession).Assembly,
                typeof(Queryable).Assembly,
                // The LINQ form of decision 118 returns IQueryable, which the runtime defines in
                // System.Linq.Expressions; every consumer project references it implicitly.
                typeof(System.Linq.Expressions.Expression).Assembly,
            ],
            ["netstandard.dll", "System.Runtime.dll", "System.Collections.dll", "System.Linq.dll", "System.Linq.Expressions.dll"]);

    /// <summary>
    /// Wraps the query method in the class and usings a consumer project would give it, then
    /// compiles it together with the entity sources. A query that is a whole file already - a
    /// foreign artifact written as a developer writes it (decision 204) - is compiled as it
    /// is, with its own usings and class; the consumer adds nothing but the project's
    /// implicit usings.
    /// </summary>
    public static CSharpCompilationResult Compile(
        string assemblyName,
        string queryMethod,
        IEnumerable<string> entitySources,
        IReadOnlyList<MetadataReference> consumerReferences,
        string extraUsings = "")
        => CSharpSourceCompiler.Compile(
            assemblyName,
            entitySources.Prepend(IsCompilationUnit(queryMethod) ? queryMethod : Holder(queryMethod, extraUsings)).Prepend(ConsumerPreamble),
            consumerReferences);

    /// <summary>
    /// Whether the text is a whole file rather than a bare method (decision 204): read as a
    /// compilation unit it carries a using directive or a namespace, or its top-level members are
    /// types alone. A bare method read that way is a top-level member that is no type - and so is
    /// the tool's artifact that declares the row type of a native SQL query beside the method,
    /// which stays a bare method the consumer wraps.
    /// </summary>
    public static bool IsCompilationUnit(string text)
    {
        var root = CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot();
        return root.Usings.Count > 0
            || root.Members.Any(member => member is BaseNamespaceDeclarationSyntax)
            || (root.Members.Count > 0 && root.Members.All(member => member is BaseTypeDeclarationSyntax));
    }

    /// <summary>Whether the text declares a type anywhere - the class a whole file puts around its method.</summary>
    public static bool DeclaresAType(string text)
        => CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Any();

    /// <summary>
    /// The class a consumer project puts around the bare method, with the usings in front of
    /// it - the text the compiler is given, and therefore the text a reading of the method
    /// by the language alone has to be given too (decision 172, the first level of a
    /// foreign artifact).
    /// </summary>
    public static string Holder(string queryMethod, string extraUsings = "")
        => $$"""
            {{extraUsings}}

            public static class GeneratedQueries
            {
            {{queryMethod}}
            }
            """;

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
