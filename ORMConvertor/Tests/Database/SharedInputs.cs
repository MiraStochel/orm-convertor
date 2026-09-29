using Model;

namespace Tests.Database;

/// <summary>
/// What the files under <c>Tests/Database</c> that both suites read have in common
/// (decisions 076 and 089): the differential inputs and the query shapes are input to the
/// tool, written in the languages of the source frameworks, and the language of a unit
/// follows its file name.
/// </summary>
internal static class SharedInputs
{
    /// <summary>
    /// The language a unit is written in, taken from its file name. The table is the Java
    /// suite's <c>ContentType.forFileName</c> and has to stay it: the two suites read the
    /// same files, so a unit that arrived under two different languages would be a finding
    /// about the suites dressed as a finding about the tool.
    /// </summary>
    public static ConversionContentType ContentTypeOf(string path) => path switch
    {
        _ when path.EndsWith(".query.cs", StringComparison.Ordinal) => ConversionContentType.CSharpQuery,
        _ when path.EndsWith(".query.java", StringComparison.Ordinal) => ConversionContentType.JavaQuery,
        _ when path.EndsWith(".cs", StringComparison.Ordinal) => ConversionContentType.CSharpEntity,
        _ when path.EndsWith(".java", StringComparison.Ordinal) => ConversionContentType.JavaEntity,
        _ when path.EndsWith(".xml", StringComparison.Ordinal) => ConversionContentType.XML,
        _ when path.EndsWith(".sql", StringComparison.Ordinal) => ConversionContentType.SqlQuery,
        _ when path.EndsWith(".hql", StringComparison.Ordinal) => ConversionContentType.HqlQuery,
        _ when path.EndsWith(".jpql", StringComparison.Ordinal) => ConversionContentType.JpqlQuery,
        _ => throw new NotSupportedException($"No content type is defined for the extension of \"{path}\"."),
    };

    /// <summary>
    /// The sections of a "key = value" file - the format <c>matrix.txt</c> and
    /// <c>categories.txt</c> share, poorer than JSON on purpose because two parsers in two
    /// languages have to agree about it. Keys keep the order of the file, and a key stated
    /// twice in one section keeps its last value.
    /// </summary>
    public static List<(string Id, List<KeyValuePair<string, string>> Values)> Sections(string fileName, IEnumerable<string> lines)
    {
        var sections = new List<(string, List<KeyValuePair<string, string>>)>();
        string? id = null;
        var values = new List<KeyValuePair<string, string>>();

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (id is not null)
                {
                    sections.Add((id, values));
                }

                id = line[1..^1].Trim();
                values = [];
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                throw new InvalidOperationException($"{fileName}: \"{line}\" is neither a section nor a key.");
            }

            var key = line[..separator].Trim();
            values.RemoveAll(pair => pair.Key == key);
            values.Add(new KeyValuePair<string, string>(key, line[(separator + 1)..].Trim()));
        }

        if (id is not null)
        {
            sections.Add((id, values));
        }

        return sections;
    }

    /// <summary>A comma-separated value as its entries, empty ones dropped.</summary>
    public static List<string> List(string value)
        => [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
