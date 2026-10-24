using System.Globalization;
using System.Text;
using System.Text.Json;
using Model;
using ORMConvertorAPI.Data;
using SampleData;
using Tests.Differential;
using Tests.Foreign;

namespace Tests.Experiment;

/// <summary>One row of a rows file, by column name - the rows of the tool and of foreign artifacts have the same columns (decision 172).</summary>
internal sealed record SummaryRow(IReadOnlyDictionary<string, string> Columns)
{
    public string this[string column] => Columns.TryGetValue(column, out var value) ? value : string.Empty;

    public string Answers => this["answers"];
    public ORMEnum Source => Enum.Parse<ORMEnum>(this["source"], ignoreCase: true);
    public ORMEnum Target => Enum.Parse<ORMEnum>(this["target"], ignoreCase: true);
    public string Producer => this["producer"];
    public string Language => this["language"];
    public int Reached => int.TryParse(this["reached"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var reached) ? reached : 0;
    public string Outcome => this["outcome"];

    /// <summary>A field of the metadata object, or an empty string where the metadata has none.</summary>
    public string Metadata(string field)
    {
        var metadata = this["metadata"];
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(field, out var value)
                ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString()
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    public static List<SummaryRow> Read(string file)
    {
        var lines = File.ReadAllLines(file).Where(line => line.Length > 0).ToList();
        if (lines.Count == 0)
        {
            return [];
        }

        var header = lines[0].Split('\t');
        return lines.Skip(1)
            .Select(line =>
            {
                var values = line.Split('\t');
                var columns = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var index = 0; index < header.Length; index++)
                {
                    columns[header[index]] = index < values.Length ? values[index] : string.Empty;
                }

                return new SummaryRow(columns);
            })
            .ToList();
    }
}

/// <summary>
/// The table of T2 × T3 (decision 173), computed from the rows the two suites wrote into the
/// directory of a run: the shares of T3 - parsable, compilable, accepted, runnable, functionally
/// equivalent, and the completeness of the transferred mapping facts - per pair of ecosystems,
/// per direction, per category of T2 and per kind of input, over the rows of the tool; and the
/// same shares over the rows of foreign artifacts, per producer, model and variant as their
/// metadata name them (decision 175). Written as Markdown so that the record of the run in
/// <c>docs/runs/</c> takes the table as it is.
/// </summary>
internal static class ExperimentSummary
{
    public const string FileName = "summary.md";

    private static readonly string[] ToolRowFiles = [ExperimentRun.RowsFileName, ExperimentRun.JavaRowsFileName];
    private static readonly string[] ForeignRowFiles = [ForeignRows.FileName, "java-" + ForeignRows.FileName];

    /// <summary>The kind the key names, the excerpts of real projects read apart from the other examples (decision 174).</summary>
    public static string KindOf(string answers)
    {
        var key = ForeignKey.Parse(answers);
        return key.Kind == ForeignKey.Example && Examples.RealProjectKeys.Contains(key.Id) ? "case study" : key.Kind;
    }

    /// <summary>The category of T2 a matrix key belongs to, the nesting type of a FIBEN query, the workload of an LDBC query; the kind for the rest.</summary>
    public static string CategoryOf(string answers)
    {
        var key = ForeignKey.Parse(answers);
        return key.Kind switch
        {
            ForeignKey.Matrix => DifferentialMatrix.Queries.SingleOrDefault(query => query.Id == key.Id)?.Category ?? "query of the matrix's own",
            ForeignKey.Fiben => FibenSample.Queries.SingleOrDefault(query => query.Key.Equals(key.Id, StringComparison.OrdinalIgnoreCase))?.Nesting.ToString() ?? key.Kind,
            ForeignKey.Ldbc => LdbcSnbSample.Queries.SingleOrDefault(query => query.Key.Equals(key.Id, StringComparison.OrdinalIgnoreCase))?.Workload.ToString() ?? key.Kind,
            _ => KindOf(answers),
        };
    }

    /// <summary>Whether the key's translations have a fourth level: a measure of one reading (decision 172) - the matrix and the FIBEN catalog -, or the empty tables of a case study's schema in the fixture (decision 176).</summary>
    public static bool HasFourthLevel(string answers)
        => HasEquivalenceMeasure(answers) || KindOf(answers) == "case study";

    /// <summary>Whether the key has a measure the rows are compared with (decision 172): the matrix and the FIBEN catalog; a case study runs and has none.</summary>
    public static bool HasEquivalenceMeasure(string answers)
        => ForeignKey.Parse(answers).Kind is ForeignKey.Matrix or ForeignKey.Fiben;

    public static string EcosystemOf(ORMEnum framework)
        => framework is ORMEnum.Dapper or ORMEnum.EFCore or ORMEnum.NHibernate ? ".NET" : "Java";

    public static string PairOf(SummaryRow row) => $"{EcosystemOf(row.Source)} → {EcosystemOf(row.Target)}";

    /// <summary>Writes the summary of the directory and returns its lines.</summary>
    public static List<string> Write(string directory)
    {
        var lines = Summarize(directory);
        File.WriteAllText(Path.Combine(directory, FileName), string.Join('\n', lines) + "\n", new UTF8Encoding(false));
        return lines;
    }

    public static List<string> Summarize(string directory)
    {
        var tool = ToolRowFiles.Select(file => Path.Combine(directory, file)).Where(File.Exists).SelectMany(SummaryRow.Read).ToList();
        var foreign = ForeignRowFiles.Select(file => Path.Combine(directory, file)).Where(File.Exists).SelectMany(SummaryRow.Read).ToList();
        var mappingFile = Path.Combine(directory, ExperimentRun.MappingRowsFileName);
        var mapping = File.Exists(mappingFile) ? SummaryRow.Read(mappingFile) : [];

        var lines = new List<string>
        {
            "# Experiment run",
            string.Empty,
            $"Written {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)} on {Environment.MachineName} over {directory}.",
            $"Rows of the tool: {tool.Count} ({Present(directory, ToolRowFiles)}); rows of foreign artifacts: {foreign.Count} ({Present(directory, ForeignRowFiles)}); mapping rows: {mapping.Count}.",
            $"Tool version in the rows: {string.Join(", ", tool.Select(row => row.Metadata("toolVersion")).Where(version => version.Length > 0).Distinct().Order(StringComparer.Ordinal).DefaultIfEmpty("-"))}.",
            string.Empty,
            "Columns: n = translations; refused = no artifact (a Failure record); native SQL = the query went out in the dialect's SQL through the framework's API (decision 113);",
            "read, compiled, accepted = the artifact reached that level (T3: parsable, compilable; the framework accepts it); n4 = translations of a key with a fourth level (the matrix, the FIBEN catalog, a case study over the empty tables of its schema), refusals included;",
            "ran = of n4, the artifact ran - the query returned rows, every query of a case study ran (T3: runnable); agrees = of the translations of n4 with a measure of equivalence (the matrix, the FIBEN catalog), the rows agree with it (T3: functionally equivalent);",
            "mapping = the facts the parser of the target reads back out of the artifact, of the facts the reference holds that the target can express (T3: completeness of transferred mapping facts), pooled over the group.",
            string.Empty,
        };

        var toolMapping = mapping.Where(row => row.Producer == ExperimentRun.Producer).ToList();
        lines.AddRange(Table("## The tool, by pair of ecosystems", tool, toolMapping, row => PairOf(row)));
        lines.AddRange(Table("## The tool, by direction", tool, toolMapping, row => $"{row.Source} → {row.Target}"));
        lines.AddRange(Table("## The tool, by kind of input and pair of ecosystems", tool, toolMapping, row => $"{KindOf(row.Answers)} | {PairOf(row)}", "kind | pair"));
        lines.AddRange(Table(
            "## The tool, by category of T2 and pair of ecosystems (the keys of the matrix)",
            tool.Where(row => ForeignKey.Parse(row.Answers).Kind == ForeignKey.Matrix).ToList(),
            toolMapping.Where(row => ForeignKey.Parse(row.Answers).Kind == ForeignKey.Matrix).ToList(),
            row => $"{CategoryOf(row.Answers)} | {PairOf(row)}",
            "category | pair"));
        lines.AddRange(Table(
            "## The tool, by category of the other catalogs and target",
            tool.Where(row => ForeignKey.Parse(row.Answers).Kind is ForeignKey.Fiben or ForeignKey.Ldbc).ToList(),
            toolMapping.Where(row => ForeignKey.Parse(row.Answers).Kind is ForeignKey.Fiben or ForeignKey.Ldbc).ToList(),
            row => $"{CategoryOf(row.Answers)} | {row.Target}",
            "category | target"));

        if (foreign.Count > 0)
        {
            var foreignMapping = mapping.Where(row => row.Producer != ExperimentRun.Producer).ToList();
            lines.AddRange(Table(
                "## Foreign artifacts, by producer, model, variant and pair of ecosystems",
                foreign,
                foreignMapping,
                row => $"{row.Producer} | {Or(row.Metadata("model"), "-")} | {Or(row.Metadata("variant"), "-")} | {PairOf(row)}",
                "producer | model | variant | pair"));
            lines.AddRange(Table(
                "## Foreign artifacts, by producer, model, variant and kind of input",
                foreign,
                foreignMapping,
                row => $"{row.Producer} | {Or(row.Metadata("model"), "-")} | {Or(row.Metadata("variant"), "-")} | {KindOf(row.Answers)}",
                "producer | model | variant | kind"));
        }

        return lines;
    }

    private static string Present(string directory, string[] files)
        => string.Join(", ", files.Select(file => File.Exists(Path.Combine(directory, file)) ? file : $"no {file}"));

    private static string Or(string value, string fallback) => value.Length > 0 ? value : fallback;

    private static IEnumerable<string> Table(string title, List<SummaryRow> rows, List<SummaryRow> mapping, Func<SummaryRow, string> group, string groupHeader = "group")
    {
        yield return title;
        yield return string.Empty;

        if (rows.Count == 0)
        {
            yield return "_no rows_";
            yield return string.Empty;
            yield break;
        }

        yield return $"| {groupHeader} | n | refused | native SQL | read | compiled | accepted | n4 | ran | agrees | mapping |";
        yield return $"|{string.Concat(Enumerable.Repeat("---|", groupHeader.Split('|').Length))}---|---|---|---|---|---|---|---|---|---|";

        var mappingByGroup = mapping.GroupBy(group, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var grouped in rows.GroupBy(group, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var n = grouped.Count();
            var refused = grouped.Count(row => row.Outcome == ForeignVerdict.Refused);
            var nativeSql = grouped.Count(row => row.Language == ForeignArtifact.LanguageNativeSql);
            var read = grouped.Count(row => row.Reached >= ForeignLevel.Read);
            var compiled = grouped.Count(row => row.Reached >= ForeignLevel.Compiled);
            var accepted = grouped.Count(row => row.Reached >= ForeignLevel.Accepted);

            // The fourth level exists for a key whose measure is one reading - the matrix and the
            // FIBEN catalog - and for a case study over its schema (decision 176), and for every
            // translation of such a key, a refusal included, so that the share of runnable
            // translations has the denominator the share of readable ones has; only an artifact
            // that carries no query (the outcome "accepted" under such a key) has nothing to run
            // and stands outside. Agreement is counted over the keys that have a measure to agree
            // with: a case study ran, and over empty tables nobody states its rows.
            var fourth = grouped.Where(row => HasFourthLevel(row.Answers) && row.Outcome != ForeignVerdict.Accepted).ToList();
            var ran = fourth.Count(row => row.Reached >= ForeignLevel.Ran);
            var measured = fourth.Where(row => HasEquivalenceMeasure(row.Answers)).ToList();
            var agrees = measured.Count(row => row.Outcome == ForeignVerdict.Agrees);

            var mappingRows = mappingByGroup.GetValueOrDefault(grouped.Key, []);
            var expressible = mappingRows.Sum(row => Number(row["expressible"]));
            var readBack = mappingRows.Sum(row => Number(row["readBack"]));

            yield return $"| {grouped.Key} | {n} | {refused} | {nativeSql} | {Share(read, n)} | {Share(compiled, n)} | {Share(accepted, n)} | {fourth.Count} | {Share(ran, fourth.Count)} | {Share(agrees, measured.Count)} | {Share(readBack, expressible)} |";
        }

        yield return string.Empty;
    }

    private static int Number(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;

    private static string Share(int count, int of)
        => of == 0 ? "—" : string.Create(CultureInfo.InvariantCulture, $"{count} ({100.0 * count / of:F0} %)");
}
