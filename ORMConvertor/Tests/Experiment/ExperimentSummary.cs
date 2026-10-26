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
    private static readonly string[] ForeignRowFiles = [ForeignRows.FileName, "java-" + ForeignRows.FileName, WebAttempts.RowsFileName];

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
        var allTool = ToolRowFiles.Select(file => Path.Combine(directory, file)).Where(File.Exists).SelectMany(SummaryRow.Read).ToList();
        var foreign = ForeignRowFiles.Select(file => Path.Combine(directory, file)).Where(File.Exists).SelectMany(SummaryRow.Read).ToList();
        var mappingFile = Path.Combine(directory, ExperimentRun.MappingRowsFileName);
        var allMapping = File.Exists(mappingFile) ? SummaryRow.Read(mappingFile) : [];

        // The points of the search for limits are another population of the same path (decision
        // 182) and not the baseline of T2 × T3 (decision 175): they are read apart, below.
        var tool = allTool.Where(row => ForeignKey.Parse(row.Answers).Kind != ForeignKey.Series).ToList();
        var mapping = allMapping.Where(row => ForeignKey.Parse(row.Answers).Kind != ForeignKey.Series).ToList();
        var javaSeries = allTool.Where(row => ForeignKey.Parse(row.Answers).Kind == ForeignKey.Series && EcosystemOf(row.Target) == "Java").ToList();

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
            lines.AddRange(Paired(tool, foreign));
        }

        lines.AddRange(Limits(directory, javaSeries));

        return lines;
    }

    /// <summary>
    /// The section on limits (decision 182), over the points the search for limits wrote: per
    /// axis and direction, the last point the tool translated within the bound of S3 without a
    /// refusal, an exception or a crash; the last point whose artifact the target accepted (the
    /// third level - for a Java target from the rows the Java suite wrote over the same input);
    /// the last point whose query ran over the fixture; the first point that failed and why, or
    /// the ceiling; and the time and the memory of the translation at the last point translated.
    /// </summary>
    private static IEnumerable<string> Limits(string directory, List<SummaryRow> javaSeries)
    {
        var file = Path.Combine(directory, LimitSearch.FileName);
        var points = SeriesPoint.Read(file);
        if (points.Count == 0)
        {
            yield break;
        }

        yield return "## Limits (decision 182)";
        yield return string.Empty;
        yield return $"Points of the search for limits: {points.Count} ({LimitSearch.FileName}); rows of the Java suite over series points: {javaSeries.Count}.";
        yield return "Columns: last translated = the largest point the tool translated within the bound of S3 (30 s) without a refusal, an exception or a crash; last accepted = the largest point whose artifact the target framework accepted (level 3; for a Java target from the rows of the Java suite);";
        yield return "last ran = the largest point whose query ran over the fixture (level 4; a series over no table of the fixture has none); stopped at = the first point that failed and why, or \"ceiling\" where the series ended without a failure; s / MB = the time and the memory of the translation at the last point translated.";
        yield return string.Empty;

        var javaReached = javaSeries
            .GroupBy(row => row["artifact"], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Max(row => row.Reached), StringComparer.Ordinal);
        var catalogs = points.Select(point => point.Catalog).Distinct().Count();

        foreach (var axis in LimitSeries.Axes)
        {
            var ofAxis = points.Where(point => point.Axis == axis.Id).ToList();
            if (ofAxis.Count == 0)
            {
                continue;
            }

            yield return $"### {axis.Id} — {axis.Grows} (points {axis.Points[0]} … {axis.Points[^1]}{(axis.Uncapped ? ", cap off" : string.Empty)})";
            yield return string.Empty;
            yield return "| direction | last translated | last accepted | last ran | stopped at | s | MB |";
            yield return "|---|---|---|---|---|---|---|";

            foreach (var direction in ofAxis.GroupBy(point => (point.Verdict.Source, point.Verdict.Target, point.Catalog)).OrderBy(group => group.Key.Source, StringComparer.Ordinal).ThenBy(group => group.Key.Target, StringComparer.Ordinal).ThenBy(group => group.Key.Catalog, StringComparer.Ordinal))
            {
                var walked = direction.OrderBy(point => point.Point).ToList();
                var passed = walked.Where(point => point.Step == SeriesPoint.Pass).ToList();
                var last = passed.LastOrDefault();
                var stop = walked.FirstOrDefault(point => point.Step != SeriesPoint.Pass);

                int Reached(SeriesPoint point) => EcosystemOf(Enum.Parse<ORMEnum>(point.Verdict.Target)) == "Java"
                    ? javaReached.GetValueOrDefault(point.Id, -1)
                    : point.Verdict.Reached;

                var accepted = passed.Where(point => Reached(point) >= ForeignLevel.Accepted).Select(point => (int?)point.Point).LastOrDefault();
                var ran = passed.Where(point => Reached(point) >= ForeignLevel.Ran).Select(point => (int?)point.Point).LastOrDefault();

                var label = $"{direction.Key.Source} → {direction.Key.Target}" + (catalogs > 1 ? $" ({direction.Key.Catalog})" : string.Empty);
                yield return $"| {label} | {last?.Point.ToString(CultureInfo.InvariantCulture) ?? "—"} | {accepted?.ToString(CultureInfo.InvariantCulture) ?? "—"} | {(axis.OverTheFixture ? ran?.ToString(CultureInfo.InvariantCulture) ?? "—" : "n/a")} | "
                    + (stop is null ? "ceiling" : $"{stop.Point.ToString(CultureInfo.InvariantCulture)}: {stop.Verdict.Outcome}")
                    + $" | {(last is null ? "—" : (last.TranslateMs / 1000).ToString("F2", CultureInfo.InvariantCulture))} | {(last is null ? "—" : (last.AllocatedKb / 1024.0).ToString("F1", CultureInfo.InvariantCulture))} |";
            }

            yield return string.Empty;
        }
    }

    /// <summary>
    /// The foreign artifacts beside the tool over the same cells (decision 189): a sample of
    /// attempts stands only beside the tool's share over the same sample. A cell is a key, a source
    /// and a target; the tool has one row in it (S2), a group of attempts - producer, model,
    /// channel, variant - has its attempts. Per measure the section gives the tool's share over the
    /// cells, the share per attempt, and best-of-n: the cells at least one attempt reached it in
    /// (decision 175). A cell without a row of the tool is named and left out of the shares.
    /// </summary>
    private static IEnumerable<string> Paired(List<SummaryRow> tool, List<SummaryRow> foreign)
    {
        static string CellOf(SummaryRow row) => $"{row.Answers} {row.Source} → {row.Target}";
        static string GroupOf(SummaryRow row) => $"{row.Producer} · {Or(row.Metadata("model"), "-")} · {Or(row.Metadata("channel"), "-")} · {Or(row.Metadata("variant"), "-")}";
        static string Verdict(SummaryRow row) => $"{row.Reached} {row.Outcome}";
        static int Attempt(SummaryRow row) => int.TryParse(row.Metadata("attempt"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var attempt) ? attempt : 0;

        var toolByCell = tool.GroupBy(CellOf, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        // The measures of T3 in the order of the levels, each with the cells it is counted over.
        (string Name, Func<SummaryRow, bool> Over, Func<SummaryRow, bool> Met)[] measures =
        [
            ("read", _ => true, row => row.Reached >= ForeignLevel.Read),
            ("compiled", _ => true, row => row.Reached >= ForeignLevel.Compiled),
            ("accepted", _ => true, row => row.Reached >= ForeignLevel.Accepted),
            ("ran", row => HasFourthLevel(row.Answers), row => row.Reached >= ForeignLevel.Ran),
            ("agrees", row => HasEquivalenceMeasure(row.Answers), row => row.Outcome == ForeignVerdict.Agrees),
        ];

        yield return "## Foreign artifacts beside the tool over the same cells (decision 189)";
        yield return string.Empty;
        yield return "A cell is a key, a source and a target. Columns: cells = the cells of the group with a row of the tool; attempts = the rows of the group over them; refused replies = replies the import refused for a trace of a search, made again;";
        yield return "the tool = the cells its one row reached the measure in; per attempt = the attempts that reached it; best-of-n = the cells at least one attempt reached it in. ran is counted over the cells with a fourth level, agrees over those with a measure of equivalence.";
        yield return string.Empty;
        yield return "| group | cells | attempts | refused replies | measure | the tool | per attempt | best-of-n |";
        yield return "|---|---|---|---|---|---|---|---|";

        var unpaired = new List<string>();
        var cellLines = new List<string>();

        foreach (var group in foreign.GroupBy(GroupOf, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var cells = group.GroupBy(CellOf, StringComparer.Ordinal).OrderBy(cell => cell.Key, StringComparer.Ordinal).ToList();
            unpaired.AddRange(cells.Where(cell => !toolByCell.ContainsKey(cell.Key)).Select(cell => $"{group.Key}: {cell.Key}"));

            var paired = cells.Where(cell => toolByCell.ContainsKey(cell.Key)).ToList();
            var attempts = paired.SelectMany(cell => cell).ToList();
            var refused = attempts.Sum(row => int.TryParse(row.Metadata("refusedBefore"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : 0);

            var first = true;
            foreach (var (name, over, met) in measures)
            {
                var cellsOver = paired.Where(cell => over(toolByCell[cell.Key])).ToList();
                var attemptsOver = cellsOver.SelectMany(cell => cell).ToList();
                var byTool = cellsOver.Count(cell => met(toolByCell[cell.Key]));
                var best = cellsOver.Count(cell => cell.Any(met));

                yield return first
                    ? $"| {group.Key} | {paired.Count} | {attempts.Count} | {refused} | {name} | {ShareOf(byTool, cellsOver.Count)} | {ShareOf(attemptsOver.Count(met), attemptsOver.Count)} | {ShareOf(best, cellsOver.Count)} |"
                    : $"| | | | | {name} | {ShareOf(byTool, cellsOver.Count)} | {ShareOf(attemptsOver.Count(met), attemptsOver.Count)} | {ShareOf(best, cellsOver.Count)} |";
                first = false;
            }

            cellLines.AddRange(paired.Select(cell =>
                $"| {cell.Key} | {group.Key} | {Verdict(toolByCell[cell.Key])} | {string.Join(" · ", cell.OrderBy(Attempt).ThenBy(row => row["artifact"], StringComparer.Ordinal).Select(Verdict))} |"));
        }

        yield return string.Empty;

        if (unpaired.Count > 0)
        {
            yield return $"Cells without a row of the tool, left out of the shares: {string.Join("; ", unpaired)}. The experiment run over the inputs of the run writes their rows.";
            yield return string.Empty;
        }

        yield return "### Cell by cell";
        yield return string.Empty;
        yield return "| cell | group | the tool | attempts |";
        yield return "|---|---|---|---|";
        foreach (var line in cellLines)
        {
            yield return line;
        }

        yield return string.Empty;
    }

    private static string ShareOf(int count, int of)
        => of == 0 ? "—" : string.Create(CultureInfo.InvariantCulture, $"{count} of {of} ({100.0 * count / of:F0} %)");

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
