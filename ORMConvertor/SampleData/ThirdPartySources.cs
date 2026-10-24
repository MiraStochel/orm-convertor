using System.Reflection;

namespace SampleData;

/// <summary>
/// Excerpts of open-source projects vendored under <c>ThirdParty/</c> (decision 138): one folder
/// per project at one commit, with the project's licence, a README listing what was taken and
/// what was changed, and the files themselves, each opening with a note that names its origin.
/// The project file embeds all of it in this assembly - the licence travels with the code - and
/// keeps it out of compilation, so the C# among it is text like the Java.
/// </summary>
public static class ThirdPartySources
{
    private const string Root = "ThirdParty/";

    private static readonly Assembly Assembly = typeof(ThirdPartySources).Assembly;

    // The logical name carries MSBuild's RecursiveDir, which uses the separator of the machine
    // the assembly was built on; the paths here always use '/'.
    private static readonly IReadOnlyDictionary<string, string> Resources = Assembly
        .GetManifestResourceNames()
        .Where(name => name.StartsWith(Root, StringComparison.Ordinal))
        .ToDictionary(name => name.Replace('\\', '/'), name => name, StringComparer.Ordinal);

    /// <summary>
    /// The schema of a project's tables, <c>&lt;project&gt;/schema.sql</c> (decision 176): the
    /// tables the excerpt maps, taken from the schema the project publishes, with <c>{{schema}}</c>
    /// for the schema the caller creates. Both test suites create it, empty, as the catalog of
    /// the excerpt; nothing else reads it.
    /// </summary>
    public const string SchemaFileName = "schema.sql";

    /// <summary>Whether the project carries a schema of its tables.</summary>
    public static bool HasSchema(string project) => Resources.ContainsKey(Root + project + "/" + SchemaFileName);

    /// <summary>The projects whose tables the suites create (decision 176), in ordinal order.</summary>
    public static IReadOnlyList<string> ProjectsWithSchema => [.. Projects.Where(HasSchema)];

    /// <summary>The text of a project's schema script.</summary>
    public static string Schema(string project) => Read(project + "/" + SchemaFileName);

    /// <summary>The projects, by the name of their folder; the README beside them is none.</summary>
    public static IReadOnlyList<string> Projects =>
        [.. Resources.Keys
            .Select(path => path[Root.Length..])
            .Where(path => path.Contains('/'))
            .Select(path => path.Split('/')[0])
            .Distinct()
            .Order(StringComparer.Ordinal)];

    /// <summary>
    /// The paths of a project's files below <c>ThirdParty/</c>, in ordinal order, optionally only
    /// those in one of its folders - <c>keycloak/entities/UserEntity.java</c>.
    /// </summary>
    public static IReadOnlyList<string> Files(string project, string folder = "")
    {
        var prefix = Root + project + "/" + (folder.Length == 0 ? string.Empty : folder.TrimEnd('/') + "/");
        return [.. Resources.Keys
            .Where(path => path.StartsWith(prefix, StringComparison.Ordinal) && path.IndexOf('/', prefix.Length) < 0)
            .Select(path => path[Root.Length..])
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>The paths of all of a project's files below <c>ThirdParty/</c>, in every folder.</summary>
    public static IReadOnlyList<string> Tree(string project) =>
        [.. Resources.Keys
            .Where(path => path.StartsWith(Root + project + "/", StringComparison.Ordinal))
            .Select(path => path[Root.Length..])
            .Order(StringComparer.Ordinal)];

    /// <summary>The text of a file, by its path below <c>ThirdParty/</c>.</summary>
    public static string Read(string path)
    {
        if (!Resources.TryGetValue(Root + path, out var name))
        {
            throw new ArgumentException($"No third-party file '{path}' is embedded.", nameof(path));
        }

        using var stream = Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
