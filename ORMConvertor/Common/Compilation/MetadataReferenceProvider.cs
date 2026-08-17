using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Common.Compilation;

/// <summary>
/// Turns a wanted reference set into Roslyn metadata references. Which assemblies a
/// compilation needs is the caller's claim - the benchmarking harness and the verification
/// of generated artifacts each mirror a different consumer project - but the mechanics of
/// resolving them are the same: anchor assemblies named by a type they contain, plus
/// facade assemblies picked out of the runtime's trusted platform assembly list.
/// </summary>
public static class MetadataReferenceProvider
{
    public static IReadOnlyList<MetadataReference> Create(
        IEnumerable<Assembly> assemblies,
        IEnumerable<string> trustedPlatformAssemblyFileNames)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in assemblies)
        {
            if (!string.IsNullOrEmpty(assembly.Location))
            {
                paths.Add(assembly.Location);
            }
        }

        var wanted = new HashSet<string>(trustedPlatformAssemblyFileNames, StringComparer.OrdinalIgnoreCase);

        if (wanted.Count > 0 && AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
        {
            foreach (var path in tpa.Split(Path.PathSeparator))
            {
                if (wanted.Contains(Path.GetFileName(path)))
                {
                    paths.Add(path);
                }
            }
        }

        return paths
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList();
    }
}
