using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using Model;
using OrmConvertor;
using Tests.Database;
using Tests.FibenJudge;

namespace Tests.Experiment;

/// <summary>
/// Every input the suites know, as one project of one framework: the artifacts the tool made
/// for the inputs of the experiment run (decision 173) into that framework, merged - every
/// entity class and its mapping once by name, every query - and written to disk as a client
/// would send a whole project. Six of them, one per framework, are the inputs of the run of
/// <see cref="WholeInputRun"/>: a realistic project many times the size of any single input,
/// to set beside the limits the series of decision 182 found and the 30 s of S3.
/// </summary>
internal sealed record WholeInput(
    ORMEnum Framework,
    string Directory,
    List<ConversionSource> Units,
    int Entities,
    int Queries,
    long Bytes,
    int FromInputs,
    int Refused,
    int Threw,
    int Unavailable,
    int DuplicateEntityArtifacts,
    int ConflictingEntityArtifacts,
    double MergeTranslateMs,
    double MergeCatalogMs)
{
    public const string ManifestFileName = "whole-input.json";

    /// <summary>The merged input written under the directory, or null where none was written yet.</summary>
    public static WholeInput? Read(string directory)
    {
        var manifestPath = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;

        var units = root.GetProperty("units").EnumerateArray()
            .Select(unit =>
            {
                var path = unit.GetProperty("path").GetString()!;
                return new ConversionSource
                {
                    Name = path,
                    ContentType = Enum.Parse<ConversionContentType>(unit.GetProperty("contentType").GetString()!),
                    Content = File.ReadAllText(Path.Combine(directory, path)),
                };
            })
            .ToList();

        return new WholeInput(
            Enum.Parse<ORMEnum>(root.GetProperty("framework").GetString()!),
            directory,
            units,
            root.GetProperty("entities").GetInt32(),
            root.GetProperty("queries").GetInt32(),
            root.GetProperty("bytes").GetInt64(),
            root.GetProperty("fromInputs").GetInt32(),
            root.GetProperty("refused").GetInt32(),
            root.GetProperty("threw").GetInt32(),
            root.GetProperty("unavailable").GetInt32(),
            root.GetProperty("duplicateEntityArtifacts").GetInt32(),
            root.GetProperty("conflictingEntityArtifacts").GetInt32(),
            root.GetProperty("mergeTranslateMs").GetDouble(),
            root.GetProperty("mergeCatalogMs").GetDouble());
    }

    /// <summary>Writes the units as files and the manifest beside them; the directory is created.</summary>
    public void Write()
    {
        System.IO.Directory.CreateDirectory(Directory);

        foreach (var unit in Units)
        {
            File.WriteAllText(Path.Combine(Directory, unit.Name!), unit.Content, new UTF8Encoding(false));
        }

        var manifest = new
        {
            framework = Framework.ToString(),
            fromInputs = FromInputs,
            refused = Refused,
            threw = Threw,
            unavailable = Unavailable,
            duplicateEntityArtifacts = DuplicateEntityArtifacts,
            conflictingEntityArtifacts = ConflictingEntityArtifacts,
            entities = Entities,
            queries = Queries,
            bytes = Bytes,
            mergeTranslateMs = MergeTranslateMs,
            mergeCatalogMs = MergeCatalogMs,
            units = Units.Select(unit => new { path = unit.Name, contentType = unit.ContentType.ToString() }).ToList(),
        };

        File.WriteAllText(Path.Combine(Directory, ManifestFileName), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
    }

    /// <summary>
    /// Builds the whole input of one framework: every input translated into it with the catalog
    /// the experiment run gives it, the artifacts of every translation without a Failure record
    /// merged. An artifact of an entity (its class, its mapping document) enters once per entity
    /// and content type - the text most inputs agree on, every other text of the same entity
    /// counted as a conflict -; a query enters in the form the consumer binds, named after the
    /// input it came from. A refused translation adds nothing and is counted, as decision 173
    /// reads a Failure.
    /// </summary>
    public static WholeInput Merge(ORMEnum framework, string directory, IReadOnlyList<ExperimentInput> inputs, TestSchemaFixture fixture, Action<string> log)
    {
        var queryUnits = new List<ConversionSource>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Every text an entity artifact was written with, by entity and content type, with how
        // many inputs wrote it and the first that did; keys in the order they were first seen.
        var entityArtifacts = new Dictionary<(ConversionContentType ContentType, string Entity), List<EntityVariant>>();
        var entityOrder = new List<(ConversionContentType ContentType, string Entity)>();

        var refused = 0;
        var threw = 0;
        var unavailable = 0;
        var fromInputs = 0;
        var queries = 0;
        var duplicates = 0;
        var translateMs = 0.0;
        var catalogMs = 0.0;

        foreach (var input in inputs)
        {
            if (ExperimentRun.Unavailable(input.Key, fixture) is { } reason)
            {
                unavailable++;
                log($"{input.Id} -> {framework}: left out, {reason}");
                continue;
            }

            var clock = Stopwatch.StartNew();
            var (conversion, artifact, refusal) = ExperimentRun.Translate(input, framework, fixture);
            translateMs += clock.Elapsed.TotalMilliseconds;

            if (conversion is null)
            {
                threw++;
                log($"{input.Id} -> {framework}: {refusal}");
                continue;
            }

            catalogMs += conversion.CatalogReadTime?.TotalMilliseconds ?? 0;

            if (artifact is null)
            {
                refused++;
                log($"{input.Id} -> {framework}: refused: {refusal}");
                continue;
            }

            fromInputs++;

            // A query enters in the one form the consumer binds (as the path takes it, ForeignArtifact.QueryMethods):
            // the C# or Java method; the SQL, HQL or JPQL text and the LINQ form written beside such a method stay
            // out. Where the method is a bare signature beside a mapper document that holds the query (MyBatis,
            // decision 081), the document is the query and the signature stays out.
            var hasBindingMethod = conversion.Sources.Any(made => made.Entity is null && made.ContentType is ConversionContentType.CSharpQuery or ConversionContentType.JavaQuery);
            var hasQueryDocument = conversion.Sources.Any(made => made.Entity is null && made.ContentType == ConversionContentType.XML);

            foreach (var made in conversion.Sources)
            {
                if (made.Entity is { } entity)
                {
                    var key = (made.ContentType, entity);
                    if (!entityArtifacts.TryGetValue(key, out var variants))
                    {
                        variants = [];
                        entityArtifacts[key] = variants;
                        entityOrder.Add(key);
                    }
                    else
                    {
                        duplicates++;
                    }

                    if (variants.FirstOrDefault(variant => string.Equals(variant.Content, made.Content, StringComparison.Ordinal)) is { } same)
                    {
                        same.Inputs++;
                    }
                    else
                    {
                        variants.Add(new EntityVariant(made.Content, made.Name ?? entity, input.Id));
                    }

                    continue;
                }

                var isBindingMethod = made.ContentType is ConversionContentType.CSharpQuery or ConversionContentType.JavaQuery;
                if (made.ContentType.IsQuery() && !isBindingMethod && hasBindingMethod)
                {
                    continue;
                }

                if (isBindingMethod && hasQueryDocument)
                {
                    continue;
                }

                if ((isBindingMethod && !hasQueryDocument) || made.ContentType == ConversionContentType.XML)
                {
                    queries++;
                }

                queryUnits.Add(Unit(made, $"{input.Id}--{made.Name ?? "unit"}", taken));
            }
        }

        // The definition most inputs agree on wins, the first seen on a tie; every other
        // definition of the same entity is a conflict, logged with the input it came from.
        var units = new List<ConversionSource>();
        var conflicts = 0;
        foreach (var key in entityOrder)
        {
            var variants = entityArtifacts[key];
            var chosen = variants.MaxBy(variant => variant.Inputs)!;
            units.Add(Unit(new ConversionSource { ContentType = key.ContentType, Content = chosen.Content }, chosen.Name, taken));

            if (variants.Count > 1)
            {
                conflicts += variants.Count - 1;
                log($"conflict {key.Entity} ({key.ContentType}): {variants.Count} definitions; kept the one of {chosen.Inputs} input(s), first {chosen.FirstInput}; dropped " +
                    string.Join(", ", variants.Where(variant => variant != chosen).Select(variant => $"{variant.Inputs}x from {variant.FirstInput}")));
            }
        }

        units.AddRange(queryUnits);

        var whole = new WholeInput(
            framework,
            directory,
            units,
            entityOrder.Select(key => key.Entity).Distinct(StringComparer.Ordinal).Count(),
            queries,
            units.Sum(unit => (long)Encoding.UTF8.GetByteCount(unit.Content)),
            fromInputs,
            refused,
            threw,
            unavailable,
            duplicates,
            conflicts,
            translateMs,
            catalogMs);

        whole.Write();
        return whole;
    }

    /// <summary>One text an entity artifact was written with: how many inputs wrote exactly it and the first of them.</summary>
    private sealed class EntityVariant(string content, string name, string firstInput)
    {
        public string Content => content;

        public string Name => name;

        public string FirstInput => firstInput;

        public int Inputs { get; set; } = 1;
    }

    /// <summary>An artifact as an input unit: its language instead of its role (decision 111), under a file name no other unit of the input has.</summary>
    private static ConversionSource Unit(ConversionSource artifact, string name, HashSet<string> taken)
    {
        var extension = Tests.Foreign.ForeignInputs.Extension(artifact.ContentType);
        if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            name += extension;
        }

        name = string.Concat(name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

        var candidate = name;
        for (var number = 2; !taken.Add(candidate); number++)
        {
            candidate = $"{name[..^extension.Length]}-{number}{extension}";
        }

        return new ConversionSource
        {
            Name = candidate,
            ContentType = artifact.ContentType.LanguageOf(),
            Content = artifact.Content,
        };
    }
}

/// <summary>One translation of a whole input into a target: the row of <c>whole-input-rows.tsv</c>.</summary>
internal sealed record WholeInputRow(
    ORMEnum Source,
    ORMEnum Target,
    int Units,
    int Entities,
    int Queries,
    long Bytes,
    double TranslateMs,
    double CatalogMs,
    long AllocatedKb,
    int Artifacts,
    int ArtifactEntities,
    int ArtifactQueries,
    int Failure,
    int Loss,
    int Convention,
    int Incompleteness,
    int Supplied,
    string CatalogState,
    string Outcome)
{
    public const string Header = "source\ttarget\tunits\tentities\tqueries\tbytes\ttranslateMs\tcatalogMs\tallocatedKb\tartifacts\tartifactEntities\tartifactQueries\tfailure\tloss\tconvention\tincompleteness\tsupplied\tcatalogState\toutcome";

    public const string Translated = "translated";

    public string Id => $"{Source}-{Target}";

    public string Row()
        => string.Join('\t',
            Source, Target,
            Units.ToString(CultureInfo.InvariantCulture),
            Entities.ToString(CultureInfo.InvariantCulture),
            Queries.ToString(CultureInfo.InvariantCulture),
            Bytes.ToString(CultureInfo.InvariantCulture),
            TranslateMs.ToString("F0", CultureInfo.InvariantCulture),
            CatalogMs.ToString("F0", CultureInfo.InvariantCulture),
            AllocatedKb.ToString(CultureInfo.InvariantCulture),
            Artifacts.ToString(CultureInfo.InvariantCulture),
            ArtifactEntities.ToString(CultureInfo.InvariantCulture),
            ArtifactQueries.ToString(CultureInfo.InvariantCulture),
            Failure.ToString(CultureInfo.InvariantCulture),
            Loss.ToString(CultureInfo.InvariantCulture),
            Convention.ToString(CultureInfo.InvariantCulture),
            Incompleteness.ToString(CultureInfo.InvariantCulture),
            Supplied.ToString(CultureInfo.InvariantCulture),
            CatalogState,
            Outcome.Replace('\t', ' ').Replace("\r", string.Empty).Replace('\n', ' '));

    public string Describe()
        => $"{Source} -> {Target}: {Units} units ({Entities} entities, {Queries} queries, {Bytes / 1024} KB) in {TranslateMs / 1000:F1} s, of it catalog {CatalogMs / 1000:F1} s, {AllocatedKb / 1024} MB allocated; {Artifacts} artifacts, records: {Failure} Failure, {Loss} Loss, {Convention} Convention, {Incompleteness} Incompleteness, {Supplied} Supplied; {Outcome}";

    public static WholeInputRow Parse(string line)
    {
        var fields = line.Split('\t');
        return new WholeInputRow(
            Enum.Parse<ORMEnum>(fields[0]),
            Enum.Parse<ORMEnum>(fields[1]),
            int.Parse(fields[2], CultureInfo.InvariantCulture),
            int.Parse(fields[3], CultureInfo.InvariantCulture),
            int.Parse(fields[4], CultureInfo.InvariantCulture),
            long.Parse(fields[5], CultureInfo.InvariantCulture),
            double.Parse(fields[6], CultureInfo.InvariantCulture),
            double.Parse(fields[7], CultureInfo.InvariantCulture),
            long.Parse(fields[8], CultureInfo.InvariantCulture),
            int.Parse(fields[9], CultureInfo.InvariantCulture),
            int.Parse(fields[10], CultureInfo.InvariantCulture),
            int.Parse(fields[11], CultureInfo.InvariantCulture),
            int.Parse(fields[12], CultureInfo.InvariantCulture),
            int.Parse(fields[13], CultureInfo.InvariantCulture),
            int.Parse(fields[14], CultureInfo.InvariantCulture),
            int.Parse(fields[15], CultureInfo.InvariantCulture),
            int.Parse(fields[16], CultureInfo.InvariantCulture),
            fields[17],
            fields[18]);
    }

    public static List<WholeInputRow> ReadAll(string path)
        => File.Exists(path)
            ? File.ReadAllLines(path).Skip(1).Where(line => line.Length > 0).Select(Parse).ToList()
            : [];

    public static void WriteAll(string path, IEnumerable<WholeInputRow> rows)
    {
        var text = new StringBuilder();
        text.Append(Header).Append('\n');
        foreach (var row in rows.OrderBy(row => row.Source).ThenBy(row => row.Target))
        {
            text.Append(row.Row()).Append('\n');
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }
}

/// <summary>
/// The run over the whole inputs: every whole input translated into every target, the six
/// included into themselves, with the time of the translation and the time of the catalog of
/// it (S3 separates them), the memory allocated, and a brief look at the output - the counts
/// of artifacts and of records by kind, the Failure and Loss reasons by count. Nothing is
/// judged: the artifacts go nowhere, the run measures what a project of this size costs the
/// tool. Nothing here asserts about the tool; a Failure record is a count in a row.
/// </summary>
internal static class WholeInputRun
{
    public const string DirectoryName = "whole-input";
    public const string InputsDirectoryName = "inputs";
    public const string RowsFileName = "whole-input-rows.tsv";
    public const string ReasonsFileName = "whole-input-reasons.txt";
    public const string SummaryFileName = "whole-input-summary.md";

    /// <summary>The 30 s S3 allows a project of 100 entities and 100 queries; the summary sets the run beside it.</summary>
    public static readonly TimeSpan TranslationBound = TimeSpan.FromSeconds(30);

    /// <summary>The directory of the run under the experiment results, or null without a configured run.</summary>
    public static string? Directory
        => ExperimentRun.ResultsDirectory is { } results ? Path.Combine(results, DirectoryName) : null;

    public static string InputDirectory(string directory, ORMEnum framework) => Path.Combine(directory, InputsDirectoryName, framework.ToString());

    /// <summary>
    /// The catalog of the run: the databases every input was translated with, asked in turn -
    /// the fixture (its own schema and the empty LDBC tables beside it), the case-study database
    /// (decision 176), the Fiben database (decision 149) - because the whole input spans the
    /// tables of all of them. A project has one database; this run has one project made of
    /// inputs over four, so the catalog is the union. The readers are fresh and cache nothing,
    /// so the catalog time of a row is what a translation of the project pays.
    /// </summary>
    public static UnionCatalogReader Catalog(TestSchemaFixture fixture, out string description)
    {
        var readers = new List<ICatalogReader>();
        var names = new List<string>();

        if (fixture.IsAvailable)
        {
            readers.Add(new SqlServerCatalogReader(TestDatabase.ConnectionString!));
            names.Add("fixture");
        }

        if (fixture.IsCaseStudyAvailable)
        {
            readers.Add(new SqlServerCatalogReader(TestDatabase.CaseStudyConnectionString!));
            names.Add("case studies");
        }

        if (FibenDatabase.IsConfigured)
        {
            readers.Add(new SqlServerCatalogReader(FibenDatabase.ConnectionString!));
            names.Add("Fiben");
        }

        description = names.Count == 0 ? "none" : string.Join(", ", names);
        return new UnionCatalogReader(readers);
    }

    /// <summary>Translates the whole input into the target and measures it; a translation that throws is a row too.</summary>
    public static (WholeInputRow Row, ConversionResult? Conversion) Translate(WholeInput source, ORMEnum target, ICatalogReader catalog)
    {
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var clock = Stopwatch.StartNew();
        ConversionResult conversion;
        try
        {
            conversion = ConversionHandler.Convert(source.Framework, target, source.Units, catalog);
        }
        catch (Exception exception)
        {
            clock.Stop();
            return (new WholeInputRow(
                source.Framework, target, source.Units.Count, source.Entities, source.Queries, source.Bytes,
                clock.Elapsed.TotalMilliseconds, 0, Math.Max(0, GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore) / 1024,
                0, 0, 0, 0, 0, 0, 0, 0, CatalogConnectionState.NotConfigured.ToString(),
                "threw: " + exception.GetBaseException().Message), null);
        }

        clock.Stop();
        var allocatedKb = Math.Max(0, GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore) / 1024;

        int Count(ConversionRecordKind kind) => conversion.Records.Count(record => record.Kind == kind);

        return (new WholeInputRow(
            source.Framework, target, source.Units.Count, source.Entities, source.Queries, source.Bytes,
            clock.Elapsed.TotalMilliseconds,
            conversion.CatalogReadTime?.TotalMilliseconds ?? 0,
            allocatedKb,
            conversion.Sources.Count,
            conversion.Sources.Where(artifact => artifact.Entity is not null).Select(artifact => artifact.Entity!).Distinct(StringComparer.Ordinal).Count(),
            conversion.Sources.Count(artifact => artifact.ContentType is ConversionContentType.CSharpQuery or ConversionContentType.JavaQuery),
            Count(ConversionRecordKind.Failure),
            Count(ConversionRecordKind.Loss),
            Count(ConversionRecordKind.Convention),
            Count(ConversionRecordKind.Incompleteness),
            Count(ConversionRecordKind.Supplied),
            conversion.CatalogState.ToString(),
            WholeInputRow.Translated), conversion);
    }

    /// <summary>The Failure and Loss reasons of one translation by count, most frequent first - the brief look at the output; appended to the reasons file.</summary>
    public static IEnumerable<string> Reasons(ConversionResult conversion, int top = 25)
    {
        foreach (var kind in new[] { ConversionRecordKind.Failure, ConversionRecordKind.Loss })
        {
            var groups = conversion.Records
                .Where(record => record.Kind == kind)
                .GroupBy(record => Generalize(record.Reason), StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .ToList();

            if (groups.Count == 0)
            {
                continue;
            }

            yield return $"  {kind}: {groups.Sum(group => group.Count())} records, {groups.Count} distinct reasons";
            foreach (var group in groups.Take(top))
            {
                var example = group.First();
                yield return $"    {group.Count(),6}  {group.Key}";
                yield return $"            e.g. [{example.Unit}]{(example.Query is { } query ? " " + query : string.Empty)}{(example.Entity is { } entity ? " " + entity : string.Empty)}: {Shorten(example.Reason)}";
            }

            if (groups.Count > top)
            {
                yield return $"    ... and {groups.Count - top} more reasons";
            }
        }
    }

    private static string Shorten(string reason)
    {
        var text = reason.Replace("\r", string.Empty).Replace('\n', ' ');
        return text.Length <= 400 ? text : text[..400] + "...";
    }

    /// <summary>A reason with its quoted names and numbers taken out, so that the same reason over different entities counts once.</summary>
    private static string Generalize(string reason)
    {
        var text = new StringBuilder();
        var quoted = false;
        var digits = false;
        foreach (var character in reason.Replace("\r", string.Empty).Replace('\n', ' '))
        {
            if (character is '"' or '\'' or '`')
            {
                if (!quoted)
                {
                    text.Append('<').Append('>');
                }

                quoted = !quoted;
                digits = false;
                continue;
            }

            if (quoted)
            {
                continue;
            }

            if (char.IsDigit(character))
            {
                if (!digits)
                {
                    text.Append('#');
                }

                digits = true;
                continue;
            }

            digits = false;
            text.Append(character);
        }

        return text.ToString();
    }

    /// <summary>The summary over the whole inputs and the rows: the six inputs, the matrix of times, the matrix of Failure records, and where the run stands against S3.</summary>
    public static IEnumerable<string> Summary(string directory, IReadOnlyList<WholeInput> inputs, IReadOnlyList<WholeInputRow> rows, string catalog, string tree)
    {
        var frameworks = Enum.GetValues<ORMEnum>();

        yield return "# The whole input";
        yield return string.Empty;
        yield return $"Every input of the experiment run merged into one project per framework and translated into every framework, {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC, tree `{tree}`. Catalog of the translations: {catalog}. Nothing judged; the time is the translation's, the catalog's time inside it is given beside it (S3).";
        yield return string.Empty;
        yield return "## The six whole inputs";
        yield return string.Empty;
        yield return "| Framework | Inputs merged | Refused | Threw | Left out | Units | Entities | Queries | KB | Duplicate entity artifacts (conflicting) | Merge: translation s | Merge: catalog s |";
        yield return "|---|---|---|---|---|---|---|---|---|---|---|---|";
        foreach (var input in inputs.OrderBy(input => input.Framework))
        {
            yield return $"| {input.Framework} | {input.FromInputs} | {input.Refused} | {input.Threw} | {input.Unavailable} | {input.Units.Count} | {input.Entities} | {input.Queries} | {input.Bytes / 1024} | {input.DuplicateEntityArtifacts} ({input.ConflictingEntityArtifacts}) | {input.MergeTranslateMs / 1000:F1} | {input.MergeCatalogMs / 1000:F1} |";
        }

        yield return string.Empty;
        yield return "## Translation time in seconds, source (row) into target (column); the catalog's share in parentheses";
        yield return string.Empty;
        yield return "| Source \\ Target | " + string.Join(" | ", frameworks) + " |";
        yield return "|---|" + string.Concat(frameworks.Select(_ => "---|"));
        foreach (var source in frameworks)
        {
            var cells = frameworks.Select(target => rows.FirstOrDefault(row => row.Source == source && row.Target == target) is { } row
                ? row.Outcome == WholeInputRow.Translated ? $"{row.TranslateMs / 1000:F1} ({row.CatalogMs / 1000:F1})" : $"{row.TranslateMs / 1000:F1} {row.Outcome}"
                : "—");
            yield return $"| **{source}** | " + string.Join(" | ", cells) + " |";
        }

        yield return string.Empty;
        yield return "## Memory allocated in MB";
        yield return string.Empty;
        yield return "| Source \\ Target | " + string.Join(" | ", frameworks) + " |";
        yield return "|---|" + string.Concat(frameworks.Select(_ => "---|"));
        foreach (var source in frameworks)
        {
            var cells = frameworks.Select(target => rows.FirstOrDefault(row => row.Source == source && row.Target == target) is { } row ? $"{row.AllocatedKb / 1024}" : "—");
            yield return $"| **{source}** | " + string.Join(" | ", cells) + " |";
        }

        yield return string.Empty;
        yield return "## Records: Failure / Loss, and artifacts out (entities, queries)";
        yield return string.Empty;
        yield return "| Source \\ Target | " + string.Join(" | ", frameworks) + " |";
        yield return "|---|" + string.Concat(frameworks.Select(_ => "---|"));
        foreach (var source in frameworks)
        {
            var cells = frameworks.Select(target => rows.FirstOrDefault(row => row.Source == source && row.Target == target) is { } row
                ? $"{row.Failure} / {row.Loss}; {row.Artifacts} ({row.ArtifactEntities}, {row.ArtifactQueries})"
                : "—");
            yield return $"| **{source}** | " + string.Join(" | ", cells) + " |";
        }

        var translated = rows.Where(row => row.Outcome == WholeInputRow.Translated).ToList();
        if (translated.Count > 0)
        {
            var slowest = translated.MaxBy(row => row.TranslateMs)!;
            var slowestWithoutCatalog = translated.MaxBy(row => row.TranslateMs - row.CatalogMs)!;
            var largest = inputs.MaxBy(input => input.Entities + input.Queries)!;
            yield return string.Empty;
            yield return "## Against S3";
            yield return string.Empty;
            yield return $"- S3 allows {TranslationBound.TotalSeconds:F0} s for 100 entities and 100 queries; the largest whole input has {largest.Entities} entities and {largest.Queries} queries ({largest.Framework}).";
            yield return $"- The slowest translation of the {translated.Count} that returned: {slowest.Source} -> {slowest.Target}, {slowest.TranslateMs / 1000:F1} s with the catalog's {slowest.CatalogMs / 1000:F1} s inside it, {slowest.TranslateMs / TranslationBound.TotalMilliseconds * 100:F0} % of the bound.";
            yield return $"- The slowest without the catalog's time: {slowestWithoutCatalog.Source} -> {slowestWithoutCatalog.Target}, {(slowestWithoutCatalog.TranslateMs - slowestWithoutCatalog.CatalogMs) / 1000:F1} s, {(slowestWithoutCatalog.TranslateMs - slowestWithoutCatalog.CatalogMs) / TranslationBound.TotalMilliseconds * 100:F0} % of the bound.";
            yield return $"- Rows that did not return: {rows.Count - translated.Count}.";
        }

        yield return string.Empty;
        yield return $"Rows: `{RowsFileName}`; the Failure and Loss reasons of every translation by count: `{ReasonsFileName}`; the six inputs: `{InputsDirectoryName}/<framework>/`.";
    }
}

/// <summary>
/// Several catalogs asked as one (the run of <see cref="WholeInputRun"/> only): a request goes
/// to the readers in turn and the first image wins; a request no reader resolves keeps the
/// ambiguity the first reader reported, or stays unresolved. Junction tables are the union of
/// what every reader finds.
/// </summary>
internal sealed class UnionCatalogReader(IReadOnlyList<ICatalogReader> readers) : ICatalogReader, IDisposable
{
    public IReadOnlyDictionary<string, TableLookup> ReadTables(IReadOnlyList<TableRequest> requests)
    {
        var results = new Dictionary<string, TableLookup>(StringComparer.Ordinal);
        var pending = requests.ToList();

        foreach (var reader in readers)
        {
            if (pending.Count == 0)
            {
                break;
            }

            var found = reader.ReadTables(pending);
            var still = new List<TableRequest>();
            foreach (var request in pending)
            {
                if (found.TryGetValue(request.Key, out var lookup) && lookup.Image is not null)
                {
                    results[request.Key] = lookup;
                    continue;
                }

                if (lookup is { AmbiguousMatches.Count: > 0 } && !results.ContainsKey(request.Key))
                {
                    results[request.Key] = lookup;
                }

                still.Add(request);
            }

            pending = still;
        }

        foreach (var request in pending)
        {
            results.TryAdd(request.Key, new TableLookup());
        }

        return results;
    }

    public IReadOnlyList<TableImage> FindJunctionTables(IReadOnlyCollection<TableImage> referencedTables)
        => readers.SelectMany(reader => reader.FindJunctionTables(referencedTables)).ToList();

    public void Dispose()
    {
        foreach (var reader in readers.OfType<IDisposable>())
        {
            reader.Dispose();
        }
    }
}
