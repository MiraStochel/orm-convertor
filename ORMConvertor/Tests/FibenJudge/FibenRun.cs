using System.Diagnostics;
using System.Globalization;
using System.Text;
using AbstractWrappers.Diagnostics;
using AdvisorBenchmarking;
using DatabaseCatalog;
using Microsoft.Data.SqlClient;
using Model;
using OrmConvertor;
using SampleData;
using Tests.Differential;

namespace Tests.FibenJudge;

/// <summary>What one query did in one framework over the stages of the run (decision 149).</summary>
internal sealed class FibenCell
{
    public required string Key { get; init; }

    public required ORMEnum Framework { get; init; }

    /// <summary>The reasons the target refused the query, or null where it translated it.</summary>
    public string? Refused { get; set; }

    /// <summary>Whether the target wrote the query in native SQL (the third value of decision 113).</summary>
    public bool FellBack { get; set; }

    /// <summary>The features the target could not say in its own language, where it fell back.</summary>
    public string? FallbackFeatures { get; set; }

    /// <summary>Why the artifact could not be compiled or its factory built, or null when it could.</summary>
    public string? Unprepared { get; set; }

    /// <summary>Why the artifact did not return rows, or null when it did.</summary>
    public string? Failed { get; set; }

    /// <summary>The rows the artifact returned, in the canonical form of decision 089; null until it ran.</summary>
    public List<string>? Rows { get; set; }

    public TimeSpan Translated { get; set; }

    public TimeSpan Compiled { get; set; }

    /// <summary>The time of the one read that produced the rows.</summary>
    public TimeSpan Selected { get; set; }

    /// <summary>The median of one execution by the policy of decision 145, or null where the cell was not measured.</summary>
    public double? MedianMilliseconds { get; set; }

    public int WarmupIterations { get; set; }

    public int Iterations { get; set; }

    public string? MeasurementFailed { get; set; }

    /// <summary>Where the query landed in the target: its own language, native SQL, a refusal, or a failure after the translation.</summary>
    public string Outcome => Refused is not null ? "refused" : Unprepared is not null ? "not compiled" : Failed is not null ? "failed" : FellBack ? "native SQL" : "translated";

    public bool Ran => Rows is not null;

    public string Describe()
    {
        var cell = $"{Key} in {Framework}";
        if (Refused is not null) return $"{cell}: refused: {Refused}";
        if (Unprepared is not null) return $"{cell}: the artifact could not be made: {Unprepared}";
        if (Failed is not null) return $"{cell}: the artifact failed to run: {Failed}";
        return $"{cell}: {Rows!.Count} rows{(FellBack ? " (native SQL)" : string.Empty)}";
    }
}

/// <summary>
/// The on-demand run of the FIBEN catalog in the .NET suite (decision 149): every query of the
/// catalog translated from Dapper into the three .NET frameworks with the Fiben database as the
/// catalog and only the entities of the tables it reads, the artifacts compiled, each run once
/// and its rows put into the canonical form of decision 089, the targets compared with the
/// source - Dapper with the catalog's own text, which judges the text - and the source compared
/// with the judge where the database holds one, and finally every cell measured by the policy
/// of decision 145. The run happens once per test run, for the first test that asks for it,
/// and writes its files into the directory the environment names: the cells, the source rows
/// in the form of <c>database/fiben/judge.tsv</c>, from which the judge is made after the
/// first documented run, and the summary.
///
/// The Java suite runs the three Java frameworks the same way against an instance whose
/// catalog is the Fiben database, with the text itself over JDBC as its source.
/// </summary>
internal sealed class FibenRun
{
    public static readonly ORMEnum[] Frameworks = [ORMEnum.Dapper, ORMEnum.EFCore, ORMEnum.NHibernate];

    private static readonly ResultRow.RenderSettings Settings = new();

    private static readonly Lazy<FibenRun> Instance = new(Run, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// How many rows of a result the judge keeps as rows; a larger result is kept as its digest
    /// alone - the count and the SHA-256 of the canonical rows -, because three queries of the
    /// catalog return over 100 000 rows and the judge is a file of the repository.
    /// </summary>
    public const int RowsKeptByTheJudge = 1000;

    private readonly Dictionary<(string Key, ORMEnum Framework), FibenCell> cells = [];
    private readonly Dictionary<string, (string Digest, List<string> Rows)> judge = new(StringComparer.OrdinalIgnoreCase);

    private FibenRun(string? unavailable) => Unavailable = unavailable;

    public static FibenRun Current => Instance.Value;

    /// <summary>Why the run could not happen, or null when it did.</summary>
    public string? Unavailable { get; }

    /// <summary>The data set the database holds, as the loader named it in <c>fiben.dataset</c>.</summary>
    public string? DataSet { get; private set; }

    /// <summary>Whether the database holds a judge; the first documented run has none and makes it.</summary>
    public bool HasJudge { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public TimeSpan CatalogRead { get; private set; }

    public IReadOnlyList<FibenQuery> Queries { get; private set; } = [];

    public FibenCell Cell(string key, ORMEnum framework) => cells[(key, framework)];

    public bool Covers(string key) => cells.ContainsKey((key, ORMEnum.Dapper));

    /// <summary>The rows the judge keeps for a query - all of them up to <see cref="RowsKeptByTheJudge"/>, none beyond -, or null where the judge has no entry.</summary>
    public IReadOnlyList<string>? JudgeRows(string key) => judge.TryGetValue(key, out var entry) ? entry.Rows : null;

    /// <summary>The digest the judge holds for a query, or null where it has no entry.</summary>
    public string? JudgeDigest(string key) => judge.TryGetValue(key, out var entry) ? entry.Digest : null;

    /// <summary>
    /// The digest of a result in the canonical form: its row count and the SHA-256 of the rows
    /// joined by a line feed, as the judge's line at position 0 writes it. The same in the Java suite.
    /// </summary>
    public static string Digest(IReadOnlyList<string> rows)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', rows));
        return $"rows={rows.Count} sha256={Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))}";
    }

    public void SkipIfUnavailable()
    {
        if (Unavailable is not null)
        {
            FibenDatabase.Unavailable(Unavailable, FibenDatabase.IsRequired);
        }
    }

    /// <summary>Skips a query the run was limited away from.</summary>
    public void SkipIfNotCovered(string key)
    {
        if (!Covers(key))
        {
            Assert.Skip($"The run was limited to {string.Join(", ", FibenDatabase.Keys?.Order(StringComparer.Ordinal) ?? Enumerable.Empty<string>())} and did not cover {key}.");
        }
    }

    private static FibenRun Run()
    {
        if (!FibenDatabase.IsConfigured)
        {
            return new FibenRun(FibenDatabase.NotConfiguredReason);
        }

        var connectionString = FibenDatabase.ConnectionString!;
        SqlConnection control;
        string? dataSet;

        try
        {
            control = new SqlConnection(connectionString);
            control.Open();
            dataSet = Scalar<string>(control, "SELECT CAST(value AS NVARCHAR(200)) FROM sys.extended_properties WHERE class = 0 AND name = N'fiben.dataset'");
        }
        catch (Exception exception)
        {
            return new FibenRun($"The FIBEN database \"{FibenDatabase.ConnectionStringName}\" is configured but cannot be reached: {exception.Message}");
        }

        using (control)
        {
            var run = new FibenRun(null) { DataSet = dataSet };
            run.Execute(control, connectionString);
            return run;
        }
    }

    private void Execute(SqlConnection control, string connectionString)
    {
        var clock = Stopwatch.StartNew();
        ReadJudge(control);

        Queries = [.. FibenSample.Queries.Where(query => FibenDatabase.Keys is null || FibenDatabase.Keys.Contains(query.Key))];

        using var catalog = new CachingCatalogReader(new SqlServerCatalogReader(connectionString));

        foreach (var query in Queries)
        {
            var prepared = new Dictionary<ORMEnum, PreparedQuery>();

            try
            {
                foreach (var framework in Frameworks)
                {
                    var cell = cells[(query.Key, framework)] = new FibenCell { Key = query.Key, Framework = framework };

                    var started = Stopwatch.GetTimestamp();
                    ConversionResult conversion;
                    try
                    {
                        conversion = ConversionHandler.Convert(ORMEnum.Dapper, framework, Units(query), catalog);
                    }
                    catch (Exception exception)
                    {
                        cell.Translated = Stopwatch.GetElapsedTime(started);
                        cell.Refused = "the conversion threw: " + Innermost(exception).Message;
                        continue;
                    }

                    cell.Translated = Stopwatch.GetElapsedTime(started);
                    if (framework == Frameworks[0] && CatalogRead == TimeSpan.Zero)
                    {
                        CatalogRead = cell.Translated;
                    }

                    var failures = conversion.Records.Where(record => record.Kind == ConversionRecordKind.Failure).ToList();
                    if (failures.Count > 0)
                    {
                        cell.Refused = string.Join("; ", failures.Select(failure => failure.Reason).Distinct());
                        continue;
                    }

                    var fallbacks = conversion.Records.Where(record => record.Kind == ConversionRecordKind.Fallback).ToList();
                    cell.FellBack = fallbacks.Count > 0;
                    cell.FallbackFeatures = cell.FellBack
                        ? string.Join("; ", fallbacks.Select(record => $"{record.Feature?.ToString() ?? "-"}: {record.Reason}").Distinct())
                        : null;

                    started = Stopwatch.GetTimestamp();
                    try
                    {
                        prepared[framework] = PreparedQuery.Prepare($"Fiben_{query.Key}_{framework}", framework, conversion, connectionString);
                    }
                    catch (Exception exception)
                    {
                        cell.Compiled = Stopwatch.GetElapsedTime(started);
                        cell.Unprepared = Innermost(exception).Message;
                        continue;
                    }

                    cell.Compiled = Stopwatch.GetElapsedTime(started);

                    started = Stopwatch.GetTimestamp();
                    try
                    {
                        var rows = prepared[framework].Run(query.Key, [], query.Columns, projection: true);
                        cell.Selected = Stopwatch.GetElapsedTime(started);
                        cell.Rows = Canonical(query, rows);
                    }
                    catch (Exception exception)
                    {
                        cell.Selected = Stopwatch.GetElapsedTime(started);
                        cell.Failed = Innermost(exception).Message;
                    }
                }

                if (FibenDatabase.Measures)
                {
                    foreach (var (framework, artifact) in prepared)
                    {
                        var cell = cells[(query.Key, framework)];
                        if (cell.Failed is not null)
                        {
                            continue;
                        }

                        Measure(cell, () => artifact.Run(query.Key, [], query.Columns, projection: true));
                    }
                }
            }
            finally
            {
                foreach (var artifact in prepared.Values)
                {
                    artifact.Dispose();
                }
            }
        }

        Elapsed = clock.Elapsed;
        WriteResults();
    }

    /// <summary>
    /// The policy of decision 145 over one cell: warm-up until two consecutive executions agree
    /// within the tolerance, at most the maximum; then executions until the accumulated time
    /// reaches the target and at least the minimum; the median of one execution is the number.
    /// </summary>
    private static void Measure(FibenCell cell, Action execute)
    {
        try
        {
            var warmups = new List<double>();
            do
            {
                warmups.Add(TimeOne(execute));
            }
            while (warmups.Count < 2
                || (warmups.Count < BenchmarkPolicy.MaxWarmupIterations && !Agree(warmups[^2], warmups[^1])));

            var measured = new List<double>();
            double total = 0;
            while (measured.Count < BenchmarkPolicy.MinIterations || total < BenchmarkPolicy.TargetMilliseconds)
            {
                var milliseconds = TimeOne(execute);
                measured.Add(milliseconds);
                total += milliseconds;
            }

            var sorted = measured.Order().ToList();
            cell.MedianMilliseconds = sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
            cell.WarmupIterations = warmups.Count;
            cell.Iterations = measured.Count;
        }
        catch (Exception exception)
        {
            cell.MeasurementFailed = Innermost(exception).Message;
        }
    }

    private static double TimeOne(Action execute)
    {
        var watch = Stopwatch.StartNew();
        execute();
        return watch.Elapsed.TotalMilliseconds;
    }

    private static bool Agree(double previous, double current)
    {
        var slower = Math.Max(previous, current);
        return slower <= 0 || Math.Abs(previous - current) <= BenchmarkPolicy.WarmupTolerance * slower;
    }

    private static List<string> Canonical(FibenQuery query, List<object?[]> rows)
    {
        var rendered = rows.Select(row => ResultRow.Render(row, Settings));
        return query.Ordered ? [.. rendered] : ResultRow.Sorted(rendered);
    }

    /// <summary>The units of a conversion: the entities of the tables the query reads, then the query as one more unit.</summary>
    internal static List<ConversionSource> Units(FibenQuery query) =>
    [
        .. FibenSample.EntitiesOf(query).Select(entity => new ConversionSource
        {
            Name = entity.FileName,
            ContentType = ConversionContentType.CSharp,
            Content = entity.Content,
        }),
        new ConversionSource { Name = $"{query.Key}.sql", ContentType = ConversionContentType.SqlQuery, Content = query.Sql },
    ];

    private void ReadJudge(SqlConnection control)
    {
        if (Scalar<int?>(control, $"SELECT OBJECT_ID(N'[{FibenDatabase.Schema}].[Judge]', N'U')") is null)
        {
            return;
        }

        using var command = control.CreateCommand();
        command.CommandText = $"SELECT [QueryKey], [Position], [Rendered] FROM [{FibenDatabase.Schema}].[Judge] ORDER BY [QueryKey], [Position];";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var key = reader.GetString(0);
            if (!judge.TryGetValue(key, out var entry))
            {
                judge[key] = entry = (string.Empty, []);
            }

            if (reader.GetInt32(1) == 0)
            {
                judge[key] = (reader.GetString(2), entry.Rows);
            }
            else
            {
                entry.Rows.Add(reader.GetString(2));
            }
        }

        HasJudge = true;
    }

    /// <summary>The lines of the summary: the run as a whole, then per framework, then per query.</summary>
    public IEnumerable<string> Describe()
    {
        yield return string.Create(
            CultureInfo.InvariantCulture,
            $"FIBEN run over {DataSet ?? "an unnamed data set"}: {Queries.Count} queries in {Frameworks.Length} frameworks in {Elapsed.TotalMinutes:F1} min; "
            + $"first catalog read {CatalogRead.TotalMilliseconds:F0} ms; judge {(HasJudge ? "present" : "absent - this run can make one")}; "
            + $"measurement {(FibenDatabase.Measures ? "by the policy of decision 145" : "left out")}.");

        foreach (var framework in Frameworks)
        {
            var mine = cells.Values.Where(cell => cell.Framework == framework).ToList();
            var outcomes = mine.GroupBy(cell => cell.Outcome).OrderBy(group => group.Key).Select(group => $"{group.Count()} {group.Key}");
            var agreeing = mine.Count(cell => cell.Ran && AgreesWithSource(cell));
            var measured = mine.Where(cell => cell.MedianMilliseconds is not null).ToList();

            var measurement = measured.Count == 0
                ? string.Empty
                : FormattableString.Invariant($"; measured {measured.Count} cells, median of medians {Median(measured.Select(cell => cell.MedianMilliseconds!.Value)):F1} ms, sum of medians {measured.Sum(cell => cell.MedianMilliseconds!.Value) / 1000:F1} s");

            var ran = mine.Count(cell => cell.Ran);
            var translation = mine.Sum(cell => cell.Translated.TotalSeconds);
            var compilation = mine.Sum(cell => cell.Compiled.TotalSeconds);
            var reads = mine.Sum(cell => cell.Selected.TotalSeconds);
            yield return FormattableString.Invariant(
                $"{framework}: {string.Join(", ", outcomes)}; {agreeing} of {ran} runs agree with the source; translation {translation:F1} s, compilation {compilation:F1} s, first reads {reads:F1} s{measurement}.");
        }

        foreach (var query in Queries)
        {
            var parts = Frameworks.Select(framework =>
            {
                var cell = cells[(query.Key, framework)];
                var median = cell.MedianMilliseconds is { } milliseconds ? string.Create(CultureInfo.InvariantCulture, $" {milliseconds:F1} ms") : string.Empty;
                var agreement = framework == Frameworks[0] ? string.Empty : cell.Ran ? (AgreesWithSource(cell) ? " =" : " !=") : string.Empty;
                return $"{framework} {cell.Outcome}{agreement}{median}";
            });

            var source = cells[(query.Key, Frameworks[0])];
            var judged = HasJudge ? (AgreesWithJudge(query.Key) switch { true => ", judge =", false => ", judge !=", null => ", judge -" }) : string.Empty;
            yield return $"{query.Key} ({query.Nesting}, {(source.Ran ? source.Rows!.Count + " rows" : "no rows")}{judged}): {string.Join("; ", parts)}";
        }
    }

    public bool AgreesWithSource(FibenCell cell)
        => cells[(cell.Key, Frameworks[0])].Rows is { } source && cell.Rows is { } rows && rows.SequenceEqual(source);

    /// <summary>
    /// Whether the source agrees with the judge - by the digest, which every entry has, and by
    /// the rows where the judge keeps them -; null where the source has no rows to compare or
    /// there is no judge. A query that returned no rows has an entry with the digest of none;
    /// a query the judge has no entry for was not run when the judge was made, so it agrees
    /// with nothing.
    /// </summary>
    public bool? AgreesWithJudge(string key)
    {
        if (!HasJudge || cells[(key, Frameworks[0])].Rows is not { } rows)
        {
            return null;
        }

        if (JudgeDigest(key) is not { } digest)
        {
            return false;
        }

        return Digest(rows) == digest && (JudgeRows(key) is not { Count: > 0 } kept || kept.SequenceEqual(rows));
    }

    /// <summary>What the source differs from the judge in, for a failure message.</summary>
    public string JudgeDifference(string key)
    {
        if (JudgeDigest(key) is not { } digest)
        {
            return "the judge holds no entry for it - the run it was made from did not run it, and a new judge is a decision (149)";
        }

        var rows = cells[(key, Frameworks[0])].Rows!;
        var kept = JudgeRows(key) ?? [];
        return kept.Count > 0 || rows.Count <= RowsKeptByTheJudge
            ? Difference(kept, rows)
            : $"the judge holds {digest}, the source returned {Digest(rows)}";
    }

    /// <summary>The first row where two renderings differ, both ways.</summary>
    public static string Difference(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var index = 0;
        while (index < expected.Count && index < actual.Count && expected[index] == actual[index])
        {
            index++;
        }

        return $"expected {expected.Count} rows, got {actual.Count}; row {index + 1}:"
            + Environment.NewLine + "  expected " + (index < expected.Count ? expected[index] : "(no row)")
            + Environment.NewLine + "  actual   " + (index < actual.Count ? actual[index] : "(no row)");
    }

    private void WriteResults()
    {
        if (FibenDatabase.ResultsDirectory is not { } directory)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

        var cellsFile = new StringBuilder();
        cellsFile.AppendLine("key\tnesting\tframework\toutcome\tfellBack\ttranslateMs\tcompileMs\tfirstReadMs\trows\tagreesWithSource\tagreesWithJudge\tmedianMs\twarmups\titerations\tdetail");
        foreach (var query in Queries)
        {
            foreach (var framework in Frameworks)
            {
                var cell = cells[(query.Key, framework)];
                var detail = cell.Refused ?? cell.Unprepared ?? cell.Failed ?? cell.MeasurementFailed ?? cell.FallbackFeatures ?? string.Empty;
                cellsFile.AppendLine(string.Join('\t',
                    query.Key, query.Nesting, framework, cell.Outcome, cell.FellBack ? "1" : "0",
                    cell.Translated.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                    cell.Compiled.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                    cell.Selected.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                    cell.Rows?.Count.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    framework == Frameworks[0] ? string.Empty : cell.Ran ? (AgreesWithSource(cell) ? "1" : "0") : string.Empty,
                    framework == Frameworks[0] ? AgreesWithJudge(query.Key) switch { true => "1", false => "0", null => string.Empty } : string.Empty,
                    cell.MedianMilliseconds?.ToString("F1", CultureInfo.InvariantCulture) ?? string.Empty,
                    cell.MedianMilliseconds is null ? string.Empty : cell.WarmupIterations.ToString(CultureInfo.InvariantCulture),
                    cell.MedianMilliseconds is null ? string.Empty : cell.Iterations.ToString(CultureInfo.InvariantCulture),
                    detail.Replace('\t', ' ').Replace("\r", string.Empty).Replace('\n', ' ')));
            }
        }

        File.WriteAllText(Path.Combine(directory, "cells.tsv"), cellsFile.ToString());

        // The source rows in the form of database/fiben/judge.tsv: what the judge is made from
        // once a run is documented. Only the source's rows, and only where the source ran.
        var judgeFile = new StringBuilder();
        judgeFile.Append("# The judge of the FIBEN catalog (decision 149): the rows of every query in the canonical form of decision 089,\n");
        judgeFile.Append($"# as the catalog's own text returned them over {DataSet} through Dapper on {stamp}.\n");
        judgeFile.Append($"# key<TAB>position<TAB>rendered row; position 0 is the digest of the whole result (row count and SHA-256 of the rows joined by LF),\n");
        judgeFile.Append($"# the rows follow it where the result has at most {RowsKeptByTheJudge} of them. A query without a line was not run. Made by the .NET suite's FibenRunTest.\n");
        foreach (var query in Queries)
        {
            if (cells[(query.Key, Frameworks[0])].Rows is not { } rows)
            {
                continue;
            }

            judgeFile.Append(query.Key).Append("\t0\t").Append(Digest(rows)).Append('\n');
            if (rows.Count > RowsKeptByTheJudge)
            {
                continue;
            }

            for (var position = 0; position < rows.Count; position++)
            {
                judgeFile.Append(query.Key).Append('\t').Append(position + 1).Append('\t').Append(rows[position]).Append('\n');
            }
        }

        File.WriteAllText(Path.Combine(directory, "judge-candidate.tsv"), judgeFile.ToString(), new UTF8Encoding(false));

        File.WriteAllLines(Path.Combine(directory, "summary.txt"), Describe());
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }

    private static Exception Innermost(Exception exception)
        => exception is System.Reflection.TargetInvocationException { InnerException: { } inner } ? Innermost(inner) : exception;

    private static T? Scalar<T>(SqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is null or DBNull ? default : (T)value;
    }
}
