using System.Text.Json;
using Model;
using SampleData;
using Tests.Database;
using Tests.Differential;
using Tests.FibenJudge;

namespace Tests.Foreign;

/// <summary>
/// The inputs the suites send, written as files (decision 172): one directory per key and
/// source, the units of the conversion with their extensions and an <c>input.json</c> that
/// names the key, the source and the units - and, for a FIBEN query, the questions it answers
/// (decision 149). What a model or a person is given is then the same set of inputs the suite
/// translates, as T4 asks, and an artifact made from it answers the key the directory names.
///
/// The schema stands in the files as the placeholder <c>{{schema}}</c>, as it does in the
/// inputs of the repository: an artifact that keeps the placeholder runs in either suite's
/// schema, because the reader substitutes it the way the inputs are substituted.
/// </summary>
internal static class ForeignInputs
{
    public const string Variable = "ORMCONVERTOR_FOREIGN_INPUTS";

    public const string ManifestFileName = "input.json";

    private const string SchemaPlaceholder = "{{schema}}";

    /// <summary>Writes every input of the three kinds under the root and returns how many directories it wrote.</summary>
    public static int Export(string root)
    {
        var count = 0;

        foreach (var query in DifferentialMatrix.Queries)
        {
            foreach (var source in query.Sources)
            {
                Write(root, new ForeignKey(ForeignKey.Matrix, query.Id), source, query.Units(source), questions: null);
                count++;
            }
        }

        foreach (var query in FibenSample.Queries)
        {
            Write(root, new ForeignKey(ForeignKey.Fiben, query.Key), ORMEnum.Dapper, FibenRun.Units(query), query.Questions);
            count++;
        }

        foreach (var query in LdbcSnbSample.Queries.Where(query => query.Sql is not null))
        {
            List<ConversionSource> units =
            [
                .. LdbcSnbSample.Entities.Select(entity => new ConversionSource { Name = entity.FileName, ContentType = ConversionContentType.CSharp, Content = entity.Content }),
                new ConversionSource { Name = $"{query.Key}.sql", ContentType = ConversionContentType.SqlQuery, Content = query.Sql! },
            ];

            Write(root, new ForeignKey(ForeignKey.Ldbc, query.Key), ORMEnum.Dapper, units, questions: null);
            count++;
        }

        return count;
    }

    /// <summary>The directory of one input under the root: the kind, the key and the source.</summary>
    public static string DirectoryOf(string root, ForeignKey key, ORMEnum source)
        => Path.Combine(root, $"{key.Kind}-{key.Id}-{source}");

    /// <summary>
    /// Reads an exported input back as the units of a conversion, with the placeholder
    /// substituted - what the test of the path feeds the tool to prove that an artifact of the
    /// tool put through the path is measured as the client measures it.
    /// </summary>
    public static (ForeignKey Key, ORMEnum Source, List<ConversionSource> Units) Read(string directory, string schema)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, ManifestFileName)));
        var root = manifest.RootElement;

        var key = ForeignKey.Parse(root.GetProperty("answers").GetString()!);
        var source = Enum.Parse<ORMEnum>(root.GetProperty("source").GetString()!);

        var units = root.GetProperty("units").EnumerateArray()
            .Select(unit =>
            {
                var path = unit.GetProperty("path").GetString()!;
                return new ConversionSource
                {
                    Name = path,
                    ContentType = Enum.Parse<ConversionContentType>(unit.GetProperty("contentType").GetString()!),
                    Content = File.ReadAllText(Path.Combine(directory, path)).Replace(SchemaPlaceholder, schema, StringComparison.Ordinal),
                };
            })
            .ToList();

        return (key, source, units);
    }

    /// <summary>Writes the input of one key and source; what the export does for every key.</summary>
    internal static void WriteOne(string root, ForeignKey key, ORMEnum source, IReadOnlyList<ConversionSource> units, IReadOnlyList<string>? questions)
        => Write(root, key, source, units, questions);

    private static void Write(string root, ForeignKey key, ORMEnum source, IReadOnlyList<ConversionSource> units, IReadOnlyList<string>? questions)
    {
        var directory = DirectoryOf(root, key, source);
        Directory.CreateDirectory(directory);

        var manifest = new List<string>
        {
            "{",
            $"  \"answers\": {JsonSerializer.Serialize(key.ToString())},",
            $"  \"source\": \"{source}\",",
        };

        if (questions is { Count: > 0 })
        {
            manifest.Add($"  \"questions\": {JsonSerializer.Serialize(questions)},");
        }

        manifest.Add("  \"units\": [");

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < units.Count; index++)
        {
            var unit = units[index];
            var path = Unique(unit.Name ?? $"unit-{index + 1}{Extension(unit.ContentType)}", taken);

            // The schema the suite substituted goes back to the placeholder, so that the file
            // names no schema of this machine; the LDBC schema is named after the fixture's and
            // keeps its suffix.
            var content = unit.Content.Replace(TestDatabase.SchemaName, SchemaPlaceholder, StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(directory, path), content);

            manifest.Add($"    {{ \"path\": {JsonSerializer.Serialize(path)}, \"contentType\": \"{unit.ContentType}\" }}{(index < units.Count - 1 ? "," : string.Empty)}");
        }

        manifest.Add("  ]");
        manifest.Add("}");

        File.WriteAllText(Path.Combine(directory, ManifestFileName), string.Join("\n", manifest) + "\n");
    }

    private static string Unique(string name, HashSet<string> taken)
    {
        if (taken.Add(name))
        {
            return name;
        }

        var extension = Path.GetExtension(name);
        var stem = name[..^extension.Length];
        for (var number = 2; ; number++)
        {
            var candidate = $"{stem}-{number}{extension}";
            if (taken.Add(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>The extension a unit of the content type is written with - the table of the frontend and of the shared inputs, read the other way.</summary>
    internal static string Extension(ConversionContentType contentType) => contentType switch
    {
        ConversionContentType.CSharp or ConversionContentType.CSharpEntity or ConversionContentType.CSharpQuery or ConversionContentType.CSharpLinqQuery => ".cs",
        ConversionContentType.Java or ConversionContentType.JavaEntity or ConversionContentType.JavaQuery => ".java",
        ConversionContentType.XML => ".xml",
        ConversionContentType.SqlQuery => ".sql",
        ConversionContentType.HqlQuery => ".hql",
        ConversionContentType.JpqlQuery => ".jpql",
        _ => ".txt",
    };
}
