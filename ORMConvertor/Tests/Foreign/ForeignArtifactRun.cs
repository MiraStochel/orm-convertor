using System.Collections;
using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Model;
using SampleData;
using Tests.Database;
using Tests.Differential;
using Tests.FibenJudge;
using Tests.Verification;

namespace Tests.Foreign;

/// <summary>
/// The levels of decision 016 over an artifact the tool did not make (decision 172), for
/// the three .NET targets. The first level is not the shape assertion the tool's artifact
/// gets - nobody stated what a foreign artifact should look like - but a reading by the
/// language of the target; from the second level on the artifact goes through the very
/// harness the tool's own artifact goes through (<see cref="PreparedQuery"/>), and the
/// fourth level compares with the same measure: the canonical result of the matrix query, or
/// the judge of the FIBEN catalog. An artifact that answers a query of the LDBC catalog ends
/// at the third level, its measure being a replay rather than one reading; a project of a case
/// study runs every query over the empty tables of its schema in the fixture (decision 176).
///
/// Nothing here asserts: a red level is a row, not a failure of the suite. What does throw
/// is the path itself - a key no measure answers, a target this suite does not own, a measure
/// whose database is not configured -, because that is a misconfiguration of the run and not
/// a verdict about the artifact.
/// </summary>
internal static class ForeignArtifactRun
{
    private static int sequence;

    public static bool Owns(ORMEnum target) => DotNetQueryRunner.Owns(target);

    /// <summary>
    /// Runs one artifact through the levels and returns its verdict. The callback sees the
    /// verdict after every level, so that a run stopped from outside leaves the level it
    /// reached behind.
    /// </summary>
    public static ForeignVerdict Run(ForeignArtifact artifact, Action<ForeignVerdict>? progress = null)
    {
        if (!Owns(artifact.Target))
        {
            throw new InvalidOperationException($"{artifact.Id}: {artifact.Target} is not a framework this suite runs; the artifact is the Java suite's.");
        }

        var measure = Measure.For(artifact);
        var verdict = new ForeignVerdict
        {
            Artifact = artifact.Id,
            Answers = artifact.Key.ToString(),
            Source = artifact.Source.ToString(),
            Target = artifact.Target.ToString(),
            Producer = artifact.Producer,
            Language = artifact.Language,
            Metadata = artifact.Metadata,
        };

        void Report()
        {
            progress?.Invoke(verdict);
        }

        // Level 1: the language of the target reads every unit.
        var clock = Stopwatch.StartNew();
        var unreadable = Unreadable(artifact);
        verdict.ReadMs = clock.Elapsed.TotalMilliseconds;
        if (unreadable is not null)
        {
            verdict.Outcome = ForeignVerdict.Unreadable;
            verdict.Detail = unreadable;
            Report();
            return verdict;
        }

        verdict.Reached = ForeignLevel.Read;
        Report();

        // Level 2: the units compile under the consumer references of the target. An artifact of a
        // whole project - several queries - compiles its entities once and every query method on
        // top of them, one by one, so that the detail names the query that does not compile.
        var entities = artifact.Entities.Select(unit => unit.Content).ToList();
        var mappings = artifact.Mappings.Select(unit => unit.Content).ToList();
        var method = artifact.QueryMethod;
        var assemblyName = $"Foreign_{Sanitize(artifact.Id)}_{Interlocked.Increment(ref sequence)}";

        clock.Restart();
        var compilation = method is null
            ? GeneratedEntityCompiler.Compile(assemblyName, entities, EntityReferences(artifact.Target))
            : PreparedQuery.Compile(assemblyName, artifact.Target, method.Content, entities, artifact.Form);

        string? uncompiled = compilation.Success ? null : string.Join("; ", compilation.Errors.Select(error => error.ToString()));

        // The compiled queries of a project, kept for the fourth level where the project has one.
        var projectQueries = new List<(ForeignUnit Query, byte[] Assembly)>();
        if (uncompiled is null && artifact.IsProject)
        {
            foreach (var (query, index) in artifact.QueryMethods.Select((query, index) => (query, index)))
            {
                var result = PreparedQuery.Compile($"{assemblyName}_q{index + 1}", artifact.Target, query.Content, entities, artifact.Form);
                if (!result.Success)
                {
                    uncompiled = $"{query.Path}: {string.Join("; ", result.Errors.Select(error => error.ToString()))}";
                    break;
                }

                projectQueries.Add((query, result.Assembly!));
            }
        }

        verdict.CompileMs = clock.Elapsed.TotalMilliseconds;

        if (uncompiled is not null)
        {
            verdict.Outcome = ForeignVerdict.NotCompiled;
            verdict.Detail = uncompiled;
            Report();
            return verdict;
        }

        verdict.Reached = ForeignLevel.Compiled;
        Report();

        // Level 3: the framework accepts the artifact. For a project: the model or the session
        // factory over the entities and mappings, and in NHibernate every HQL text against it.
        PreparedQuery? prepared = null;
        clock.Restart();
        try
        {
            if (method is null)
            {
                Accept(artifact.Target, compilation.Assembly!, mappings);
            }
            else
            {
                prepared = PreparedQuery.Open(artifact.Target, compilation.Assembly!, mappings, measure.ConnectionString, artifact.Form);
            }

            if (artifact.Target == ORMEnum.NHibernate)
            {
                NHibernateQueryAcceptance.CompileQueries(compilation.Assembly!, mappings, artifact.HqlTexts.Select(hql => (hql.Path, hql.Content)));
            }
        }
        catch (Exception exception)
        {
            verdict.AcceptMs = clock.Elapsed.TotalMilliseconds;
            verdict.Outcome = ForeignVerdict.NotAccepted;
            verdict.Detail = exception is InvalidOperationException { InnerException: not null } named ? named.Message : Innermost(exception).Message;
            prepared?.Dispose();
            Report();
            return verdict;
        }

        verdict.AcceptMs = clock.Elapsed.TotalMilliseconds;
        verdict.Reached = ForeignLevel.Accepted;
        Report();

        // Level 4 of a project whose tables stand in the fixture (decision 176): every query runs
        // over the empty tables of the project's schema. Runnable is what is measured; there is no
        // measure of equivalence, so the outcome is "ran" and agrees stays empty.
        if (prepared is null && artifact.IsProject && measure.RunsProject)
        {
            RunProject(artifact, projectQueries, mappings, measure.ConnectionString, verdict);
            Report();
            return verdict;
        }

        using (prepared)
        {
            // Level 4 of a point of a series (decision 182): the one query runs over the fixture's
            // data with a value of the declared type for every parameter, as a project's queries do.
            // Runnable is what is measured; no canonical result states its rows, so the outcome is
            // "ran" and agrees stays empty.
            if (prepared is not null && measure.Run is null && measure.RunsProject)
            {
                RunOne(prepared, method!, verdict);
                Report();
                return verdict;
            }

            if (prepared is null || measure.Run is null)
            {
                verdict.Outcome = ForeignVerdict.Accepted;
                verdict.Detail = prepared is null
                    ? artifact.IsProject
                        ? $"the artifact is a project of {artifact.QueryMethods.Count} queries, judged to the third level as one; " + (measure.NoFourthLevel ?? "a project of this key has no measure at the fourth")
                        : "the artifact carries no query, so there is nothing to run"
                    : measure.NoFourthLevel!;
                Report();
                return verdict;
            }

            // Level 4: the query runs and its rows are compared with the measure.
            List<string> rows;
            clock.Restart();
            try
            {
                rows = measure.Run(prepared);
            }
            catch (Exception exception)
            {
                verdict.RunMs = clock.Elapsed.TotalMilliseconds;
                verdict.Outcome = ForeignVerdict.Failed;
                verdict.Detail = Innermost(exception).Message;
                Report();
                return verdict;
            }

            verdict.RunMs = clock.Elapsed.TotalMilliseconds;
            verdict.Reached = ForeignLevel.Ran;
            verdict.Rows = rows.Count;

            var (agrees, detail) = measure.Compare!(rows);
            verdict.AgreesWithMeasure = agrees;
            verdict.Outcome = agrees ? ForeignVerdict.Agrees : ForeignVerdict.Differs;
            verdict.Detail = agrees ? string.Empty : detail;
            Report();
            return verdict;
        }
    }

    /// <summary>
    /// The fourth level of a project (decision 176): every query opened over the fixture - the
    /// context or the session factory built over the query's own compilation, as the single query
    /// of a matrix key is - and run with a value of the declared type for every parameter, as the
    /// Java suite accepts a query at the third level. The first query that fails stops the project
    /// at <c>failed</c> and the detail names it; otherwise the project <c>ran</c>, the row carries
    /// the rows of all its queries - over empty tables that is the aggregates' one row each, and a
    /// row from anywhere else would say the catalog bound an entity to a table that holds data.
    /// </summary>
    private static void RunProject(ForeignArtifact artifact, List<(ForeignUnit Query, byte[] Assembly)> queries, List<string> mappings, string connectionString, ForeignVerdict verdict)
    {
        var clock = Stopwatch.StartNew();
        var rows = 0;
        var returning = new List<string>();

        foreach (var (query, compiled) in queries)
        {
            try
            {
                using var one = PreparedQuery.Open(artifact.Target, compiled, mappings, connectionString, artifact.Form);
                var count = one.Count(query.Path, [.. one.Parameters.Select(parameter => Sample(parameter.ParameterType))]);
                rows += count;
                if (count > 0)
                {
                    returning.Add($"{query.Path} ({count})");
                }
            }
            catch (Exception exception)
            {
                verdict.RunMs = clock.Elapsed.TotalMilliseconds;
                verdict.Outcome = ForeignVerdict.Failed;
                verdict.Detail = $"{query.Path}: {Innermost(exception).Message}";
                return;
            }
        }

        verdict.RunMs = clock.Elapsed.TotalMilliseconds;
        verdict.Reached = ForeignLevel.Ran;
        verdict.Rows = rows;
        verdict.Outcome = ForeignVerdict.Ran;
        verdict.Detail = $"{queries.Count} queries ran over the empty tables of the project's schema"
            + (returning.Count > 0 ? $"; rows came back from {string.Join(", ", returning)}" : string.Empty);
    }

    /// <summary>The fourth level of one query without a measure (decision 182): it runs over the database of the measure, and the row carries how many rows it returned.</summary>
    private static void RunOne(PreparedQuery prepared, ForeignUnit query, ForeignVerdict verdict)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var count = prepared.Count(query.Path, [.. prepared.Parameters.Select(parameter => Sample(parameter.ParameterType))]);
            verdict.RunMs = clock.Elapsed.TotalMilliseconds;
            verdict.Reached = ForeignLevel.Ran;
            verdict.Rows = count;
            verdict.Outcome = ForeignVerdict.Ran;
            verdict.Detail = "the query ran over the fixture; a point of a series has no measure of equivalence (decision 182)";
        }
        catch (Exception exception)
        {
            verdict.RunMs = clock.Elapsed.TotalMilliseconds;
            verdict.Outcome = ForeignVerdict.Failed;
            verdict.Detail = Innermost(exception).Message;
        }
    }

    /// <summary>
    /// A value of the declared type of a parameter, for a query run without a measure that would
    /// state its arguments: one element in a collection, because an empty parameter list is a
    /// query NHibernate refuses; a moment, a day, a text, a number, a flag, otherwise.
    /// </summary>
    internal static object Sample(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string))
        {
            return "x";
        }

        if (underlying.IsArray)
        {
            var element = underlying.GetElementType()!;
            var array = Array.CreateInstance(element, 1);
            array.SetValue(Sample(element), 0);
            return array;
        }

        if (underlying.IsGenericType && underlying.GetGenericArguments() is [var itemType]
            && typeof(IEnumerable<>).MakeGenericType(itemType).IsAssignableFrom(underlying))
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType))!;
            list.Add(Sample(itemType));
            return list;
        }

        if (underlying.IsEnum)
        {
            return Enum.GetValues(underlying).GetValue(0)!;
        }

        return Type.GetTypeCode(underlying) switch
        {
            TypeCode.Boolean => true,
            TypeCode.Byte => (byte)1,
            TypeCode.SByte => (sbyte)1,
            TypeCode.Int16 => (short)1,
            TypeCode.UInt16 => (ushort)1,
            TypeCode.Int32 => 1,
            TypeCode.UInt32 => 1u,
            TypeCode.Int64 => 1L,
            TypeCode.UInt64 => 1ul,
            TypeCode.Single => 1f,
            TypeCode.Double => 1.0,
            TypeCode.Decimal => 1m,
            TypeCode.Char => 'x',
            TypeCode.DateTime => new DateTime(2011, 1, 1),
            _ when underlying == typeof(DateOnly) => new DateOnly(2011, 1, 1),
            _ when underlying == typeof(TimeOnly) => new TimeOnly(0, 0),
            _ when underlying == typeof(TimeSpan) => TimeSpan.Zero,
            _ when underlying == typeof(DateTimeOffset) => new DateTimeOffset(2011, 1, 1, 0, 0, 0, TimeSpan.Zero),
            _ when underlying == typeof(Guid) => new Guid("11111111-1111-1111-1111-111111111111"),
            _ => throw new InvalidOperationException($"No sample value for a parameter of type {type.Name}."),
        };
    }

    /// <summary>
    /// Why the language of the target does not read the artifact, or null where it reads every
    /// unit: syntax diagnostics only - Roslyn's over a C# unit, the query method inside the
    /// class the compiler puts around it; the XML parser's over a mapping; the T-SQL parser's
    /// over native SQL. HQL has no reader apart from NHibernate's, which the third level is.
    /// </summary>
    internal static string? Unreadable(ForeignArtifact artifact)
    {
        foreach (var unit in artifact.Units)
        {
            var problem = unit.ContentType switch
            {
                ConversionContentType.CSharp or ConversionContentType.CSharpEntity => CSharpSyntax(unit.Content),
                ConversionContentType.CSharpQuery or ConversionContentType.CSharpLinqQuery => CSharpSyntax(GeneratedQueryCompiler.Holder(unit.Content)),
                ConversionContentType.XML => XmlSyntax(unit.Content),
                ConversionContentType.SqlQuery => SqlSyntax(unit.Content),
                _ => null,
            };

            if (problem is not null)
            {
                return $"{unit.Path}: {problem}";
            }
        }

        return null;
    }

    private static string? CSharpSyntax(string text)
    {
        var errors = CSharpSyntaxTree.ParseText(text).GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => $"{diagnostic.Id} {diagnostic.GetMessage()} ({diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1})")
            .ToList();

        return errors.Count == 0 ? null : string.Join("; ", errors);
    }

    private static string? XmlSyntax(string text)
    {
        try
        {
            XDocument.Parse(text);
            return null;
        }
        catch (System.Xml.XmlException exception)
        {
            return exception.Message;
        }
    }

    /// <summary>
    /// The T-SQL parser over a statement, with Dapper's own spelling of a collection parameter -
    /// <c>IN @ids</c>, which Dapper expands into a list at run time - read as the parenthesized
    /// variable T-SQL carries, as <c>Combined/QueryShapeMatrixTest</c> reads it at the second
    /// level: the spelling is the framework's, not a syntax error of the artifact.
    /// </summary>
    private static string? SqlSyntax(string text)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(System.Text.RegularExpressions.Regex.Replace(text, @"\bIN @(\w+)", "IN (@$1)"));
        parser.Parse(reader, out var errors);

        return errors.Count == 0 ? null : string.Join("; ", errors.Select(error => $"{error.Message} ({error.Line})"));
    }

    private static IReadOnlyList<MetadataReference> EntityReferences(ORMEnum target)
        => target == ORMEnum.EFCore ? GeneratedEntityCompiler.EFCoreConsumerReferences : GeneratedEntityCompiler.NHibernateConsumerReferences;

    /// <summary>The third level of an artifact without a query: the model of EF Core, the session factory of NHibernate; Dapper has nothing to refuse.</summary>
    private static void Accept(ORMEnum target, byte[] compiled, IReadOnlyList<string> mappings)
    {
        switch (target)
        {
            case ORMEnum.EFCore:
                EFCoreAcceptance.BuildModel(compiled);
                break;
            case ORMEnum.NHibernate:
                NHibernateAcceptance.BuildSessionFactory(compiled, mappings);
                break;
        }
    }

    private static string Sanitize(string id)
        => new([.. id.Select(character => char.IsLetterOrDigit(character) ? character : '_')]);

    private static Exception Innermost(Exception exception)
        => exception is System.Reflection.TargetInvocationException { InnerException: { } inner } ? Innermost(inner) : exception;

    /// <summary>
    /// What an artifact is measured against: the database its query runs in, how its rows are
    /// read into the canonical form, and what they are compared with. Resolved from the key
    /// before anything runs, so that a key nobody can measure stops the run before the
    /// artifact is read.
    /// </summary>
    private sealed record Measure(
        string ConnectionString,
        Func<PreparedQuery, List<string>>? Run,
        Func<List<string>, (bool Agrees, string Detail)>? Compare,
        string? NoFourthLevel,
        bool RunsProject = false)
    {
        /// <summary>A connection string for a handle that is never opened: the third level of a key without a fourth.</summary>
        private const string Unopened = "Server=localhost;Database=ORMConvertorTests;Trusted_Connection=True;TrustServerCertificate=True;";

        public static Measure For(ForeignArtifact artifact)
        {
            var key = artifact.Key;
            switch (key.Kind)
            {
                case ForeignKey.Matrix:
                {
                    var query = DifferentialMatrix.Queries.SingleOrDefault(candidate => candidate.Id == key.Id)
                        ?? throw new InvalidOperationException($"{artifact.Id}: the matrix has no query {key.Id}; the sections of Tests/Database/Differential/matrix.txt are the keys.");
                    var connectionString = TestDatabase.ConnectionString
                        ?? throw new InvalidOperationException($"{artifact.Id}: a matrix query runs over the fixture, and no test database is configured. {TestDatabase.NotConfiguredReason}");
                    var canonical = query.CanonicalResult();

                    return new Measure(
                        connectionString,
                        prepared => ResultRow.Canonical(
                            prepared.Run(key.Id, [.. query.Arguments.Select(argument => argument.Materialize())], query.Fields, query.Projection),
                            query.Settings, query.Fields, query.OrderingKey),
                        rows => (rows.SequenceEqual(canonical), FibenRun.Difference(canonical, rows)),
                        null);
                }

                case ForeignKey.Fiben:
                {
                    var query = FibenSample.Queries.SingleOrDefault(candidate => candidate.Key.Equals(key.Id, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException($"{artifact.Id}: the FIBEN catalog has no query {key.Id}.");
                    var connectionString = FibenDatabase.ConnectionString
                        ?? throw new InvalidOperationException($"{artifact.Id}: a FIBEN query runs over the Fiben database, and none is configured. {FibenDatabase.NotConfiguredReason}");

                    Dictionary<string, (string Digest, List<string> Rows)>? judge;
                    using (var control = new SqlConnection(connectionString))
                    {
                        control.Open();
                        judge = FibenJudgeTable.Read(control);
                    }

                    if (judge is null)
                    {
                        throw new InvalidOperationException($"{artifact.Id}: the Fiben database holds no judge, so a FIBEN query has no measure (decision 149).");
                    }

                    return new Measure(
                        connectionString,
                        prepared => ResultRow.Canonical(prepared.Run(query.Key, [], query.Columns, projection: true), new ResultRow.RenderSettings(), query.Columns, query.OrderingKey),
                        rows => FibenJudgeTable.Agrees(judge, query.Key, rows) switch
                        {
                            true => (true, string.Empty),
                            false => (false, judge[query.Key].Rows.Count > 0 || rows.Count <= FibenRun.RowsKeptByTheJudge
                                ? FibenRun.Difference(judge[query.Key].Rows, rows)
                                : $"the judge holds {judge[query.Key].Digest}, the artifact returned {FibenRun.Digest(rows)}"),
                            null => (false, "the judge holds no entry for the query - the run it was made from did not run it"),
                        },
                        null);
                }

                case ForeignKey.Ldbc:
                {
                    _ = LdbcSnbSample.Queries.SingleOrDefault(candidate => candidate.Key.Equals(key.Id, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException($"{artifact.Id}: the LDBC catalog has no query {key.Id}.");

                    return new Measure(
                        TestDatabase.ConnectionString ?? Unopened,
                        null,
                        null,
                        "a query of the LDBC catalog is judged by a replay of the validation set, not by one reading, so a foreign artifact of it ends at the third level (decision 172)");
                }

                case ForeignKey.Cross:
                {
                    if (!Enum.TryParse<ORMEnum>(key.Id, ignoreCase: true, out _))
                    {
                        throw new InvalidOperationException($"{artifact.Id}: the scenario key names the source \"{key.Id}\", which is no framework of the tool; the scenarios of CrossFrameworkInputs are one per source framework.");
                    }

                    return new Measure(
                        TestDatabase.ConnectionString ?? Unopened,
                        null,
                        null,
                        "the scenario of CrossFrameworkInputs has no measure at the fourth level - its entity stands over no table of the fixture and its row is stored and read by a scenario written per entity -, so a foreign artifact of it ends at the third level (decision 172)");
                }

                case ForeignKey.Example:
                {
                    _ = ORMConvertorAPI.Data.Examples.GetExamples.SingleOrDefault(candidate => candidate.Key.Equals(key.Id, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException($"{artifact.Id}: the examples have no key {key.Id}; the keys are those of GET /examples.");

                    // The excerpt of a real project - the case study of T1 - has its tables in the
                    // case-study database (decision 176), empty, so its project runs there at the
                    // fourth level - in the targets whose mapping carries the schema the catalog
                    // supplied. Dapper writes the table as the source named it, and a source that
                    // names no schema gives SQL that runs in the default schema of its connection,
                    // which is not the project's; its project ends at the third level and says so.
                    if (ORMConvertorAPI.Data.Examples.RealProjectKeys.Contains(key.Id))
                    {
                        var connectionString = TestDatabase.CaseStudyConnectionString
                            ?? throw new InvalidOperationException($"{artifact.Id}: a case study runs over the tables of its project in the case-study database, and no test database is configured. {TestDatabase.NotConfiguredReason}");

                        return artifact.Target == ORMEnum.Dapper
                            ? new Measure(connectionString, null, null,
                                "the SQL of a Dapper artifact names the tables as the source did, without a schema, and the tables of the project stand in a schema of their own in the case-study database (decision 176); the project is not run")
                            : new Measure(connectionString, null, null, null, RunsProject: true);
                    }

                    return new Measure(
                        TestDatabase.ConnectionString ?? Unopened,
                        null,
                        null,
                        "an example written for the tool runs over no database of it (decision 099), so an artifact of it ends at the third level (decision 174)");
                }

                case ForeignKey.Series:
                {
                    // A point of the search for limits (decision 182): a query over the domain of
                    // the query-shape matrices runs over the fixture's data - runnable is the
                    // measure, no canonical result states the rows of a synthetic query -; a series
                    // of entities, or of a project over no table of the fixture, ends at the third.
                    var point = Experiment.LimitSeries.Parse(key.Id)
                        ?? throw new InvalidOperationException($"{artifact.Id}: no series has the point {key.Id}; the keys are <axis>-<point> over the axes of Experiment/LimitSeries.");

                    if (!point.Axis.OverTheFixture)
                    {
                        return new Measure(
                            TestDatabase.ConnectionString ?? Unopened,
                            null,
                            null,
                            $"the series {point.Axis.Id} grows {point.Axis.Grows}, over no table of the fixture, so a point of it ends at the third level (decision 182)");
                    }

                    var connectionString = TestDatabase.ConnectionString
                        ?? throw new InvalidOperationException($"{artifact.Id}: a point of a query series runs over the fixture, and no test database is configured. {TestDatabase.NotConfiguredReason}");

                    return new Measure(connectionString, null, null, null, RunsProject: true);
                }

                default:
                    throw new InvalidOperationException($"{artifact.Id}: the kind {key.Kind} has no measure.");
            }
        }
    }
}
