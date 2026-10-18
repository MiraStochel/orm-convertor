using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SampleData;

/// <summary>
/// The FIBEN benchmark as input to the tool (decision 149): Dapper entity classes over the
/// 152 tables of <c>database/fiben/schema.sql</c>, and the 237 distinct SQL queries of the
/// benchmark, each with the natural-language question it answers, converted into T-SQL over
/// those tables. Both are generated from the benchmark's files by <c>FibenImport</c> and
/// embedded under <c>Fiben/</c> of this assembly; the queries as one JSON document, the
/// entities one file per table, so that a conversion is given only the entities of the tables
/// its query reads.
///
/// Dapper is the source for the reason it is for LDBC: the benchmark's queries are SQL, and a
/// Dapper source states no key, relation or type of its own - every such fact comes from the
/// catalog, and with the Fiben database as the catalog it comes from the very tables the data
/// was loaded into.
///
/// FIBEN (J. Sen et al., ATHENA++, PVLDB 2020) is published by IBM under Apache-2.0, data
/// included. This is not a run of the benchmark: the tool does not query in natural language;
/// the question stands beside each query as its intent, for the reader and for the comparison
/// with language models (T4, T5).
/// </summary>
public static class FibenSample
{
    /// <summary>
    /// The data set the texts are written over, named as the loader records it in the extended
    /// property <c>fiben.dataset</c> of the Fiben database: data.zip of IBM/fiben-benchmark at
    /// the commit the image downloads it from.
    /// </summary>
    public const string DataSet = "fiben-benchmark-96eaffc";

    private const string Root = "Fiben/";
    private const string EntitiesFolder = Root + "Entities/";
    private const string QueriesResource = Root + "queries.json";

    private static readonly Assembly Assembly = typeof(FibenSample).Assembly;

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

    /// <summary>The 237 queries, in the order of their numbers.</summary>
    public static IReadOnlyList<FibenQuery> Queries { get; } = ReadQueries();

    /// <summary>The entity files of the tables a query reads - what a conversion of it is given.</summary>
    public static IReadOnlyList<(string FileName, string Content)> EntitiesOf(FibenQuery query)
        => [.. Entities.Where(entity => query.Tables.Contains(Path.GetFileNameWithoutExtension(entity.FileName), StringComparer.OrdinalIgnoreCase))];

    private static IReadOnlyList<FibenQuery> ReadQueries()
    {
        var document = JsonSerializer.Deserialize<CatalogDocument>(Read(QueriesResource), JsonOptions)
            ?? throw new InvalidOperationException("Fiben/queries.json is empty.");

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

    private sealed record CatalogDocument(string GeneratedBy, List<FibenQuery> Queries);
}
