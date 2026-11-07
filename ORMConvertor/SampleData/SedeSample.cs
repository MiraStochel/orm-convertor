using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SampleData;

/// <summary>
/// The SEDE query set as input to the tool (decision 208): Dapper entity classes over the
/// tables of <c>database/sede/schema.sql</c>, the schema of the Stack Exchange Data Explorer,
/// and the human-reviewed queries of the validation and the test part of the set, each with
/// the title and the description its author wrote, in T-SQL as the author wrote it with the
/// placeholders of SEDE turned into parameters. Both are generated from the set's files by
/// <c>SedeImport</c> and embedded under <c>Sede/</c> of this assembly; the queries as one JSON
/// document, the entities one file per table, so that a conversion is given only the entities
/// of the tables its query reads.
///
/// Dapper is the source for the reason it is for LDBC and FIBEN: the queries are SQL, and a
/// Dapper source states no key, relation or type of its own - every such fact comes from the
/// catalog, and with the Sede database as the catalog it comes from the very tables the dump
/// was loaded into.
///
/// SEDE (M. Hazoom, V. Malik, B. Bogin, NLP4Prog 2021) is published under Apache-2.0; the data
/// are the dump of dba.stackexchange.com under CC BY-SA 4.0. This is not a run of SEDE and not
/// text-to-SQL: the tool does not query in natural language; the title stands beside each
/// query as its intent, for the reader and for the comparison with language models (T4, T5).
/// </summary>
public static class SedeSample
{
    /// <summary>
    /// The data set the texts are run over, named as the loader records it in the extended
    /// property <c>sede.dataset</c> of the Sede database: the site and the date of the dump the
    /// image downloads.
    /// </summary>
    public const string DataSet = "dba.stackexchange.com-2024-04-06";

    private const string Root = "Sede/";
    private const string EntitiesFolder = Root + "Entities/";
    private const string QueriesResource = Root + "queries.json";

    private static readonly Assembly Assembly = typeof(SedeSample).Assembly;

    // The logical name carries MSBuild's RecursiveDir, which uses the separator of the machine
    // the assembly was built on; the paths here always use '/'.
    private static readonly IReadOnlyDictionary<string, string> Resources = Assembly
        .GetManifestResourceNames()
        .Where(name => name.Replace('\\', '/').StartsWith(Root, StringComparison.Ordinal))
        .ToDictionary(name => name.Replace('\\', '/'), name => name, StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The Dapper entity classes, one file per table, by the name of the table.</summary>
    public static IReadOnlyList<(string FileName, string Content)> Entities { get; } =
        [.. Resources.Keys
            .Where(path => path.StartsWith(EntitiesFolder, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(path => (path[EntitiesFolder.Length..], Read(path)))];

    /// <summary>The queries, the validation part first, each part in the order of its numbers.</summary>
    public static IReadOnlyList<SedeQuery> Queries { get; } = ReadQueries();

    /// <summary>The entity files of the tables a query reads - what a conversion of it is given.</summary>
    public static IReadOnlyList<(string FileName, string Content)> EntitiesOf(SedeQuery query)
        => [.. Entities.Where(entity => query.Tables.Contains(Path.GetFileNameWithoutExtension(entity.FileName), StringComparer.OrdinalIgnoreCase))];

    private static IReadOnlyList<SedeQuery> ReadQueries()
    {
        var document = JsonSerializer.Deserialize<CatalogDocument>(Read(QueriesResource), JsonOptions)
            ?? throw new InvalidOperationException("Sede/queries.json is empty.");

        return document.Queries;
    }

    private static string Read(string path)
    {
        if (!Resources.TryGetValue(path, out var name))
        {
            throw new InvalidOperationException($"The resource '{path}' is not embedded in SampleData.");
        }

        using var stream = Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record CatalogDocument(string GeneratedBy, List<SedeQuery> Queries);
}
