using System.Text.Json;
using Microsoft.CodeAnalysis;
using Model;

namespace Tests.Verification;

/// <summary>
/// The references a consumer project of a .NET target compiles with (decision 207): the shared
/// framework of the pinned target framework - every managed assembly of Microsoft.NETCore.App
/// the process runs on - and the pinned package of the target with its transitive closure, as
/// packages.lock.json resolves it and the restore puts it beside the suite. The prompt of the
/// comparison promises the model such a project - "a project that references Entity Framework
/// Core with its SQL Server provider" -, so the judge compiles with what that project has:
/// Microsoft.Data.SqlClient under the provider, System.Text.Json and the data annotations of
/// the shared framework. One set per target, for the artifact of the tool and a foreign one
/// alike; what compiled with fewer references compiles with these.
/// </summary>
internal static class ConsumerReferences
{
    // Declared before the sets: static fields initialize in textual order.
    private static readonly string RuntimeDirectory = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(typeof(object).Assembly.Location)!);

    /// <summary>The managed assemblies of the shared framework the process runs on, by path - the trusted platform assemblies under the runtime directory.</summary>
    public static readonly IReadOnlyList<string> SharedFramework = TrustedPlatformAssemblies()
        .Where(path => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(path)!), RuntimeDirectory, StringComparison.OrdinalIgnoreCase))
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public static readonly IReadOnlyList<MetadataReference> Dapper = Build("Dapper");

    public static readonly IReadOnlyList<MetadataReference> EFCore = Build("Microsoft.EntityFrameworkCore.SqlServer");

    public static readonly IReadOnlyList<MetadataReference> NHibernate = Build("NHibernate");

    public static IReadOnlyList<MetadataReference> For(ORMEnum target) => target switch
    {
        ORMEnum.Dapper => Dapper,
        ORMEnum.EFCore => EFCore,
        ORMEnum.NHibernate => NHibernate,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "a Java target compiles in the Java suite"),
    };

    /// <summary>
    /// The runtime assemblies of the package's transitive closure, as the restore resolved it:
    /// read from the suite's <c>.deps.json</c>, the dependency graph the build writes beside the
    /// assembly from the same restore the lock file pins (decision 186), and taken from beside
    /// the suite, where the build copies them. A package without a runtime assembly here (a
    /// native or platform-specific one) contributes nothing a compiler could reference.
    /// </summary>
    public static IReadOnlyList<string> Closure(string package)
    {
        var deps = Path.Combine(AppContext.BaseDirectory, typeof(ConsumerReferences).Assembly.GetName().Name + ".deps.json");
        using var document = JsonDocument.Parse(File.ReadAllText(deps));
        var target = document.RootElement.GetProperty("targets").EnumerateObject().First(entry => entry.Value.EnumerateObject().Any()).Value;

        var byName = target.EnumerateObject()
            .ToDictionary(entry => entry.Name[..entry.Name.IndexOf('/')], entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>([package]);

        while (queue.TryDequeue(out var name))
        {
            if (!seen.Add(name) || !byName.TryGetValue(name, out var entry))
            {
                continue;
            }

            if (entry.TryGetProperty("runtime", out var runtime))
            {
                foreach (var file in runtime.EnumerateObject())
                {
                    var path = Path.Combine(AppContext.BaseDirectory, Path.GetFileName(file.Name.Replace('/', Path.DirectorySeparatorChar)));
                    if (File.Exists(path))
                    {
                        paths.Add(path);
                    }
                }
            }

            if (entry.TryGetProperty("dependencies", out var dependencies))
            {
                foreach (var dependency in dependencies.EnumerateObject())
                {
                    queue.Enqueue(dependency.Name);
                }
            }
        }

        if (!seen.Contains(package) || !byName.ContainsKey(package))
        {
            throw new InvalidOperationException($"{deps} names no package {package}; the consumer references of decision 207 start from the pinned package of the target.");
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<MetadataReference> Build(string package)
        => SharedFramework
            .Concat(Closure(package))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList();

    private static IEnumerable<string> TrustedPlatformAssemblies()
        => AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa
            ? tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            : [];
}
