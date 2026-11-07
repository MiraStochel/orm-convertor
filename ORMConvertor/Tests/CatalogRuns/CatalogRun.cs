using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using AbstractWrappers.Diagnostics;
using AdvisorBenchmarking;
using DatabaseCatalog;
using Microsoft.Data.SqlClient;
using Model;
using OrmConvertor;
using Tests.Differential;

namespace Tests.CatalogRuns;

/// <summary>What one query did in one framework over the stages of the run (decisions 149 and 208).</summary>
internal sealed class CatalogCell
{
    public required string Key { get; init; }

    public required ORMEnum Framework { get; init; }

    /// <summary>The reasons the target refused the query, or null where it translated it.</summary>
    public string? Refused { get; set; }

    /// <summary>Whether the target wrote the query in native SQL (the third value of decision 113).</summary>
    public bool FellBack { get; set; }

    /// <summary>The features the target could not say in its own language, where it fell back.</summary>
    public string? FallbackFeatures { get; set; }

    /// <summary>What the translation left out with a Loss record (decision 070): a projection outside the vocabulary, an ordering it does not carry. The artifact is poorer by it, and a read that misses a column says so.</summary>
    public string? Lost { get; set; }

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
/// The settings of the server a run reads before it starts (decision 164): the MAXDOP the
/// catalog's database runs its plans with - its own database-scoped value where that is not
/// zero, the server's otherwise -, the server's memory, and the processors it sees, which the
/// cpuset of the container limits. The first two decide whether the run measures (decision
/// 166): the measurement mode runs single-threaded plans on processors of the server's own,
/// and the judge's mode, whose plans are single-threaded too, gives the server every processor
/// of the VM. The processors need VIEW SERVER STATE and are null without it.
/// </summary>
internal sealed record CatalogServer(int Maxdop, int MaxServerMemoryMegabytes, int? Processors)
{
    /// <summary>
    /// The processors of the database in the measurement mode: the default cpuset of the
    /// databases in <c>docker-compose.yml</c>, which <c>CatalogRunTest</c> holds equal.
    /// </summary>
    public const int MeasurementProcessors = 2;

    public static CatalogServer Read(SqlConnection connection)
    {
        int maxdop, memory;
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT COALESCE(NULLIF((SELECT CAST(value AS INT) FROM sys.database_scoped_configurations WHERE name = N'MAXDOP'), 0), "
                + "(SELECT CAST(value_in_use AS INT) FROM sys.configurations WHERE name = N'max degree of parallelism')), "
                + "(SELECT CAST(value_in_use AS INT) FROM sys.configurations WHERE name = N'max server memory (MB)')";
            using var reader = command.ExecuteReader();
            reader.Read();
            (maxdop, memory) = (reader.GetInt32(0), reader.GetInt32(1));
        }

        int? processors;
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT cpu_count FROM sys.dm_os_sys_info";
            processors = (int)command.ExecuteScalar()!;
        }
        catch (SqlException)
        {
            processors = null;
        }

        return new CatalogServer(maxdop, memory, processors);
    }

    /// <summary>
    /// Why a run leaves its measurement out, or null when it measures: the environment may leave
    /// it out, and a server outside the measurement mode - parallel plans, or processors other
    /// than its own two, as in the judge's mode - leaves it out by itself, so that no number
    /// comes from it.
    /// </summary>
    public static string? MeasurementLeftOut(bool configured, int maxdop, int? processors)
        => !configured ? "left out"
            : maxdop != 1 ? $"left out: the server runs its plans with MAXDOP {maxdop}, not in the measurement mode (decision 164)"
            : processors != MeasurementProcessors
                ? $"left out: the server has {processors?.ToString(CultureInfo.InvariantCulture) ?? "an unknown number of"} processors, not the {MeasurementProcessors} of its own the measurement mode gives it (decision 166)"
            : null;

    public string Describe()
        => FormattableString.Invariant($"MAXDOP {Maxdop}, max server memory {MaxServerMemoryMegabytes} MB, {Processors?.ToString(CultureInfo.InvariantCulture) ?? "unknown"} processors");
}

/// <summary>
/// One query of a catalog as the run reads it, whatever the catalog: its key, the group the
/// summary sorts it under (Kim's type for FIBEN, the part of the set for SEDE), the columns of
/// its result and the key of its ordering (decision 170), the units of its conversion - the
/// entities of the tables it reads and the text as one more unit -, the values of its
/// parameters by name, and whether the catalog claims it translated at all.
/// </summary>
internal sealed record CatalogRunQuery(
    string Key,
    string Group,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> OrderingKey,
    Func<List<ConversionSource>> Units,
    IReadOnlyList<(string Name, string SqlType, string Value)> Arguments,
    bool IsTranslated);

/// <summary>What a run is over: the catalog's name, its database, the data set its texts are written over and the queries.</summary>
internal sealed record CatalogDefinition(
    string Name,
    CatalogDatabase Database,
    string DataSetProperty,
    string ExpectedDataSet,
    IReadOnlyList<CatalogRunQuery> Queries);

/// <summary>
/// The on-demand run of a query catalog in the .NET suite (decisions 149 and 208): every query
/// of the catalog translated from Dapper into the three .NET frameworks with the catalog's
/// database as the catalog and only the entities of the tables it reads, the artifacts
/// compiled, each run once with the catalog's values and its rows put into the canonical form
/// of decision 089, the targets compared with the source - Dapper with the catalog's own text,
/// which judges the text - and the source compared with the judge where the database holds
/// one, and finally every cell measured by the policy of decision 145. The run happens once
/// per test run and catalog, for the first test that asks for it, and writes its files into
/// the directory the environment names: the cells, the source rows in the form of
/// <c>judge.tsv</c>, from which the judge is made after a documented run, and the summary.
///
/// The Java suite runs the three Java frameworks the same way against an instance whose
/// catalog is the catalog's database, with the text itself over JDBC as its source.
/// </summary>
internal sealed class CatalogRun
{
    public static readonly ORMEnum[] Frameworks = [ORMEnum.Dapper, ORMEnum.EFCore, ORMEnum.NHibernate];

    private static readonly ResultRow.RenderSettings Settings = new();

    private static readonly Dictionary<string, Lazy<CatalogRun>> Runs = [];

    /// <summary>
    /// How many rows of a result the judge keeps as rows; a larger result is kept as its digest
    /// alone - the count and the SHA-256 of the canonical rows -, because some queries return
    /// over 100 000 rows and the judge is a file of the repository.
    /// </summary>
    public const int RowsKeptByTheJudge = 1000;

    private readonly Dictionary<(string Key, ORMEnum Framework), CatalogCell> cells = [];
    private readonly Dictionary<string, (string Digest, List<string> Rows)> judge = new(StringComparer.OrdinalIgnoreCase);

    private CatalogRun(CatalogDefinition definition, string? unavailable)
    {
        Definition = definition;
        Unavailable = unavailable;
    }

    /// <summary>The run of this test run over the catalog, made by the first test that asks for it.</summary>
    public static CatalogRun Of(CatalogDefinition definition)
    {
        Lazy<CatalogRun> run;
        lock (Runs)
        {
            if (!Runs.TryGetValue(definition.Name, out run!))
            {
                Runs[definition.Name] = run = new Lazy<CatalogRun>(() => Run(definition), LazyThreadSafetyMode.ExecutionAndPublication);
            }
        }

        return run.Value;
    }

    public CatalogDefinition Definition { get; }

    public CatalogDatabase Database => Definition.Database;

    /// <summary>Why the run could not happen, or null when it did.</summary>
    public string? Unavailable { get; }

    /// <summary>The data set the database holds, as the loader named it in the catalog's data set property.</summary>
    public string? DataSet { get; private set; }

    /// <summary>The settings of the server the run read before it started (decision 164).</summary>
    public CatalogServer? Server { get; private set; }

    /// <summary>Why the run leaves its measurement out, or null when it measures (decisions 145 and 164).</summary>
    public string? MeasurementLeftOut => Server is null ? "left out" : CatalogServer.MeasurementLeftOut(Database.Measures, Server.Maxdop, Server.Processors);

    /// <summary>Whether the database holds a judge; a run over a database without one can make it.</summary>
    public bool HasJudge { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    public TimeSpan CatalogRead { get; private set; }

    public IReadOnlyList<CatalogRunQuery> Queries { get; private set; } = [];

    public CatalogRunQuery Query(string key) => Definition.Queries.Single(query => query.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public CatalogCell Cell(string key, ORMEnum framework) => cells[(key, framework)];

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
            Database.Unavailable(Unavailable, Database.IsRequired);
        }
    }

    /// <summary>Skips a query the run was limited away from.</summary>
    public void SkipIfNotCovered(string key)
    {
        if (!Covers(key))
        {
            Assert.Skip($"The run was limited to {string.Join(", ", Database.Keys?.Order(StringComparer.Ordinal) ?? Enumerable.Empty<string>())} and did not cover {key}.");
        }
    }

    private static CatalogRun Run(CatalogDefinition definition)
    {
        var database = definition.Database;
        if (!database.IsConfigured)
        {
            return new CatalogRun(definition, database.NotConfiguredReason);
        }

        var connectionString = database.ConnectionString!;
        SqlConnection control;
        string? dataSet;
        CatalogServer server;

        try
        {
            control = new SqlConnection(connectionString);
            control.Open();
            dataSet = Scalar<string>(control, $"SELECT CAST(value AS NVARCHAR(200)) FROM sys.extended_properties WHERE class = 0 AND name = N'{definition.DataSetProperty}'");
            server = CatalogServer.Read(control);
        }
        catch (Exception exception)
        {
            return new CatalogRun(definition, $"The {definition.Name} database \"{database.ConnectionStringName}\" is configured but cannot be reached: {exception.Message}");
        }

        using (control)
        {
            var run = new CatalogRun(definition, null) { DataSet = dataSet, Server = server };
            run.Execute(control, connectionString);
            return run;
        }
    }

    private void Execute(SqlConnection control, string connectionString)
    {
        var clock = Stopwatch.StartNew();
        ReadJudge(control);

        Queries = [.. Definition.Queries.Where(query => Database.Keys is null || Database.Keys.Contains(query.Key))];

        using var catalog = new CachingCatalogReader(new SqlServerCatalogReader(connectionString));

        foreach (var query in Queries)
        {
            var prepared = new Dictionary<ORMEnum, PreparedQuery>();

            try
            {
                foreach (var framework in Frameworks)
                {
                    var cell = cells[(query.Key, framework)] = new CatalogCell { Key = query.Key, Framework = framework };

                    var started = Stopwatch.GetTimestamp();
                    ConversionResult conversion;
                    try
                    {
                        conversion = ConversionHandler.Convert(ORMEnum.Dapper, framework, query.Units(), catalog);
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
                    var losses = conversion.Records.Where(record => record.Kind == ConversionRecordKind.Loss).Select(record => record.Reason).Distinct().ToList();
                    cell.Lost = losses.Count > 0 ? string.Join("; ", losses) : null;

                    started = Stopwatch.GetTimestamp();
                    try
                    {
                        prepared[framework] = PreparedQuery.Prepare($"{Definition.Name}_{query.Key}_{framework}", framework, conversion, connectionString);
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
                        var arguments = ArgumentsFor(prepared[framework].Parameters, query);
                        var rows = prepared[framework].Run(query.Key, arguments, query.Columns, projection: true);
                        cell.Selected = Stopwatch.GetElapsedTime(started);
                        cell.Rows = Canonical(query, rows);
                    }
                    catch (Exception exception)
                    {
                        cell.Selected = Stopwatch.GetElapsedTime(started);
                        cell.Failed = Innermost(exception).Message + (cell.Lost is null ? string.Empty : $" (the translation lost: {cell.Lost})");
                    }
                }

                if (MeasurementLeftOut is null)
                {
                    foreach (var (framework, artifact) in prepared)
                    {
                        var cell = cells[(query.Key, framework)];
                        if (cell.Failed is not null)
                        {
                            continue;
                        }

                        var arguments = ArgumentsFor(artifact.Parameters, query);
                        Measure(cell, () => artifact.Run(query.Key, arguments, query.Columns, projection: true));
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
    /// The arguments of a read, in the order of the parameters of the generated method: the
    /// value the catalog states for each name, converted to the type the method declares. A
    /// parameter the catalog values under no name fails the cell with its name.
    /// </summary>
    internal static object?[] ArgumentsFor(IReadOnlyList<ParameterInfo> parameters, CatalogRunQuery query)
        => [.. parameters.Select(parameter =>
        {
            var argument = query.Arguments.FirstOrDefault(candidate => SameName(candidate.Name, parameter.Name ?? string.Empty));
            if (argument.Name is null)
            {
                throw new InvalidOperationException($"{query.Key}: the generated method takes the parameter {parameter.Name}, which the catalog does not value.");
            }

            return Argument(parameter.ParameterType, argument.Value);
        })];

    /// <summary>Two names are the same when they read alike without case and underscores: the catalog's UserId, p_UserId and the method's userId, pUserId.</summary>
    private static bool SameName(string first, string second)
        => first.Replace("_", string.Empty).Equals(second.Replace("_", string.Empty), StringComparison.OrdinalIgnoreCase);

    internal static object? Argument(Type type, string text)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target == typeof(string)) return text;
        if (target == typeof(int)) return int.Parse(text, CultureInfo.InvariantCulture);
        if (target == typeof(long)) return long.Parse(text, CultureInfo.InvariantCulture);
        if (target == typeof(short)) return short.Parse(text, CultureInfo.InvariantCulture);
        if (target == typeof(byte)) return byte.Parse(text, CultureInfo.InvariantCulture);
        if (target == typeof(double)) return double.Parse(text, CultureInfo.InvariantCulture);
        if (target == typeof(float)) return float.Parse(text, CultureInfo.InvariantCulture);
        if (target == typeof(decimal)) return decimal.Parse(text, CultureInfo.InvariantCulture);
        if (target == typeof(bool)) return text is "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase);
        if (target == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture);
        if (target == typeof(DateOnly)) return DateOnly.Parse(text, CultureInfo.InvariantCulture);
        if (target == typeof(Guid)) return Guid.Parse(text);
        throw new NotSupportedException($"A parameter of type {type.Name} has no conversion from the catalog's text.");
    }

    /// <summary>
    /// The policy of decision 165 over one cell: the read that produced the cell's rows was its
    /// first, cold execution and is its warm-up, and the policy's further warm-ups, if any, follow
    /// it; then executions until the accumulated time reaches the target and at least the
    /// minimum; the median of one execution is the number.
    /// </summary>
    private static void Measure(CatalogCell cell, Action execute)
    {
        try
        {
            for (var warmup = 1; warmup < BenchmarkPolicy.WarmupIterations; warmup++)
            {
                execute();
            }

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
            cell.WarmupIterations = BenchmarkPolicy.WarmupIterations;
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

    /// <summary>The rows in the canonical form of decision 170: in the order of the query's key, the rows of one key value as a set.</summary>
    private static List<string> Canonical(CatalogRunQuery query, List<object?[]> rows)
        => ResultRow.Canonical(rows, Settings, query.Columns, query.OrderingKey);

    private void ReadJudge(SqlConnection control)
    {
        if (JudgeTable.Read(control) is not { } entries)
        {
            return;
        }

        foreach (var (key, entry) in entries)
        {
            judge[key] = entry;
        }

        HasJudge = true;
    }

    /// <summary>The lines of the summary: the run as a whole, then per framework, then per query.</summary>
    public IEnumerable<string> Describe()
    {
        yield return string.Create(
            CultureInfo.InvariantCulture,
            $"{Definition.Name} run over {DataSet ?? "an unnamed data set"}: {Queries.Count} queries in {Frameworks.Length} frameworks in {Elapsed.TotalMinutes:F1} min; "
            + $"first catalog read {CatalogRead.TotalMilliseconds:F0} ms; judge {(HasJudge ? "present" : "absent - this run can make one")}; "
            + $"server {Server?.Describe() ?? "not read"}; measurement {MeasurementLeftOut ?? "by the policy of decisions 145 and 165"}.");

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
            yield return $"{query.Key} ({query.Group}, {(source.Ran ? source.Rows!.Count + " rows" : "no rows")}{judged}): {string.Join("; ", parts)}";
        }
    }

    public bool AgreesWithSource(CatalogCell cell)
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
            return $"the judge holds no entry for it - the run it was made from did not run it, and a new judge is a decision ({Database.Decision})";
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
        if (Database.ResultsDirectory is not { } directory)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

        var cellsFile = new StringBuilder();
        cellsFile.AppendLine("key\tgroup\tframework\toutcome\tfellBack\ttranslateMs\tcompileMs\tfirstReadMs\trows\tagreesWithSource\tagreesWithJudge\tmedianMs\twarmups\titerations\tdetail");
        foreach (var query in Queries)
        {
            foreach (var framework in Frameworks)
            {
                var cell = cells[(query.Key, framework)];
                var detail = cell.Refused ?? cell.Unprepared ?? cell.Failed ?? cell.MeasurementFailed ?? cell.FallbackFeatures ?? string.Empty;
                cellsFile.AppendLine(string.Join('\t',
                    query.Key, query.Group, framework, cell.Outcome, cell.FellBack ? "1" : "0",
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

        // The source rows in the form of judge.tsv: what the judge is made from once a run is
        // documented. Only the source's rows, and only where the source ran.
        var judgeFile = new StringBuilder();
        judgeFile.Append($"# The judge of the {Definition.Name} catalog (decision {Database.Decision}): the rows of every query in the canonical form of decisions 089 and 170,\n");
        judgeFile.Append($"# as the catalog's own text returned them over {DataSet} through Dapper on {stamp}.\n");
        judgeFile.Append($"# key<TAB>position<TAB>rendered row; position 0 is the digest of the whole result (row count and SHA-256 of the rows joined by LF),\n");
        judgeFile.Append($"# the rows follow it where the result has at most {RowsKeptByTheJudge} of them. A query without a line was not run. Made by the .NET suite's {Definition.Name} run.\n");
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
