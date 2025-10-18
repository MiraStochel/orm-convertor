using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace AdvisorBenchmarking;

internal static class BenchmarkReferenceProvider
{
    public static IReadOnlyList<MetadataReference> GetStandardReferences()
    {
        var assemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddAssembly(Assembly assembly)
        {
            if (!string.IsNullOrEmpty(assembly.Location))
            {
                assemblies.Add(assembly.Location);
            }
        }

        AddAssembly(typeof(object).Assembly);
        AddAssembly(typeof(Attribute).Assembly);
        AddAssembly(typeof(Enumerable).Assembly);
        AddAssembly(typeof(List<>).Assembly);
        AddAssembly(typeof(Console).Assembly);
        AddAssembly(typeof(System.Runtime.GCSettings).Assembly);
        AddAssembly(typeof(System.Diagnostics.Stopwatch).Assembly);
        AddAssembly(typeof(System.Data.Common.DbConnection).Assembly);
        AddAssembly(typeof(System.ComponentModel.DataAnnotations.KeyAttribute).Assembly);
        AddAssembly(typeof(System.ComponentModel.Component).Assembly);
        AddAssembly(typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly);
        AddAssembly(typeof(Dapper.SqlMapper).Assembly);
        AddAssembly(typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly);
        AddAssembly(typeof(Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsExtensions).Assembly);

        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrEmpty(tpa))
        {
            foreach (var path in tpa.Split(Path.PathSeparator))
            {
                var fileName = Path.GetFileName(path);
                if (fileName.Equals("netstandard.dll", StringComparison.OrdinalIgnoreCase)
                    || fileName.Equals("System.Runtime.dll", StringComparison.OrdinalIgnoreCase)
                    || fileName.Equals("System.Console.dll", StringComparison.OrdinalIgnoreCase)
                    || fileName.Equals("System.Linq.dll", StringComparison.OrdinalIgnoreCase)
                    || fileName.Equals("System.Collections.dll", StringComparison.OrdinalIgnoreCase)
                    || fileName.Equals("System.Data.Common.dll", StringComparison.OrdinalIgnoreCase)
                    || fileName.Equals("System.ComponentModel.DataAnnotations.dll", StringComparison.OrdinalIgnoreCase))
                {
                    assemblies.Add(path);
                }
            }
        }

        return assemblies
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList();
    }
}
