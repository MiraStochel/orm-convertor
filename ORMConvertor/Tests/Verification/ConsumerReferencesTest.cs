using System.Text.Json;
using Common.Compilation;
using Microsoft.CodeAnalysis;
using Tests.Combined;

namespace Tests.Verification;

/// <summary>
/// The reference set of a consumer project (decision 207) is what the prompt of the comparison
/// promises the model: the shared framework and the pinned package with its closure. Held against
/// the two sources of truth - the runtime the suite runs on and <c>packages.lock.json</c>
/// (decision 186) - and measured on the code the run of 2026-11-06 saw refused.
/// </summary>
public class ConsumerReferencesTest
{
    private static IEnumerable<string> FileNames(IReadOnlyList<MetadataReference> references)
        => references.OfType<PortableExecutableReference>().Select(reference => Path.GetFileName(reference.FilePath!));

    [Fact]
    public void EverySetCarriesTheSharedFramework()
    {
        foreach (var references in new[] { ConsumerReferences.Dapper, ConsumerReferences.EFCore, ConsumerReferences.NHibernate })
        {
            var names = FileNames(references).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("System.Text.Json.dll", names);
            Assert.Contains("System.ComponentModel.Annotations.dll", names);
            Assert.Contains("System.Data.Common.dll", names);
            Assert.Contains("System.Linq.Expressions.dll", names);
            Assert.All(ConsumerReferences.SharedFramework, path => Assert.Contains(Path.GetFileName(path), names));
        }

        Assert.Contains("Microsoft.Data.SqlClient.dll", FileNames(ConsumerReferences.EFCore), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Microsoft.EntityFrameworkCore.SqlServer.dll", FileNames(ConsumerReferences.EFCore), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Dapper.dll", FileNames(ConsumerReferences.Dapper), StringComparer.OrdinalIgnoreCase);
        Assert.Contains("NHibernate.dll", FileNames(ConsumerReferences.NHibernate), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every package of the target's closure in the lock file that the restore put beside the suite is a reference of the target's set.</summary>
    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", "EFCore")]
    [InlineData("Dapper", "Dapper")]
    [InlineData("NHibernate", "NHibernate")]
    public void TheSetCarriesTheClosureOfTheLockFile(string package, string target)
    {
        var references = target switch
        {
            "EFCore" => ConsumerReferences.EFCore,
            "Dapper" => ConsumerReferences.Dapper,
            _ => ConsumerReferences.NHibernate,
        };
        var names = FileNames(references).ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var lockFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(TargetFrameworkDescriptorTest.SolutionDirectory(), "Tests", "packages.lock.json")));
        var packages = lockFile.RootElement.GetProperty("dependencies").EnumerateObject().First().Value;

        var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>([package]);
        while (queue.TryDequeue(out var name))
        {
            if (!closure.Add(name) || !packages.TryGetProperty(name, out var entry) || !entry.TryGetProperty("dependencies", out var dependencies))
            {
                continue;
            }

            foreach (var dependency in dependencies.EnumerateObject())
            {
                queue.Enqueue(dependency.Name);
            }
        }

        Assert.Contains(package, closure);
        var present = closure.Where(name => File.Exists(Path.Combine(AppContext.BaseDirectory, name + ".dll"))).ToList();
        Assert.NotEmpty(present);
        Assert.All(present, name => Assert.Contains(name + ".dll", names));

        if (target == "EFCore")
        {
            // The fact behind decision 207: the provider pulls Microsoft.Data.SqlClient.
            Assert.Contains("Microsoft.Data.SqlClient", closure);
        }
    }

    /// <summary>What the run of 2026-11-06 refused as not compiled compiles in the promised project: native SQL with SqlParameter under EF Core, data annotations on a Dapper entity.</summary>
    [Fact]
    public void ThePromisedProjectCompilesWhatTheRunRefused()
    {
        var efCore = CSharpSourceCompiler.Compile("ConsumerReferencesProbeEFCore",
            [
                """
                using Microsoft.Data.SqlClient;
                using System.Data;
                using System.Text.Json;

                namespace Shop;

                public static class Probe
                {
                    public static object Parameter() => new SqlParameter("@since", SqlDbType.DateTime2) { Value = System.DateTime.UtcNow };
                    public static string Json() => JsonSerializer.Serialize(new { a = 1 });
                }
                """,
            ],
            ConsumerReferences.EFCore);
        Assert.True(efCore.Success, string.Join("; ", efCore.Errors.Select(error => error.ToString())));

        var dapper = CSharpSourceCompiler.Compile("ConsumerReferencesProbeDapper",
            [
                """
                using System.ComponentModel.DataAnnotations;
                using System.ComponentModel.DataAnnotations.Schema;

                namespace Shop;

                [Table("Products", Schema = "dbo")]
                public class Product
                {
                    [Key]
                    [Column("ProductId")]
                    public int Id { get; set; }
                }
                """,
            ],
            ConsumerReferences.Dapper);
        Assert.True(dapper.Success, string.Join("; ", dapper.Errors.Select(error => error.ToString())));
    }
}
