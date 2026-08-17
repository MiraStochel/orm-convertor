using System.Collections.Generic;
using Common.Compilation;
using Microsoft.CodeAnalysis;

namespace AdvisorBenchmarking;

/// <summary>
/// The reference set of a benchmark harness: the frameworks whose generated code gets
/// compiled and run, plus the runtime facades that code draws on. The resolution mechanics
/// live in <see cref="MetadataReferenceProvider"/>, shared with the verification of
/// generated artifacts, which mirrors a different consumer project.
/// </summary>
internal static class BenchmarkReferenceProvider
{
    public static IReadOnlyList<MetadataReference> GetStandardReferences()
        => MetadataReferenceProvider.Create(
            [
                typeof(object).Assembly,
                typeof(System.Attribute).Assembly,
                typeof(System.Linq.Enumerable).Assembly,
                typeof(List<>).Assembly,
                typeof(System.Console).Assembly,
                typeof(System.Runtime.GCSettings).Assembly,
                typeof(System.Diagnostics.Stopwatch).Assembly,
                typeof(System.Data.Common.DbConnection).Assembly,
                typeof(System.Linq.IQueryable).Assembly,
                typeof(System.Linq.Expressions.Expression).Assembly,
                typeof(System.ComponentModel.DataAnnotations.KeyAttribute).Assembly,
                typeof(System.ComponentModel.Component).Assembly,
                typeof(System.ComponentModel.TypeConverter).Assembly,
                typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly,
                typeof(Dapper.SqlMapper).Assembly,
                typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly,
                typeof(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<>).Assembly,
                typeof(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions).Assembly,
                typeof(Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsExtensions).Assembly,
            ],
            [
                "netstandard.dll",
                "System.Runtime.dll",
                "System.Console.dll",
                "System.Linq.dll",
                "System.Collections.dll",
                "System.Data.Common.dll",
                "System.ComponentModel.DataAnnotations.dll",
            ]);
}
