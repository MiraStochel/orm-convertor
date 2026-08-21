using System.Reflection;

namespace OrmConvertor;

/// <summary>
/// The version of the tool itself, read from the assembly rather than written down a second
/// time. The number is set once for the whole solution in <c>Directory.Build.props</c>
/// (decision 034), so this class cannot claim a version the build did not produce - the same
/// arrangement the framework versions have in their descriptors (decision 013).
/// </summary>
public static class ToolRelease
{
    /// <summary>
    /// Version of the running tool, as the run record reports it (S6). Read once: the
    /// assembly cannot change under a running process.
    /// </summary>
    public static string Version { get; } = Read();

    private static string Read()
    {
        var assembly = typeof(ToolRelease).Assembly;

        // The informational version is the one MSBuild fills from <Version>. SourceLink
        // appends "+<commit>" to it where it is set up; the build metadata is not part of
        // the version we claim, so it is cut off.
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrEmpty(informational))
        {
            return assembly.GetName().Version?.ToString() ?? "0.0.0";
        }

        var metadata = informational.IndexOf('+');
        return metadata < 0 ? informational : informational[..metadata];
    }
}
