using System.Diagnostics;
using System.Reflection;
using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using Microsoft.Data.SqlClient;
using Model;
using OrmConvertor;
using SampleData;
using Tests.Differential;

namespace Tests.LdbcJudge;

/// <summary>What one artifact did over the reads of its operation (decision 117).</summary>
internal sealed class LdbcTally
{
    /// <summary>How many disagreements a failure message lists; the rest is counted, not printed.</summary>
    private const int Listed = 3;

    private readonly List<string> disagreements = [];

    public int Reads { get; private set; }

    public int Matches { get; private set; }

    public int Failures { get; private set; }

    /// <summary>Why the artifact could not be made at all - a refusal, a compilation - or null when it could.</summary>
    public string? Unprepared { get; set; }

    public IReadOnlyList<string> Disagreements => disagreements;

    public void Match() => (Reads, Matches) = (Reads + 1, Matches + 1);

    public void Mismatch(int position, IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        Reads++;
        if (disagreements.Count < Listed)
        {
            disagreements.Add(
                $"line {position}: the judge expects {expected.Count} rows, the artifact returned {actual.Count}"
                + Environment.NewLine + Difference(expected, actual));
        }
    }

    public void Fail(int position, Exception exception)
    {
        Reads++;
        Failures++;
        if (disagreements.Count < Listed)
        {
            disagreements.Add($"line {position}: {Innermost(exception).GetType().Name}: {Innermost(exception).Message}");
        }
    }

    /// <summary>A failure message that says what happened, with the first disagreements.</summary>
    public string Describe(string cell)
        => Unprepared is not null
            ? $"{cell}: the artifact could not be made: {Unprepared}"
            : $"{cell}: {Matches} of {Reads} reads agree with the judge, {Failures} failed to run."
              + string.Concat(disagreements.Select(line => Environment.NewLine + "  " + line));

    internal static Exception Innermost(Exception exception)
        => exception is TargetInvocationException { InnerException: { } inner } ? Innermost(inner) : exception;

    /// <summary>The first row where the two differ, both ways, so a reader sees the field without diffing by eye.</summary>
    private static string Difference(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var index = 0;
        while (index < expected.Count && index < actual.Count && expected[index] == actual[index])
        {
            index++;
        }

        return $"    row {index + 1}:"
            + Environment.NewLine + "      expected " + (index < expected.Count ? expected[index] : "(no row)")
            + Environment.NewLine + "      actual   " + (index < actual.Count ? actual[index] : "(no row)");
    }
}

/// <summary>Whether a deliberately wrong artifact was caught by the judge (the negative half, decision 117).</summary>
internal sealed class LdbcMutationTally
{
    public bool Changed { get; init; }

    /// <summary>How it was caught - another result on a line, or a failure to compile or run -, or null while it was not.</summary>
    public string? Detection { get; set; }

    public int Reads { get; set; }
}

/// <summary>
/// The replay of the validation set of LDBC Interactive v1 over LdbcSnb (decision 117): the
/// fourth verification level over the LDBC catalog, with LDBC's own expected results as the
/// judge. The lines are replayed in their order: an insert runs its script from
/// <c>database/ldbc/updates</c> and stays committed, so that every artifact sees it from its
/// own connection; a read runs the generated artifact of every framework this suite owns -
/// Dapper from the catalog's own text, which judges the text, then EF Core and NHibernate,
/// which judge the translation - and compares the rows with the expected result. Before the
/// replay and after it the compensation returns the database to its loaded state, and an
/// application lock keeps a replay of the other suite from changing the state meanwhile.
///
/// Every artifact is translated with LdbcSnb as the catalog - the database it runs on -, and
/// compiled once; the replay then runs it for every read of its operation. The replay happens
/// once per test run, when the first test of <see cref="LdbcValidationTest"/> asks for it.
/// </summary>
internal sealed class LdbcReplay
{
    /// <summary>The query whose artifact carries the mutations of decision 089: it has a filter, a comparison, an ordering, a row count and two adjacent projected fields.</summary>
    public const string MutatedQuery = "ic2";

    /// <summary>The frameworks whose artifacts this suite runs; Dapper is the source, translated into itself.</summary>
    public static readonly ORMEnum[] Frameworks = [ORMEnum.Dapper, ORMEnum.EFCore, ORMEnum.NHibernate];

    private const string LockResource = "ldbc.validation";
    private const int PageSize = 500;

    private static readonly Lazy<LdbcReplay> Instance = new(Replay, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly Dictionary<(string Key, ORMEnum Framework), LdbcTally> tallies = [];
    private readonly Dictionary<(string Mutation, ORMEnum Framework), LdbcMutationTally> mutations = [];

    private LdbcReplay(string? unavailable) => Unavailable = unavailable;

    public static LdbcReplay Current => Instance.Value;

    /// <summary>Why the judge is not there, or null when it is.</summary>
    public string? Unavailable { get; }

    /// <summary>The set the database holds, as the loader named it in the extended property ldbc.validation.</summary>
    public string? SetName { get; private set; }

    public int Lines { get; private set; }

    public int Inserts { get; private set; }

    public TimeSpan Elapsed { get; private set; }

    /// <summary>The rows of every LDBC table after the compensation before the replay - the loaded state.</summary>
    public IReadOnlyDictionary<string, long> Loaded { get; private set; } = new Dictionary<string, long>();

    /// <summary>The rows of every LDBC table after the last line, before the compensation.</summary>
    public IReadOnlyDictionary<string, long> Replayed { get; private set; } = new Dictionary<string, long>();

    /// <summary>The rows of every LDBC table after the compensation that ends the replay.</summary>
    public IReadOnlyDictionary<string, long> Restored { get; private set; } = new Dictionary<string, long>();

    public LdbcTally Tally(string key, ORMEnum framework) => tallies[(key, framework)];

    public LdbcMutationTally Mutation(string mutation, ORMEnum framework) => mutations[(mutation, framework)];

    /// <summary>Skips the test, or fails it where the environment promised the judge (<see cref="LdbcDatabase"/>).</summary>
    public void SkipIfUnavailable()
    {
        if (Unavailable is not null)
        {
            LdbcDatabase.Unavailable(Unavailable, LdbcDatabase.IsRequired);
        }
    }

    /// <summary>The queries the set judges: every query of the Interactive workload, each bound to an operation.</summary>
    public static IEnumerable<LdbcQuery> Judged() => LdbcSnbSample.Queries.Where(query => query.Validation is not null);

    private static LdbcReplay Replay()
    {
        if (!LdbcDatabase.IsConfigured)
        {
            return new LdbcReplay(LdbcDatabase.NotConfiguredReason);
        }

        var connectionString = LdbcDatabase.ConnectionString!;
        SqlConnection control;
        string? setName;

        try
        {
            control = new SqlConnection(connectionString);
            control.Open();
            setName = Scalar<string>(control, "SELECT CAST(value AS NVARCHAR(200)) FROM sys.extended_properties WHERE class = 0 AND name = N'ldbc.validation'");
        }
        catch (Exception exception)
        {
            return new LdbcReplay(
                $"The LDBC database \"{LdbcDatabase.ConnectionStringName}\" is configured but cannot be reached: {exception.Message}");
        }

        using (control)
        {
            if (setName is null)
            {
                return new LdbcReplay(
                    "The LDBC database holds no validation set: the extended property ldbc.validation is missing. "
                    + "database/ldbc/load-ldbc.sh loads it on the start of the container; elsewhere run database/ldbc/validation.sql (decision 117).");
            }

            var replay = new LdbcReplay(null) { SetName = setName };
            replay.Run(control, connectionString);
            return replay;
        }
    }

    private void Run(SqlConnection control, string connectionString)
    {
        var clock = Stopwatch.StartNew();
        using var catalog = new CachingCatalogReader(new SqlServerCatalogReader(connectionString));

        var prepared = Prepare(catalog, connectionString);
        var mutated = PrepareMutations(catalog, connectionString);

        try
        {
            // Held for the lifetime of this connection: a replay of the other suite waits, and a
            // replay that dies releases the lock with its connection.
            Execute(
                control,
                $"DECLARE @granted INT; EXEC @granted = sp_getapplock @Resource = N'{LockResource}', @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = -1; "
                + $"IF @granted < 0 THROW 50117, 'The application lock {LockResource} was not granted.', 1;",
                timeout: 0);

            LdbcUpdates.Undo(control);
            Loaded = Counts(control);

            try
            {
                Replay(control, prepared, mutated);
                Replayed = Counts(control);
            }
            finally
            {
                LdbcUpdates.Undo(control);
            }

            Restored = Counts(control);
        }
        finally
        {
            foreach (var query in prepared.Values.SelectMany(cells => cells.Values).Concat(mutated.Values))
            {
                query?.Dispose();
            }
        }

        Elapsed = clock.Elapsed;
    }

    /// <summary>Every judged query translated into every framework of this suite and compiled; a cell that cannot be made says why.</summary>
    private Dictionary<string, Dictionary<ORMEnum, PreparedQuery?>> Prepare(ICatalogReader catalog, string connectionString)
    {
        var prepared = new Dictionary<string, Dictionary<ORMEnum, PreparedQuery?>>();

        foreach (var query in Judged())
        {
            var cells = prepared[query.Validation!.Operation] = [];

            foreach (var framework in Frameworks)
            {
                var tally = tallies[(query.Key, framework)] = new LdbcTally();

                try
                {
                    var conversion = Translate(query, framework, catalog);
                    cells[framework] = PreparedQuery.Prepare($"Ldbc_{query.Key}_{framework}", framework, conversion, connectionString);
                }
                catch (Exception exception)
                {
                    tally.Unprepared = LdbcTally.Innermost(exception).Message;
                    cells[framework] = null;
                }
            }
        }

        return prepared;
    }

    /// <summary>
    /// The mutations of decision 089, applied to the artifact of one query in every framework.
    /// A mutation that changes nothing is recorded as such; one that leaves an artifact that
    /// cannot even be compiled is caught already, the loud way.
    /// </summary>
    private Dictionary<(string Mutation, ORMEnum Framework), PreparedQuery?> PrepareMutations(ICatalogReader catalog, string connectionString)
    {
        var query = LdbcSnbSample.Queries.Single(candidate => candidate.Key == MutatedQuery);
        var prepared = new Dictionary<(string Mutation, ORMEnum Framework), PreparedQuery?>();

        foreach (var framework in Frameworks)
        {
            var conversion = Translate(query, framework, catalog);
            var artifact = DotNetQueryRunner.QueryMethod(conversion);

            foreach (var mutation in DifferentialMutation.All())
            {
                var text = mutation.Apply(artifact);
                var tally = mutations[(mutation.Key, framework)] = new LdbcMutationTally { Changed = text != artifact };
                prepared[(mutation.Key, framework)] = null;

                if (!tally.Changed)
                {
                    continue;
                }

                try
                {
                    prepared[(mutation.Key, framework)] = PreparedQuery.Prepare(
                        $"Ldbc_{query.Key}_{framework}_{mutation.Key}", framework, conversion, connectionString, text);
                }
                catch (Exception exception)
                {
                    tally.Detection = $"the mutated artifact cannot be made: {LdbcTally.Innermost(exception).Message}";
                }
            }
        }

        return prepared;
    }

    private void Replay(
        SqlConnection control,
        Dictionary<string, Dictionary<ORMEnum, PreparedQuery?>> prepared,
        Dictionary<(string Mutation, ORMEnum Framework), PreparedQuery?> mutated)
    {
        var queries = Judged().ToDictionary(query => query.Validation!.Operation);
        var last = LdbcDatabase.Rows ?? int.MaxValue;
        var after = 0;

        while (true)
        {
            var page = Page(control, after, last);
            if (page.Count == 0)
            {
                return;
            }

            foreach (var (position, operation, parameters, result) in page)
            {
                after = position;
                Lines++;

                if (operation.StartsWith("INS", StringComparison.Ordinal))
                {
                    LdbcUpdates.Insert(control, operation, parameters);
                    Inserts++;
                    continue;
                }

                var query = queries[operation];
                var expected = LdbcCanonicalForm.Expected(query.Validation!, result);

                foreach (var (framework, artifact) in prepared[operation])
                {
                    if (artifact is not null)
                    {
                        Judge(tallies[(query.Key, framework)], query, artifact, position, parameters, expected);
                    }
                }

                if (query.Key == MutatedQuery)
                {
                    Catch(mutated, query, position, parameters, expected);
                }
            }
        }
    }

    private static void Judge(LdbcTally tally, LdbcQuery query, PreparedQuery artifact, int position, string parameters, List<string> expected)
    {
        try
        {
            var actual = Run(query, artifact, parameters);

            if (actual.SequenceEqual(expected))
            {
                tally.Match();
            }
            else
            {
                tally.Mismatch(position, expected, actual);
            }
        }
        catch (Exception exception)
        {
            tally.Fail(position, exception);
        }
    }

    /// <summary>Runs every mutation not caught yet on this read; the first read that tells it apart catches it.</summary>
    private void Catch(
        Dictionary<(string Mutation, ORMEnum Framework), PreparedQuery?> mutated,
        LdbcQuery query,
        int position,
        string parameters,
        List<string> expected)
    {
        foreach (var ((mutation, framework), artifact) in mutated)
        {
            var tally = mutations[(mutation, framework)];
            if (artifact is null || tally.Detection is not null)
            {
                continue;
            }

            tally.Reads++;

            try
            {
                if (!Run(query, artifact, parameters).SequenceEqual(expected))
                {
                    tally.Detection = $"line {position} returned other rows than the judge expects";
                }
            }
            catch (Exception exception)
            {
                tally.Detection = $"line {position} failed to run: {LdbcTally.Innermost(exception).Message}";
            }
        }
    }

    private static List<string> Run(LdbcQuery query, PreparedQuery artifact, string parameters)
    {
        var binding = query.Validation!;
        var arguments = LdbcCanonicalForm.Arguments(query, artifact.Parameters.Select(parameter => parameter.Name!), parameters);

        // Every query of the workload is a projection: HQL hands its rows back by position.
        var rows = artifact.Run(query.Key, arguments, [.. binding.Fields.Select(field => field.Column)], projection: true);

        return LdbcCanonicalForm.Actual(binding, rows);
    }

    /// <summary>
    /// The artifact of one framework, from Dapper with LdbcSnb as the catalog: the completion
    /// writes the schema of the tables into the mapping, and an artifact completed over another
    /// database would not find them here. A Failure record makes the cell unpreparable, with
    /// the reasons, rather than a cell that silently reads nothing.
    /// </summary>
    private static ConversionResult Translate(LdbcQuery query, ORMEnum framework, ICatalogReader catalog)
    {
        var conversion = ConversionHandler.Convert(ORMEnum.Dapper, framework, Units(query), catalog);
        var failures = conversion.Records.Where(record => record.Kind == ConversionRecordKind.Failure).ToList();

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"{query.Key} into {framework} was refused: " + string.Join("; ", failures.Select(failure => failure.Reason)));
        }

        return conversion;
    }

    private static List<ConversionSource> Units(LdbcQuery query) =>
    [
        .. LdbcSnbSample.Entities.Select(entity => new ConversionSource
        {
            Name = entity.FileName,
            ContentType = ConversionContentType.CSharp,
            Content = entity.Content,
        }),
        new ConversionSource { Name = $"{query.Key}.sql", ContentType = ConversionContentType.SqlQuery, Content = query.Sql! },
    ];

    private static List<(int Position, string Operation, string Parameters, string Result)> Page(SqlConnection control, int after, int last)
    {
        using var command = control.CreateCommand();
        command.CommandText =
            $"SELECT TOP ({PageSize}) [Position], [Operation], [Parameters], [Result] FROM [{LdbcDatabase.Schema}].[ValidationOperation] "
            + "WHERE [Position] > @after AND [Position] <= @last ORDER BY [Position];";
        command.Parameters.AddWithValue("@after", after);
        command.Parameters.AddWithValue("@last", last);

        using var reader = command.ExecuteReader();
        var page = new List<(int, string, string, string)>(PageSize);
        while (reader.Read())
        {
            page.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return page;
    }

    /// <summary>The rows of every LDBC table, by the names of the catalog's entities - which are the tables of schema.sql.</summary>
    private static Dictionary<string, long> Counts(SqlConnection control)
    {
        var counts = new Dictionary<string, long>();
        foreach (var table in LdbcSnbSample.Entities.Select(entity => Path.GetFileNameWithoutExtension(entity.FileName)))
        {
            counts[table] = Scalar<long>(control, $"SELECT COUNT_BIG(*) FROM [{LdbcDatabase.Schema}].[{table}];");
        }

        return counts;
    }

    private static T? Scalar<T>(SqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 600;
        var value = command.ExecuteScalar();
        return value is null or DBNull ? default : (T)value;
    }

    private static void Execute(SqlConnection connection, string sql, int timeout)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = timeout;
        command.ExecuteNonQuery();
    }
}
