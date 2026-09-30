using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Sql;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Model;

namespace TransactSql;

/// <summary>
/// The step of the shared T-SQL reading that stands over the whole text, before any query
/// of it is read (decision 108): the guard of a declared foreign dialect (decision 088), the
/// guard of nesting depth (decision 092), the grammar, and the check that nothing in the
/// text changes state. What comes out are the SELECTs of the text in the order they are
/// written, each for <see cref="SqlQueryReader"/> to read into a builder of its own.
///
/// Whether the text may carry more than one SELECT is not the grammar's to know. A bare SQL
/// unit is a script, and T-SQL says what a script of several statements means: a batch whose
/// statements run in order and whose every SELECT returns a result set of its own, so every
/// SELECT is a query of its own. A text handed to a construct of a host framework - a Dapper
/// call, a MyBatis &lt;select&gt;, an NHibernate &lt;sql-query&gt; - is one command mapping
/// one result, and a second SELECT in it is refused as before, unless the construct maps
/// every result set of its text, as Dapper's QueryMultiple does, which makes the text a
/// script again (decision 109). Which of the two a text is, is a fact of the source
/// framework, so the wrapper states it (S1).
/// </summary>
public static class SqlText
{
    /// <summary>
    /// The SELECTs of the text, or null when the text as a whole was refused, with the
    /// reason on the channel. Every refusal here is one of the whole text: a syntax error
    /// anywhere, because the grammar reports errors per script and says nothing reliable
    /// about where one statement ends past them, and a statement that is not a SELECT that
    /// reads, because the queries of a script are independent only while nothing in it
    /// writes. A SELECT that its own reading refuses refuses itself alone, later.
    /// </summary>
    /// <param name="declaredSourceDialect">
    /// The dialect the source declared for this text (decision 088); null where it declared
    /// none. Required rather than defaulted, for the reason <see cref="SqlQueryReader"/>
    /// gives.
    /// </param>
    /// <param name="isScript">
    /// True where the text is a unit of its own, whose every SELECT is a query of its own;
    /// false where it is the argument of one construct of the host framework.
    /// </param>
    public static IReadOnlyList<SqlSelect>? Selects(
        string sql,
        Action<ConversionRecordKind, string, QueryFeature?> report,
        SourceSqlDialect? declaredSourceDialect,
        ParseLimits? limits = null,
        bool isScript = false)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(report);

        limits ??= ParseLimits.Default;

        // Before the grammar, because the grammar is not what decides this (decision 088).
        // It filters syntax and not vocabulary: LIMIT 10 and || fail here as parse errors,
        // whereas SUBSTR(name, 1, 3) is an ordinary function call to TSql160Parser and
        // would come out of the target's visitor under a name T-SQL does not have. The
        // refusal carries no category, being no property of the query (decision 048); the
        // channel is the one a syntax error already leaves by, and only the reason is new.
        if (ForeignDialect.StopsReading(declaredSourceDialect))
        {
            report(ConversionRecordKind.Failure, ForeignDialect.QueryReason, null);
            return null;
        }

        // Before the grammar for a second reason, and the graver one: of all five languages
        // the tool reads, T-SQL gives out first - 1024 levels of parentheses read, 2048 kill
        // the process (decision 092). The grammar has no guard of its own and no way to be
        // given one, but its lexer is separate and is a loop, so the depth is measured on the
        // token stream and the grammar never sees what would overflow it.
        if (NestingDepthGuard.FirstBeyond(Tracked(sql), limits) is { } tooDeep)
        {
            report(ConversionRecordKind.Failure, NestingDepthGuard.Reason(tooDeep, limits), null);
            return null;
        }

        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(new StringReader(sql), out var errors);

        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                // A parse error carries a line and a column, which is what S7 asks the UI to
                // show and what no other source of ours can give.
                report(
                    ConversionRecordKind.Failure,
                    $"The SQL could not be parsed at line {error.Line}, column {error.Column}: {error.Message}",
                    null);
            }

            return null;
        }

        var statements = Statements(fragment);

        // Everything the text says besides reading (decisions 048, 070 and 108). The text
        // used to be read as if it held its first SELECT alone, so `DELETE FROM T; SELECT …`
        // came back as a read-only artifact with nothing to say that the DELETE had been
        // dropped; and a SELECT that writes - into a table or into a variable - is no query
        // that reads either: `SELECT @x = MAX(Id) FROM T` returns no rows, and read without
        // its assignment it came out as SELECT * FROM T. The line is "not a SELECT that
        // reads" rather than a list of harmful statements: SET NOCOUNT ON changes no rows,
        // but USE changes the database every following query reads from, and keeping a list
        // of harmless options would be work with no use for translating queries. A writing
        // statement is outside what the tool translates for any framework, which is the
        // refusal the MyBatis wrapper already makes by element name (decision 084).
        var writing = statements.Where(statement => !Reads(statement)).ToList();
        if (writing.Count > 0)
        {
            report(
                ConversionRecordKind.Failure,
                $"The SQL states {string.Join(", ", writing.Select(Describe))}, which "
                    + (writing.Count == 1 ? "is not a SELECT that reads" : "are not SELECTs that read")
                    + "; the tool translates queries that read, and a text in which a statement writes or sets state says more "
                    + "than its queries, so no artifact was generated.",
                null);
            return null;
        }

        var selects = statements.Cast<SelectStatement>().Select(statement => new SqlSelect(statement)).ToList();

        if (selects.Count == 0)
        {
            report(ConversionRecordKind.Failure, "The SQL contains no SELECT statement to translate.", null);
            return null;
        }

        // The host runs the text as one command and maps one result: Dapper's Query<T> the
        // first set, MyBatis the first unless resultSets names more, NHibernate one per named
        // query. Two methods would claim two queries where the source makes one, and the
        // first alone would be a claim about which set each host maps (decision 108).
        if (!isScript && selects.Count > 1)
        {
            report(
                ConversionRecordKind.Failure,
                $"The SQL states {selects.Count} SELECT statements, but it is passed to the framework as one command that maps one "
                    + "result; translating them as separate queries would state queries the source does not make, and translating the "
                    + "first alone would hand over less than the source says, so no artifact was generated.",
                null);
            return null;
        }

        return selects;
    }

    /// <summary>
    /// The SQL as the shared nesting guard reads it (decision 092): ScriptDom's own lexer,
    /// which is a loop, projected onto text and position. Lexical errors are dropped here on
    /// purpose - the grammar reports them a moment later, with the position S7 asks the
    /// interface to show.
    /// </summary>
    private static IEnumerable<SourceToken> Tracked(string sql)
    {
        using var reader = new StringReader(sql);
        var read = new TSql160Parser(initialQuotedIdentifiers: true).GetTokenStream(reader, out _);

        return read.Select(token => new SourceToken(token.Text, token.Line, token.Column));
    }

    /// <summary>
    /// The statements of every batch in the order they are written - across GO too, since a
    /// batch separator ends a batch and not the script. Navigated explicitly rather than with
    /// a visitor: a visitor descends into subqueries too, and their instructions would then be
    /// emitted into the outer scope.
    /// </summary>
    private static List<TSqlStatement> Statements(TSqlFragment fragment)
        => fragment is TSqlScript script
            ? [.. script.Batches.SelectMany(b => b.Statements)]
            : [];

    /// <summary>A SELECT that returns rows and writes nowhere: neither INTO a table nor into a variable.</summary>
    private static bool Reads(TSqlStatement statement)
        => statement is SelectStatement { Into: null } select && AssignmentIn(select.QueryExpression) is null;

    /// <summary>
    /// The first assignment to a variable among the select elements, where the grammar puts
    /// it: in the query specification itself, or in an operand of a set operation.
    /// </summary>
    private static SelectSetVariable? AssignmentIn(QueryExpression? expression) => expression switch
    {
        QuerySpecification query => query.SelectElements.OfType<SelectSetVariable>().FirstOrDefault(),
        QueryParenthesisExpression parenthesis => AssignmentIn(parenthesis.QueryExpression),
        BinaryQueryExpression binary => AssignmentIn(binary.FirstQueryExpression) ?? AssignmentIn(binary.SecondQueryExpression),
        _ => null,
    };

    /// <summary>
    /// A statement by the keyword it opens with, which is how the refusal names what it found
    /// without the caller having to read a type name out of the grammar. A SELECT that writes
    /// is named by what makes it write.
    /// </summary>
    private static string Describe(TSqlStatement statement) => statement switch
    {
        SelectStatement { Into: not null } => "SELECT … INTO",
        SelectStatement select when AssignmentIn(select.QueryExpression) is { } assignment
            => $"SELECT {assignment.Variable.Name} = …",
        SelectStatement => "SELECT",
        InsertStatement => "INSERT",
        UpdateStatement => "UPDATE",
        DeleteStatement => "DELETE",
        MergeStatement => "MERGE",
        TruncateTableStatement => "TRUNCATE TABLE",
        DeclareVariableStatement => "DECLARE",
        SetVariableStatement or SetOnOffStatement => "SET",
        UseStatement => "USE",
        ExecuteStatement => "EXECUTE",
        _ => statement.GetType().Name,
    };
}

/// <summary>
/// One SELECT of a text, as <see cref="SqlText.Selects"/> found it, for
/// <see cref="SqlQueryReader.Read(SqlSelect)"/> to read. Opaque on purpose: the grammar's own
/// types stay inside this project, and a wrapper handles the SELECT only as a position in the
/// text.
/// </summary>
public sealed class SqlSelect
{
    internal SqlSelect(SelectStatement statement) => Statement = statement;

    internal SelectStatement Statement { get; }
}
