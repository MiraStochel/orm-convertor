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
/// at the third level, its measure being a replay rather than one reading.
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

        // Level 2: the units compile under the consumer references of the target.
        var entities = artifact.Entities.Select(unit => unit.Content).ToList();
        var mappings = artifact.Mappings.Select(unit => unit.Content).ToList();
        var method = artifact.QueryMethod;
        var assemblyName = $"Foreign_{Sanitize(artifact.Id)}_{Interlocked.Increment(ref sequence)}";

        clock.Restart();
        var compilation = method is null
            ? GeneratedEntityCompiler.Compile(assemblyName, entities, EntityReferences(artifact.Target))
            : PreparedQuery.Compile(assemblyName, artifact.Target, method.Content, entities, artifact.Form);
        verdict.CompileMs = clock.Elapsed.TotalMilliseconds;

        if (!compilation.Success)
        {
            verdict.Outcome = ForeignVerdict.NotCompiled;
            verdict.Detail = string.Join("; ", compilation.Errors.Select(error => error.ToString()));
            Report();
            return verdict;
        }

        verdict.Reached = ForeignLevel.Compiled;
        Report();

        // Level 3: the framework accepts the artifact.
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

            if (artifact.Target == ORMEnum.NHibernate && artifact.HqlText is { } hql)
            {
                NHibernateQueryAcceptance.CompileQuery(compilation.Assembly!, mappings, hql.Content);
            }
        }
        catch (Exception exception)
        {
            verdict.AcceptMs = clock.Elapsed.TotalMilliseconds;
            verdict.Outcome = ForeignVerdict.NotAccepted;
            verdict.Detail = Innermost(exception).Message;
            prepared?.Dispose();
            Report();
            return verdict;
        }

        verdict.AcceptMs = clock.Elapsed.TotalMilliseconds;
        verdict.Reached = ForeignLevel.Accepted;
        Report();

        using (prepared)
        {
            if (prepared is null || measure.Run is null)
            {
                verdict.Outcome = ForeignVerdict.Accepted;
                verdict.Detail = prepared is null ? "the artifact carries no query, so there is nothing to run" : measure.NoFourthLevel!;
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

    private static string? SqlSyntax(string text)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(text);
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
        string? NoFourthLevel)
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

                default:
                    throw new InvalidOperationException($"{artifact.Id}: the kind {key.Kind} has no measure.");
            }
        }
    }
}
