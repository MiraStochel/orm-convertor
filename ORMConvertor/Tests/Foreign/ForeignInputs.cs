using System.Text.Json;
using System.Text.RegularExpressions;
using Model;
using SampleData;
using Tests.Combined;
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
///
/// Beside the units stands <c>schema.sql</c> (decision 207): the DDL of the catalog the tool
/// reads for the key - CREATE TABLE statements with keys and foreign keys, no rows, the schema
/// as the placeholder -, which the variant <c>schema</c> of the web channel hands the model and
/// the tool never reads, because it reads the catalog. A key the tool reads no catalog for has
/// none, and the manifest names the file where there is one.
/// </summary>
internal static class ForeignInputs
{
    public const string Variable = "ORMCONVERTOR_FOREIGN_INPUTS";

    public const string ManifestFileName = "input.json";

    public const string SchemaFileName = "schema.sql";

    internal const string SchemaPlaceholder = "{{schema}}";

    private const string FibenSchemaResourceName = "Tests.Database.Fiben.schema.sql";

    /// <summary>A statement that writes rows, at the start of a line of a batch: the batch is data, not schema.</summary>
    private static readonly Regex DataStatement = new(@"^\s*(INSERT|UPDATE|DELETE|MERGE|SET\s+IDENTITY_INSERT)\b", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>The text of a unit with the schema the suite substituted back as the placeholder, so that the file names no schema of this machine; the LDBC schema is named after the fixture's and keeps its suffix.</summary>
    internal static string Placeholder(string content) => content.Replace(TestDatabase.SchemaName, SchemaPlaceholder, StringComparison.Ordinal);

    /// <summary>
    /// Writes every input of the five kinds under the root - or those the filter takes, as the
    /// web channel writes the inputs of its sample (decision 189) - and returns how many
    /// directories it wrote.
    /// </summary>
    public static int Export(string root, Func<ForeignKey, ORMEnum, bool>? take = null)
    {
        var count = 0;

        void One(ForeignKey key, ORMEnum source, Func<IReadOnlyList<ConversionSource>> units, IReadOnlyList<string>? questions)
        {
            if (take is null || take(key, source))
            {
                Write(root, key, source, units(), questions, SchemaScript(key));
                count++;
            }
        }

        foreach (var query in DifferentialMatrix.Queries)
        {
            foreach (var source in query.Sources)
            {
                One(new ForeignKey(ForeignKey.Matrix, query.Id), source, () => query.Units(source), questions: null);
            }
        }

        foreach (var query in FibenSample.Queries)
        {
            One(new ForeignKey(ForeignKey.Fiben, query.Key), ORMEnum.Dapper, () => FibenRun.Units(query), query.Questions);
        }

        foreach (var framework in Enum.GetValues<ORMEnum>())
        {
            One(new ForeignKey(ForeignKey.Cross, framework.ToString()), framework, () => CrossFrameworkInputs.Units(framework), questions: null);
        }

        foreach (var query in LdbcSnbSample.Queries.Where(query => query.Sql is not null))
        {
            One(new ForeignKey(ForeignKey.Ldbc, query.Key), ORMEnum.Dapper, () => LdbcUnits(query), questions: null);
        }

        // The examples of the explanatory page, the excerpts of real projects among them - the
        // case study of T1 (decision 174): one input per example, in the language of its source.
        foreach (var example in ORMConvertorAPI.Data.Examples.GetExamples)
        {
            One(new ForeignKey(ForeignKey.Example, example.Key), example.SourceOrm, () => example.Units, questions: null);
        }

        return count;
    }

    /// <summary>The units of a query of the LDBC catalog: every entity of the catalog and the query as one more unit, as <c>Combined/LdbcCatalogTest</c> sends them.</summary>
    internal static List<ConversionSource> LdbcUnits(LdbcQuery query) =>
    [
        .. LdbcSnbSample.Entities.Select(entity => new ConversionSource { Name = entity.FileName, ContentType = ConversionContentType.CSharp, Content = entity.Content }),
        new ConversionSource { Name = $"{query.Key}.sql", ContentType = ConversionContentType.SqlQuery, Content = query.Sql! },
    ];

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
        => Write(root, key, source, units, questions, SchemaScript(key));

    /// <summary>
    /// The DDL of the catalog the tool reads for the key (decision 207), the schema as the
    /// placeholder: the fixture's tables for a key of the matrix, a scenario or a point of a
    /// series, the LDBC tables in the fixture's LDBC schema, the FIBEN tables, the excerpted
    /// project's tables for a case study; null where the tool reads no catalog (an example
    /// written for the tool, decision 099). Statements that carry data are left out: the catalog
    /// has none.
    /// </summary>
    internal static string? SchemaScript(ForeignKey key) => key.Kind switch
    {
        ForeignKey.Matrix or ForeignKey.Cross or ForeignKey.Series => string.Join("\nGO\n",
            new[] { TestSchemaFixture.ScriptResourceName, TestSchemaFixture.ShapesDataResourceName, TestSchemaFixture.DataResourceName }
                .SelectMany(resource => Ddl(TestSchemaFixture.ReadScript(resource, SchemaPlaceholder)))) + "\nGO\n",
        ForeignKey.Ldbc => string.Join("\nGO\n", Ddl(TestSchemaFixture.ReadScript(TestSchemaFixture.LdbcSchemaResourceName, SchemaPlaceholder + "_ldbc"))) + "\nGO\n",
        ForeignKey.Fiben => string.Join("\nGO\n", Ddl(TestSchemaFixture.ReadScript(FibenSchemaResourceName, SchemaPlaceholder))) + "\nGO\n",
        ForeignKey.Example when ORMConvertorAPI.Data.Examples.RealProjectKeys.Contains(key.Id)
            => ThirdPartySources.Schema(CaseStudySchemaTest.ProjectOf(key.Id)).Replace("\r\n", "\n", StringComparison.Ordinal),
        _ => null,
    };

    /// <summary>The batches of a script that define the schema: every batch without a statement that writes rows.</summary>
    private static IEnumerable<string> Ddl(string script)
        => TestSchemaFixture.Split(script).Where(batch => !DataStatement.IsMatch(batch));

    /// <summary>The DDL beside an exported input, or null where the manifest names none.</summary>
    public static string? SchemaOf(string directory)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, ManifestFileName)));
        return manifest.RootElement.TryGetProperty("schema", out var schema) && schema.ValueKind == JsonValueKind.String
            ? File.ReadAllText(Path.Combine(directory, schema.GetString()!))
            : null;
    }

    private static void Write(string root, ForeignKey key, ORMEnum source, IReadOnlyList<ConversionSource> units, IReadOnlyList<string>? questions, string? schemaScript)
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

        if (schemaScript is not null)
        {
            File.WriteAllText(Path.Combine(directory, SchemaFileName), schemaScript);
            manifest.Add($"  \"schema\": {JsonSerializer.Serialize(SchemaFileName)},");
        }

        manifest.Add("  \"units\": [");

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < units.Count; index++)
        {
            var unit = units[index];
            var path = Unique(unit.Name ?? $"unit-{index + 1}{Extension(unit.ContentType)}", taken);

            File.WriteAllText(Path.Combine(directory, path), Placeholder(unit.Content));

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
