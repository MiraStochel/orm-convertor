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

/// <summary>The measurement of one execution by the policy of decisions 145 and 165, which the source and every cell carry.</summary>
internal abstract class CatalogMeasured
{
    /// <summary>The median of one execution by the policy of decision 145, or null where it was not measured.</summary>
    public double? MedianMilliseconds { get; set; }

    public int WarmupIterations { get; set; }

    public int Iterations { get; set; }

    public string? MeasurementFailed { get; set; }
}

/// <summary>
/// What the catalog's own text did over the database, run as it stands with the catalog's
/// values declared in front of it (decision 209): the rows every target is compared with and
/// the judge is made from. Run only for a query the catalog claims as translated.
/// </summary>
internal sealed class CatalogSource : CatalogMeasured
{
    public required string Key { get; init; }

    /// <summary>How many rows the text returned; null where it did not run.</summary>
    public int? RowCount { get; set; }

    /// <summary>Why the text did not return rows, or null where it did or was not run.</summary>
    public string? Failed { get; set; }

    /// <summary>The time of the one read that produced the rows.</summary>
    public TimeSpan Selected { get; set; }

    /// <summary>The digest of the rows (<see cref="CatalogRun.Digest"/>), kept once the rows are let go.</summary>
    public string? Digest { get; set; }

    /// <summary>The verdict of the judge over the rows; null without a judge or without rows.</summary>
    public bool? AgreesWithJudge { get; set; }

    public string? JudgeDifference { get; set; }

    public bool Ran => RowCount is not null;

    public string Describe()
        => Ran ? $"{Key}: the text returned {RowCount} rows"
            : Failed is not null ? $"{Key}: the text failed over ADO.NET: {Failed}"
            : $"{Key}: the text was not run, because the catalog does not claim the query as translated";
}

/// <summary>What one query did in one framework over the stages of the run (decisions 149 and 208).</summary>
internal sealed class CatalogCell : CatalogMeasured
{
    public required string Key { get; init; }

    public required ORMEnum Framework { get; init; }

    /// <summary>The reasons the target refused the query, or null where it translated it.</summary>
    public string? Refused { get; set; }

    /// <summary>Whether the target wrote the query in native SQL (the third value of decision 113).</summary>
    public bool FellBack { get; set; }

    /// <summary>The features the target could not say in its own language, where it fell back.</summary>
    public string? FallbackFeatures { get; set; }

    /// <summary>What the query lost with a Loss record of the query branch (decisions 070 and 022): a projection outside the vocabulary, an ordering it does not carry. The artifact is poorer by it, and a read that misses a column says so.</summary>
    public string? Lost { get; set; }

    /// <summary>Why the artifact could not be compiled or its factory built, or null when it could.</summary>
    public string? Unprepared { get; set; }

    /// <summary>Why the artifact did not return rows, or null when it did.</summary>
    public string? Failed { get; set; }

    /// <summary>
    /// How many rows the artifact returned; null until it ran. The rows themselves, in the
    /// canonical form of decision 089, are compared with the source's as soon as they are read
    /// and then let go: a catalog of a thousand and more queries returning a hundred thousand
    /// rows does not fit a run that keeps them, so the run keeps the verdicts.
    /// </summary>
    public int? RowCount { get; set; }

    /// <summary>Whether the rows were the text's, row for row; null where the cell did not run or the text did not.</summary>
    public bool? AgreesWithSource { get; set; }

    /// <summary>The first row where the rows differed from the source's (<see cref="CatalogRun.Difference"/>), where they did.</summary>
    public string? SourceDifference { get; set; }

    public TimeSpan Translated { get; set; }

    public TimeSpan Compiled { get; set; }

    /// <summary>The time of the one read that produced the rows.</summary>
    public TimeSpan Selected { get; set; }

    /// <summary>Where the query landed in the target: its own language, native SQL, a refusal, or a failure after the translation.</summary>
    public string Outcome => Refused is not null ? "refused" : Unprepared is not null ? "not compiled" : Failed is not null ? "failed" : FellBack ? "native SQL" : "translated";

    public bool Ran => RowCount is not null;

    public string Describe()
    {
        var cell = $"{Key} in {Framework}";
        if (Refused is not null) return $"{cell}: refused: {Refused}";
        if (Unprepared is not null) return $"{cell}: the artifact could not be made: {Unprepared}";
        if (Failed is not null) return $"{cell}: the artifact failed to run: {Failed}";
        return $"{cell}: {RowCount} rows{(FellBack ? " (native SQL)" : string.Empty)}";
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
/// entities of the tables it reads and the text as one more unit -, the text itself, which the
/// run executes as the source (decision 209), the values of its parameters by name, and
/// whether the catalog claims it translated at all.
/// </summary>
internal sealed record CatalogRunQuery(
    string Key,
    string Group,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> OrderingKey,
    Func<List<ConversionSource>> Units,
    string Sql,
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
/// of the catalog translated from Dapper into the three .NET frameworks - Dapper among them -
/// with the catalog's database as the catalog and only the entities of the tables it reads,
/// the artifacts compiled, each run once with the catalog's values and its rows put into the
/// canonical form of decision 089, every target compared with the source - the catalog's own
/// text run over ADO.NET as it stands, with the values declared in front of it, as the Java
/// suite runs it over JDBC (decision 209) - and the source compared with the judge where the
/// database holds one, and finally the source and every cell measured by the policy of
/// decision 145. The run happens once per test run and catalog, for the first test that asks
/// for it, and writes its files into the directory the environment names: the cells, the
/// source rows in the form of <c>judge.tsv</c>, from which the judge is made after a
/// documented run, and the summary.
///
/// The rows of a query live only while the query is being run: every comparison is made as
/// soon as the rows are read, the cells and the judge lines of the query are written to the
/// files before the next query starts, and the summary alone is written at the end. The first
/// SEDE run kept every row of every cell until the end and ran out of memory while writing,
/// after eight hours and with nothing written.
///
/// The Java suite runs the three Java frameworks the same way against an instance whose
/// catalog is the catalog's database, with the same text over JDBC as its source.
/// </summary>
internal sealed class CatalogRun
{
    /// <summary>The targets of the run; Dapper is a target like the others, its source is the text (decision 209).</summary>
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
    private readonly Dictionary<string, CatalogSource> sources = new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>What the catalog's text did for a query (decision 209).</summary>
    public CatalogSource Source(string key) => sources[key];

    public bool Covers(string key) => sources.ContainsKey(key);

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
        using var results = ResultsWriter.Open(this);

        foreach (var query in Queries)
        {
            var prepared = new Dictionary<ORMEnum, PreparedQuery>();
            var source = sources[query.Key] = new CatalogSource { Key = query.Key };
            List<string>? sourceRows = null;

            try
            {
                // The source is the catalog's text as it stands (decision 209), run first, and
                // only where the catalog claims the query: an unclaimed text is not valid over
                // the schema, not a read, or fails over the data, and judges nothing.
                if (query.IsTranslated)
                {
                    var started = Stopwatch.GetTimestamp();
                    try
                    {
                        sourceRows = Canonical(query, Text(control, query));
                    }
                    catch (Exception exception)
                    {
                        source.Failed = Innermost(exception).Message;
                    }

                    source.Selected = Stopwatch.GetElapsedTime(started);
                    if (sourceRows is not null)
                    {
                        source.RowCount = sourceRows.Count;
                        Judge(source, sourceRows);
                        results?.WriteJudgeLines(query.Key, sourceRows);
                    }
                }

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
                    if (CatalogRead == TimeSpan.Zero)
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
                    // The losses of the query branch alone (decision 022): what a mapping of the
                    // target cannot state changes no column of a result.
                    var losses = conversion.Records.Where(record => record.Kind == ConversionRecordKind.Loss && record.Feature is not null).Select(record => record.Reason).Distinct().ToList();
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
                    List<string> canonical;
                    try
                    {
                        var arguments = ArgumentsFor(prepared[framework].Parameters, query);
                        var rows = prepared[framework].Run(query.Key, arguments, query.Columns, projection: true);
                        cell.Selected = Stopwatch.GetElapsedTime(started);
                        canonical = Canonical(query, rows);
                    }
                    catch (Exception exception)
                    {
                        cell.Selected = Stopwatch.GetElapsedTime(started);
                        cell.Failed = Innermost(exception).Message + (cell.Lost is null ? string.Empty : $" (the translation lost: {cell.Lost})");
                        continue;
                    }

                    // The comparison happens here, while the rows exist; the cell keeps the verdict.
                    cell.RowCount = canonical.Count;
                    if (sourceRows is not null)
                    {
                        cell.AgreesWithSource = canonical.SequenceEqual(sourceRows);
                        cell.SourceDifference = cell.AgreesWithSource == true ? null : Difference(sourceRows, canonical);
                    }
                }

                if (MeasurementLeftOut is null)
                {
                    if (sourceRows is not null)
                    {
                        Measure(source, () => Text(control, query));
                    }

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

                results?.WriteCells(query);
            }
        }

        Elapsed = clock.Elapsed;
        results?.WriteSummary();
    }

    /// <summary>
    /// The verdict of the judge over the source's rows, made while they exist (see
    /// <see cref="AgreesWithJudge"/> for what the values mean).
    /// </summary>
    private void Judge(CatalogSource source, List<string> rows)
    {
        var digest = source.Digest = Digest(rows);
        if (!HasJudge)
        {
            return;
        }

        if (JudgeDigest(source.Key) is not { } expected)
        {
            source.AgreesWithJudge = false;
            source.JudgeDifference = $"the judge holds no entry for it - the run it was made from did not run it, and a new judge is a decision ({Database.Decision})";
            return;
        }

        var kept = JudgeRows(source.Key) ?? [];
        if (digest == expected && (kept.Count == 0 || kept.SequenceEqual(rows)))
        {
            source.AgreesWithJudge = true;
            return;
        }

        source.AgreesWithJudge = false;
        source.JudgeDifference = kept.Count > 0 || rows.Count <= RowsKeptByTheJudge
            ? Difference(kept, rows)
            : $"the judge holds {expected}, the source returned {digest}";
    }

    /// <summary>
    /// The batch the source runs as (decision 209): the catalog's values declared in front of
    /// the text, one <c>DECLARE</c> each, in the order the catalog states them - the one way a
    /// text with <c>@name</c> variables runs unchanged, and the batch the Java suite sends over
    /// JDBC.
    /// </summary>
    internal static string TextBatch(CatalogRunQuery query)
    {
        var batch = new StringBuilder();
        foreach (var (name, sqlType, value) in query.Arguments)
        {
            batch.Append("DECLARE @").Append(name).Append(' ').Append(sqlType).Append(" = ").Append(Literal(sqlType, value)).Append(";\n");
        }

        return batch.Append(query.Sql).ToString();
    }

    /// <summary>A value of the catalog as a T-SQL literal of its type: a number as it is written, anything else a Unicode string.</summary>
    internal static string Literal(string sqlType, string value)
    {
        var parenthesis = sqlType.IndexOf('(');
        var type = (parenthesis > 0 ? sqlType[..parenthesis] : sqlType).Trim().ToUpperInvariant();
        return type is "INT" or "BIGINT" or "SMALLINT" or "TINYINT" or "FLOAT" or "REAL" or "DECIMAL" or "NUMERIC" or "BIT"
            ? value
            : "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    /// <summary>
    /// The catalog's own text over ADO.NET, the source of the comparison: its rows with the
    /// columns in the order the catalog states, a column the text names otherwise failing the
    /// source with both names.
    /// </summary>
    private static List<object?[]> Text(SqlConnection control, CatalogRunQuery query)
    {
        using var command = control.CreateCommand();
        command.CommandText = TextBatch(query);
        command.CommandTimeout = BenchmarkPolicy.ExecutionTimeoutSeconds;
        using var reader = command.ExecuteReader();
        if (reader.FieldCount != query.Columns.Count)
        {
            throw new InvalidOperationException($"{query.Key}: the text returns {reader.FieldCount} columns, the catalog states {query.Columns.Count}.");
        }

        for (var index = 0; index < reader.FieldCount; index++)
        {
            if (!reader.GetName(index).Equals(query.Columns[index], StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"{query.Key}: column {index + 1} is {reader.GetName(index)}, the catalog states {query.Columns[index]}.");
            }
        }

        var rows = new List<object?[]>();
        while (reader.Read())
        {
            var row = new object?[reader.FieldCount];
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[index] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            }

            rows.Add(row);
        }

        return rows;
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
    private static void Measure(CatalogMeasured cell, Action execute)
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

        var texts = sources.Values.ToList();
        var textsMeasured = texts.Where(source => source.MedianMilliseconds is not null).ToList();
        yield return FormattableString.Invariant(
            $"source (the text over ADO.NET): {texts.Count(source => source.Ran)} of {texts.Count(source => Query(source.Key).IsTranslated)} claimed queries ran, {texts.Sum(source => source.Selected.TotalSeconds):F1} s of first reads")
            + (textsMeasured.Count == 0
                ? "."
                : FormattableString.Invariant($"; measured {textsMeasured.Count}, median of medians {Median(textsMeasured.Select(source => source.MedianMilliseconds!.Value)):F1} ms, sum of medians {textsMeasured.Sum(source => source.MedianMilliseconds!.Value) / 1000:F1} s."));

        foreach (var framework in Frameworks)
        {
            var mine = cells.Values.Where(cell => cell.Framework == framework).ToList();
            var outcomes = mine.GroupBy(cell => cell.Outcome).OrderBy(group => group.Key).Select(group => $"{group.Count()} {group.Key}");
            var compared = mine.Count(cell => cell.AgreesWithSource is not null);
            var agreeing = mine.Count(AgreesWithSource);
            var measured = mine.Where(cell => cell.MedianMilliseconds is not null).ToList();

            var measurement = measured.Count == 0
                ? string.Empty
                : FormattableString.Invariant($"; measured {measured.Count} cells, median of medians {Median(measured.Select(cell => cell.MedianMilliseconds!.Value)):F1} ms, sum of medians {measured.Sum(cell => cell.MedianMilliseconds!.Value) / 1000:F1} s");

            var ran = mine.Count(cell => cell.Ran);
            var translation = mine.Sum(cell => cell.Translated.TotalSeconds);
            var compilation = mine.Sum(cell => cell.Compiled.TotalSeconds);
            var reads = mine.Sum(cell => cell.Selected.TotalSeconds);
            yield return FormattableString.Invariant(
                $"{framework}: {string.Join(", ", outcomes)}; {ran} runs, {agreeing} of the {compared} with a source agree with it; translation {translation:F1} s, compilation {compilation:F1} s, first reads {reads:F1} s{measurement}.");
        }

        foreach (var query in Queries)
        {
            var parts = Frameworks.Select(framework =>
            {
                var cell = cells[(query.Key, framework)];
                var median = cell.MedianMilliseconds is { } milliseconds ? string.Create(CultureInfo.InvariantCulture, $" {milliseconds:F1} ms") : string.Empty;
                var agreement = cell.AgreesWithSource switch { true => " =", false => " !=", null => string.Empty };
                return $"{framework} {cell.Outcome}{agreement}{median}";
            });

            var source = sources[query.Key];
            var text = source.Ran ? $"{source.RowCount} rows" : source.Failed is not null ? $"source failed: {source.Failed}" : "source not run";
            var textMedian = source.MedianMilliseconds is { } sourceMilliseconds ? string.Create(CultureInfo.InvariantCulture, $", text {sourceMilliseconds:F1} ms") : string.Empty;
            var judged = HasJudge ? (AgreesWithJudge(query.Key) switch { true => ", judge =", false => ", judge !=", null => ", judge -" }) : string.Empty;
            yield return $"{query.Key} ({query.Group}, {text}{textMedian}{judged}): {string.Join("; ", parts)}";
        }
    }

    /// <summary>Whether the cell's rows were the source's, compared when they were read; false where either did not run.</summary>
    public bool AgreesWithSource(CatalogCell cell) => cell.AgreesWithSource == true;

    /// <summary>
    /// Whether the source agreed with the judge - by the digest, which every entry has, and by
    /// the rows where the judge keeps them -; null where the source has no rows to compare or
    /// there is no judge. A query that returned no rows has an entry with the digest of none;
    /// a query the judge has no entry for was not run when the judge was made, so it agrees
    /// with nothing. Decided when the source's rows were read.
    /// </summary>
    public bool? AgreesWithJudge(string key) => sources.TryGetValue(key, out var source) ? source.AgreesWithJudge : null;

    /// <summary>What the source differed from the judge in, for a failure message.</summary>
    public string JudgeDifference(string key)
        => sources.TryGetValue(key, out var source) && source.Ran ? source.JudgeDifference ?? "the source agrees with the judge" : "the source did not run";

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

    /// <summary>
    /// The files of the run in the directory the environment names, written as the run goes:
    /// the cells of a query and the source rows of it in the form of <c>judge.tsv</c> - what
    /// the judge is made from once a run is documented - as soon as the query is done, flushed,
    /// so that a run that ends early leaves what it did; the summary at the end. Null where no
    /// directory is named.
    /// </summary>
    private sealed class ResultsWriter : IDisposable
    {
        private readonly CatalogRun run;
        private readonly string directory;
        private readonly StreamWriter cells;
        private readonly StreamWriter judge;

        private ResultsWriter(CatalogRun run, string directory)
        {
            this.run = run;
            this.directory = directory;
            var encoding = new UTF8Encoding(false);
            cells = new StreamWriter(Path.Combine(directory, "cells.tsv"), false, encoding) { NewLine = "\n" };
            cells.WriteLine("key\tgroup\tframework\toutcome\tfellBack\ttranslateMs\tcompileMs\tfirstReadMs\trows\tagreesWithSource\tagreesWithJudge\tmedianMs\twarmups\titerations\tdetail");
            cells.Flush();

            var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
            judge = new StreamWriter(Path.Combine(directory, "judge-candidate.tsv"), false, encoding) { NewLine = "\n" };
            judge.WriteLine($"# The judge of the {run.Definition.Name} catalog (decision {run.Database.Decision}): the rows of every query in the canonical form of decisions 089 and 170,");
            judge.WriteLine($"# as the catalog's own text returned them over {run.DataSet} through ADO.NET in the run started on {stamp} (decision 209).");
            judge.WriteLine("# key<TAB>position<TAB>rendered row; position 0 is the digest of the whole result (row count and SHA-256 of the rows joined by LF),");
            judge.WriteLine($"# the rows follow it where the result has at most {RowsKeptByTheJudge} of them. A query without a line was not run. Made by the .NET suite's {run.Definition.Name} run.");
            judge.Flush();
        }

        public static ResultsWriter? Open(CatalogRun run)
        {
            if (run.Database.ResultsDirectory is not { } directory)
            {
                return null;
            }

            Directory.CreateDirectory(directory);
            return new ResultsWriter(run, directory);
        }

        /// <summary>The source's rows of one query, while they exist.</summary>
        public void WriteJudgeLines(string key, List<string> rows)
        {
            judge.Write(key);
            judge.Write("\t0\t");
            judge.WriteLine(Digest(rows));
            if (rows.Count <= RowsKeptByTheJudge)
            {
                for (var position = 0; position < rows.Count; position++)
                {
                    judge.Write(key);
                    judge.Write('\t');
                    judge.Write((position + 1).ToString(CultureInfo.InvariantCulture));
                    judge.Write('\t');
                    judge.WriteLine(rows[position]);
                }
            }

            judge.Flush();
        }

        /// <summary>The source's line and the cells of one query, once it is done in every framework; the source's line as the Java suite writes it, with the framework <c>Text</c>.</summary>
        public void WriteCells(CatalogRunQuery query)
        {
            if (run.sources.TryGetValue(query.Key, out var source))
            {
                cells.WriteLine(string.Join('\t',
                    query.Key, query.Group, "Text", source.Ran ? "ran" : query.IsTranslated ? "failed" : "not run", "0",
                    string.Empty, string.Empty,
                    source.Selected.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                    source.RowCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    string.Empty,
                    source.AgreesWithJudge switch { true => "1", false => "0", null => string.Empty },
                    source.MedianMilliseconds?.ToString("F1", CultureInfo.InvariantCulture) ?? string.Empty,
                    source.MedianMilliseconds is null ? string.Empty : source.WarmupIterations.ToString(CultureInfo.InvariantCulture),
                    source.MedianMilliseconds is null ? string.Empty : source.Iterations.ToString(CultureInfo.InvariantCulture),
                    Clean(source.Failed ?? source.MeasurementFailed ?? string.Empty)));
            }

            foreach (var framework in Frameworks)
            {
                if (!run.cells.TryGetValue((query.Key, framework), out var cell))
                {
                    continue;
                }

                var detail = cell.Refused ?? cell.Unprepared ?? cell.Failed ?? cell.MeasurementFailed ?? cell.FallbackFeatures ?? string.Empty;
                cells.WriteLine(string.Join('\t',
                    query.Key, query.Group, framework, cell.Outcome, cell.FellBack ? "1" : "0",
                    cell.Translated.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                    cell.Compiled.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                    cell.Selected.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                    cell.RowCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    cell.AgreesWithSource switch { true => "1", false => "0", null => string.Empty },
                    string.Empty,
                    cell.MedianMilliseconds?.ToString("F1", CultureInfo.InvariantCulture) ?? string.Empty,
                    cell.MedianMilliseconds is null ? string.Empty : cell.WarmupIterations.ToString(CultureInfo.InvariantCulture),
                    cell.MedianMilliseconds is null ? string.Empty : cell.Iterations.ToString(CultureInfo.InvariantCulture),
                    Clean(detail)));
            }

            cells.Flush();
        }

        private static string Clean(string detail) => detail.Replace('\t', ' ').Replace("\r", string.Empty).Replace('\n', ' ');

        public void WriteSummary() => File.WriteAllLines(Path.Combine(directory, "summary.txt"), run.Describe());

        public void Dispose()
        {
            cells.Dispose();
            judge.Dispose();
        }
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
