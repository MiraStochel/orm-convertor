using System.Text;

namespace SampleData;

/// <summary>
/// Whole source files composed out of the samples' parts (decision 111): the entity a sample
/// declares and, in the same file, the class that queries it - which is what a user has in
/// hand and what one unit of the tool now is. Composed rather than written out again, so the
/// file cannot drift from the entity and the query the tests and the other pages use.
/// </summary>
internal static class SampleFiles
{
    /// <summary>A C# file: the entity file, then one class holding the members.</summary>
    public static string CSharp(string entityFile, string classHeader, params string[] members)
        => entityFile.TrimEnd() + "\n\n" + classHeader + "\n{\n" + Members(members) + "\n}\n";

    /// <summary>
    /// A Java file: the first file whole, then the type of the second one - its imports merged
    /// into the first's, its package dropped as the first's already stands, and its public
    /// taken away, because a Java file declares one public top-level type.
    /// </summary>
    public static string Java(string first, string second)
    {
        var (package, imports, body) = Split(first);
        var (_, moreImports, secondBody) = Split(second);

        var file = new StringBuilder();
        if (package is not null)
        {
            file.Append(package).Append("\n\n");
        }

        var merged = imports.Concat(moreImports.Where(import => !imports.Contains(import))).ToList();
        if (merged.Count > 0)
        {
            file.Append(string.Join("\n", merged)).Append("\n\n");
        }

        file.Append(body.Trim()).Append("\n\n");
        file.Append(Demoted(secondBody.Trim())).Append('\n');
        return file.ToString();
    }

    /// <summary>A Java type holding the members, with the imports its members need above it.</summary>
    public static string JavaType(IEnumerable<string> imports, string classHeader, params string[] members)
        => string.Concat(imports.Select(import => $"import {import};\n")) + "\n" + classHeader + " {\n\n" + Members(members) + "\n}\n";

    /// <summary>The text with every non-empty line moved right by the given number of columns.</summary>
    public static string Indent(string text, int columns)
        => string.Join("\n", text.TrimEnd().Split('\n').Select(line => line.TrimEnd('\r').Length == 0
            ? string.Empty
            : new string(' ', columns) + line.TrimEnd('\r')));

    private static string Members(IEnumerable<string> members)
        => string.Join("\n\n", members.Select(member => Indent(member, 4)));

    private static (string? Package, List<string> Imports, string Body) Split(string file)
    {
        string? package = null;
        var imports = new List<string>();
        var body = new StringBuilder();

        foreach (var raw in file.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("package ", StringComparison.Ordinal))
            {
                package = trimmed;
            }
            else if (trimmed.StartsWith("import ", StringComparison.Ordinal))
            {
                imports.Add(trimmed);
            }
            else
            {
                body.Append(line).Append('\n');
            }
        }

        return (package, imports, body.ToString());
    }

    private static string Demoted(string body)
    {
        var lines = body.Split('\n').ToList();
        var declaration = lines.FindIndex(line => line.StartsWith("public ", StringComparison.Ordinal));
        if (declaration >= 0)
        {
            lines[declaration] = lines[declaration]["public ".Length..];
        }

        return string.Join("\n", lines);
    }
}
