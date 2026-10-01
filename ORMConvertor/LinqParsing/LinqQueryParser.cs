using System.Globalization;
using System.Text;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Naming;
using Common.Reading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace LinqParsing;

/// <summary>
/// Reads a LINQ query written in C# into the query IR (decision 026). Everything here is a
/// property of <c>System.Linq</c> rather than of any ORM: the same Where, Join, Select,
/// OrderBy and GroupBy mean the same thing under either provider. What differs between
/// frameworks is how the chain starts, and that is the one thing a subclass supplies.
///
/// The chain is decomposed explicitly, head to tail, the way the paper's Algorithm 2
/// describes. An earlier version rode on the visit order of a syntax walker, which made the
/// head-to-tail order accidental and turned "a Where directly after a GroupBy is a HAVING"
/// into a special case that had to look back up the tree.
/// </summary>
public abstract class LinqQueryParser(Func<AbstractQueryBuilder> queryBuilders) : IQueryParser
{
    /// <summary>
    /// The builder of the query being read. Assigned at the start of every Parse from the
    /// factory the orchestration supplied: one query, one fresh builder (decision 081). The
    /// parser may not make one itself - a builder belongs to the target framework and this
    /// parser to the source (S1) - and it is never touched outside a Parse call.
    /// </summary>
    protected AbstractQueryBuilder queryBuilder = default!;

    private IReadOnlyList<EntityMap>? entityMaps;

    /// <summary>The mapping of the conversion the unit being read belongs to, for a reader a subclass composes (decision 113).</summary>
    protected IReadOnlyList<EntityMap>? EntityMaps => entityMaps;

    /// <summary>
    /// The variables of the unit being read that hold a query (decision 109). Assigned at the
    /// start of every Parse; empty until then, so a chain decomposed outside a Parse follows
    /// nothing.
    /// </summary>
    private QueryVariables variables = new(SyntaxFactory.CompilationUnit(), _ => false);

    /// <summary>The chains the variables on the way of the query being read held.</summary>
    private readonly List<ExpressionSyntax> followed = [];

    private string sourceAlias = "t";

    /// <summary>
    /// The grouping keys recorded in the scope being read, which is what <c>g.Key</c> names
    /// on the LINQ side - and exactly what the EF Core builder writes for a projection of
    /// that key. Without reading it back the round trip broke in the middle: <c>g.Key</c>
    /// went into the representation as a column <c>Key</c> of a table <c>g</c>, so every
    /// target wrote a column nobody declared and the SQL did not even parse. Saved and
    /// restored around a nested chain, because each scope groups on its own.
    /// </summary>
    private List<GroupingKey> groupingKeys = [];

    /// <summary>
    /// One column of the grouping of a scope, under the name the key object gives it: for
    /// <c>GroupBy(c =&gt; new { N = c.CustomerName })</c> the column is CustomerName and the
    /// name is N, which is what <c>g.Key.N</c> then says.
    /// </summary>
    private readonly record struct GroupingKey(string Table, string Attribute, string Name);

    /// <summary>
    /// The shape of the row the lambdas of a scope range over. Before a join it is the one
    /// entity row of the source. The result selector of a join says what the joined row
    /// carries from there on - <c>(ol, o) =&gt; new { ol, o }</c> makes a row whose members
    /// ol and o are the two entity rows, and a further join flattens or nests it as its own
    /// selector says - and the steps after the join reach a column through those members
    /// (<c>x.o.PlacedAt</c>). The parser has to know them to say which table such a column
    /// belongs to: until it did, the selector was not read at all, the middle member was
    /// skipped and the column was qualified by the source, so a projection written in the
    /// selector came out as every column of both tables without a record (decision 048)
    /// and a filter on the joined table was refused as unreadable.
    /// </summary>
    private abstract record RowShape;

    /// <summary>
    /// The row of one table, under the alias its clause declared, with the mapping behind
    /// it when the conversion holds one - a navigation written from the row is matched to
    /// a relation of that mapping (decision 101).
    /// </summary>
    private sealed record EntityRow(string Alias, EntityMap? Map = null) : RowShape;

    /// <summary>The row a result selector composed, member by member, under the names it gave them.</summary>
    private sealed record JoinedRow(IReadOnlyDictionary<string, RowShape> Members) : RowShape;

    /// <summary>
    /// Resolves a member path to the row it ends on or to the column it names, from wherever
    /// the caller knows the path starts: the parameters of a result selector, or the row of
    /// the scope for a lambda whose parameter is not tracked.
    /// </summary>
    private delegate bool Resolve(ExpressionSyntax expression, out RowShape? endsOnRow, out (string Alias, string Column)? endsOnColumn);

    /// <summary>
    /// The row of the scope being read, and the rows of the scopes around it, innermost
    /// last - a nested chain reaches the joined row of the chain it sits in the way it
    /// reaches the outer alias (decision 061). Saved and restored around a nested chain
    /// like the grouping keys.
    /// </summary>
    private RowShape row = new EntityRow("t");
    private readonly List<RowShape> enclosingRows = [];

    /// <summary>
    /// The construct that sank the condition being read, when the parser can name it - a
    /// value from the enclosing scope, which is what a parameter looks like in LINQ - so
    /// that the clause's refusal says what the caller would have to change (F11). The
    /// category overrides the clause's own only for a parameter, which has a category of
    /// its own (decision 070).
    /// </summary>
    private (string What, QueryFeature? Category)? unread;

    /// <summary>
    /// The limits this parser reads its input under (decision 092). The orchestration sets
    /// them on every parser it creates; one constructed by hand - in a test - runs under the
    /// default, which is the cap the application uses unless its operator moved it.
    /// </summary>
    public ParseLimits Limits { get; set; } = ParseLimits.Default;

    public bool CanParse(ConversionContentType contentType)
        => contentType == ConversionContentType.CSharp;

    /// <summary>
    /// The content type is not consulted here: C# is the only language this parser claims
    /// (see CanParse), so there is nothing to branch on. It is in the signature because the
    /// unit declares its language and a parser reading two of them - the Dapper one, with
    /// SQL bare beside SQL wrapped in C# - has to be told which (decision 047).
    ///
    /// A unit carries a query for every chain over a query root in it, not for the first one
    /// alone (decision 109), each read into a builder of its own, so a chain the reading
    /// refuses refuses itself alone. Queries of one unit are numbered by the position of their
    /// chain in the text, since a chain names nothing.
    ///
    /// The unit is a whole C# file or a fragment of one (decision 111), and the entity pass
    /// reads the same text for its classes. A unit with no chain in it is therefore no error -
    /// a file holding an entity alone is an ordinary input - and yields no query; whether the
    /// unit yielded anything at all is asked of both passes together (decision 081). A text
    /// nested beyond the cap yields nothing here either, and says nothing: the entity pass,
    /// which read the same text first, has reported it.
    /// </summary>
    public IReadOnlyCollection<AbstractQueryBuilder> Parse(ConversionContentType contentType, string source, IReadOnlyList<EntityMap>? maps = null)
    {
        entityMaps = maps;

        if (!Prepare(source, out var root, out var rewriter))
        {
            return [];
        }

        var queries = FindQueries(root, rewriter);
        var builders = new List<AbstractQueryBuilder>(queries.Count);

        for (var i = 0; i < queries.Count; i++)
        {
            queryBuilder = queryBuilders();
            unread = null;
            followed.Clear();

            // Given before the reading, to every query, so that a refused one does not
            // renumber its neighbours (decisions 108 and 109); a single query keeps the fixed
            // name.
            if (queries.Count > 1)
            {
                queryBuilder.QueryName = QueryMethodNaming.Positional(i + 1);
            }

            ReadQuery(root, queries[i], rewriter);
            builders.Add(queryBuilder);
        }

        return builders;
    }

    /// <summary>
    /// The places where the unit hands a query to the provider (decisions 109 and 111): one
    /// node for every query <see cref="Parse"/> would read, found by the same search. The
    /// entity pass of the wrapper asks it which classes hold the code around queries rather
    /// than an entity, and the answer has to be the one the query pass acts on - two searches
    /// would sooner or later disagree, and a class would come out as both or as neither. The
    /// nodes belong to the tree this parser reads, in which a class keeps its name, its
    /// namespace and its members; a text nested beyond the cap has none.
    /// </summary>
    public IReadOnlyList<SyntaxNode> FindHandovers(string source)
    {
        if (!Prepare(source, out var root, out var rewriter))
        {
            return [];
        }

        return [.. FindQueries(root, rewriter).Select(query => query.Node)];
    }

    /// <summary>
    /// The tree of the unit, ready to be searched: the nesting cap checked over the source as
    /// written (decision 092), a file parsed as it stands and a fragment wrapped
    /// (decision 111), the query expressions rewritten into the chains the language defines
    /// them as (decision 103), and the variables of the unit that hold a query described
    /// (decision 109). False for a text nested beyond the cap, which is not parsed at all.
    /// </summary>
    private bool Prepare(string source, out SyntaxNode root, out QueryExpressionRewriter rewriter)
    {
        root = default!;
        rewriter = default!;

        // Before Roslyn, because building the tree is what the cap protects (decision 092).
        if (NestingDepthGuard.FirstBeyond(Tracked(source), Limits) is not null)
        {
            return false;
        }

        root = CSharpUnit.Parse(source, Wrap).GetCompilationUnitRoot();

        // A query expression is read as the method chain the language defines it as
        // (decision 103), so from here on there is only the one shape to read.
        rewriter = new QueryExpressionRewriter(root);
        root = rewriter.Visit(root)!;

        variables = new QueryVariables(root, expression => TryDecompose(expression, out _, out _));
        return true;
    }

    /// <summary>
    /// Recognises the head of the chain — <c>ctx.Customers</c>, <c>ctx.Set&lt;T&gt;()</c>,
    /// <c>session.Query&lt;T&gt;()</c> — and says what it names.
    /// </summary>
    protected abstract bool TryReadQueryRoot(ExpressionSyntax expression, out LinqQueryRoot? root);

    /// <summary>
    /// Whether the provider escapes the argument of a string method before it becomes a LIKE
    /// pattern. EF Core does: <c>StartsWith("A_")</c> matches a literal underscore, so the
    /// argument is read as a core whose wildcards are escaped (decision 102). NHibernate's
    /// provider concatenates the argument with the wildcard as it is, so there the same
    /// call matches any character and the argument is read as the pattern's core verbatim.
    /// A fact about the provider, not about System.Linq, which is why the subclass states it.
    /// </summary>
    protected virtual bool ProviderEscapesStringMethodArguments => true;

    /// <summary>
    /// The provider's own pattern function, when it has one and the invocation is it: EF Core's
    /// <c>EF.Functions.Like(column, pattern[, escape])</c>. Returns the column, the pattern
    /// argument and the escape argument (null where the overload has none) so that the shared
    /// reader can read them the way it reads the string methods; false where the invocation
    /// is not the function. The default knows no such function.
    /// </summary>
    protected virtual bool TryReadProviderPatternFunction(
        InvocationExpressionSyntax invocation,
        out ExpressionSyntax? column,
        out ExpressionSyntax? pattern,
        out ExpressionSyntax? escape)
    {
        column = null;
        pattern = null;
        escape = null;
        return false;
    }

    /// <summary>
    /// A step of the provider's own API that decides what the query returns, with the category
    /// it is refused under - the provider's half of <see cref="ChangesTheRowSet"/>, which names
    /// System.Linq only (decision 070). EF Core's <c>FromSqlRaw()</c> replaces the source of
    /// the chain with SQL; read as an unknown step, it left with a loss record while the
    /// artifact went out over the whole table. Null where the step is not one; the default
    /// knows none.
    /// </summary>
    protected virtual QueryFeature? ProviderStepChangesTheRowSet(string method) => null;

    /// <summary>
    /// The C# text as the shared nesting guard reads it (decision 092): Roslyn's own lexer,
    /// which is a loop, projected onto text and position. Each reading layer writes this for
    /// its own lexer rather than sharing one - a token type is exactly what the five languages
    /// do not have in common, and the number they are measured against is shared instead.
    /// </summary>
    private static IEnumerable<SourceToken> Tracked(string source)
    {
        var text = SourceText.From(source);

        foreach (var token in SyntaxFactory.ParseTokens(source))
        {
            var position = text.Lines.GetLinePosition(token.SpanStart);
            yield return new SourceToken(token.Text, position.Line + 1, position.Character + 1);
        }
    }

    private static string Wrap(string source) =>
        "using System;\n" +
        "using System.Linq;\n" +
        "using System.Collections.Generic;\n" +
        "\n" +
        "public class Snippet\n" +
        "{\n" +
        source +
        "\n}\n";

    /// <summary>
    /// One query of the unit (decision 109): a chain over a query root, a chain whose query
    /// the code composes at run time and which is refused naming the variable, a query
    /// expression the rewrite refused before it became a chain over a root, or a hand-over in
    /// another language than LINQ (decision 113). The node is the place itself - the chain,
    /// the call, or what the refused expression left behind.
    /// </summary>
    private sealed record QueryPlace(SyntaxNode Node, InvocationExpressionSyntax? Chain, string? ComposedIn, ForeignQuery? Foreign = null)
    {
        public int Position => Node.SpanStart;
    }

    /// <summary>
    /// A place where the unit hands its provider a query in another language than LINQ
    /// (decision 113) - SQL through the framework's API for native queries, NHibernate's HQL
    /// through CreateQuery, a query defined elsewhere and handed over by its name, a query the
    /// API composes at run time -, as the wrapper recognizes it in its framework's API (S1),
    /// the way the Dapper wrapper names Dapper's methods (decision 109). <see cref="Read"/>
    /// puts what the place states into the builder of the query: the query, or the record that
    /// says why there is none. A place of its own, so that it is numbered among the queries of
    /// the unit and the entity pass sees the class around it as code rather than as an entity
    /// (decision 111).
    /// </summary>
    protected sealed record ForeignQuery(Action<AbstractQueryBuilder> Read);

    /// <summary>
    /// The hand-over in another language than LINQ that an expression ending in this call is,
    /// asked of the outermost call of every expression before it is read as a chain
    /// (decision 113); null where it is none. The default knows none: which calls of the API
    /// hand a query over is the wrapper's statement about its framework.
    /// </summary>
    protected virtual ForeignQuery? TryReadForeignQuery(InvocationExpressionSyntax outermost) => null;

    /// <summary>
    /// The calls of a fluent chain from its head out: the call whose receiver is not itself a
    /// call first, the outermost last - <c>session.CreateSQLQuery(…)</c>, then the
    /// <c>.SetParameter(…)</c> on what it returned, and so on.
    /// </summary>
    protected static IReadOnlyList<InvocationExpressionSyntax> CallChain(InvocationExpressionSyntax outermost)
    {
        var calls = new List<InvocationExpressionSyntax> { outermost };
        var current = outermost;

        while (current.Expression is MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax inner })
        {
            calls.Add(inner);
            current = inner;
        }

        calls.Reverse();
        return calls;
    }

    /// <summary>
    /// The channel a hand-over in another language reports over into the builder of its
    /// query: the record names the language that was handed over, the shape every query
    /// parser of the solution uses - SQL for a native query, HQL for NHibernate's own.
    /// </summary>
    protected static Action<ConversionRecordKind, string, QueryFeature?> ChannelOf(AbstractQueryBuilder builder, ConversionContentType language)
        => (kind, reason, feature) => builder.Report(new ConversionRecord
        {
            Kind = kind,
            Framework = builder.Descriptor.Framework,
            Artifact = language,
            Feature = feature,
            Reason = reason,
        });

    /// <summary>The name of the method a call invokes, its type arguments left out.</summary>
    protected static string? MethodNameOf(InvocationExpressionSyntax call) => call.Expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        SimpleNameSyntax name => name.Identifier.Text,
        _ => null,
    };

    /// <summary>
    /// The text a string argument states: a regular, verbatim or raw literal through the value
    /// of its token, so that escapes and the indentation of a raw literal have been resolved
    /// by Roslyn rather than unwound by hand - the way the Dapper wrapper takes its SQL. Null
    /// for anything else, which is a text the code composes at run time.
    /// </summary>
    protected static string? LiteralText(ExpressionSyntax? expression)
        => expression is LiteralExpressionSyntax literal && literal.RawKind == (int)SyntaxKind.StringLiteralExpression
            ? literal.Token.ValueText
            : null;

    /// <summary>
    /// The argument of a slice the code sets on a query object as a row count (decision 085):
    /// a non-negative integer literal, or a value from the enclosing scope, which is the
    /// parameter of that name - the reading Skip and Take get. Null for anything else.
    /// </summary>
    protected static RowCount? RowCountOf(ExpressionSyntax? expression) => expression switch
    {
        LiteralExpressionSyntax { Token.Value: int value } when value >= 0 => RowCount.Literal(value),
        LiteralExpressionSyntax { Token.Value: long value } when value >= 0 => RowCount.Literal(value),
        IdentifierNameSyntax identifier => RowCount.Bound(QueryParameter.Named(identifier.Identifier.Text)),
        _ => null,
    };

    /// <summary>
    /// The variable the statement around a call keeps its result in, where the code goes on to
    /// call a method on that variable in a later statement of the block other than the ones
    /// named harmless (decision 113): a query object a host goes on to slice or bind a list on
    /// out of the reading's sight. Null where the result is not kept, or is kept and only
    /// harmlessly used.
    /// </summary>
    protected static string? ContinuedElsewhere(InvocationExpressionSyntax outermost, IReadOnlySet<string> harmless)
    {
        if (outermost.Parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }
            || declarator.FirstAncestorOrSelf<BlockSyntax>() is not { } block)
        {
            return null;
        }

        var name = declarator.Identifier.Text;

        var continued = block.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(call => call.SpanStart > declarator.Span.End)
            .Any(call => call.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver } member
                         && receiver.Identifier.Text == name
                         && !harmless.Contains(member.Name.Identifier.Text));

        return continued ? name : null;
    }

    /// <summary>
    /// The queries of the unit in the order of the text. Outer nodes come before inner ones in
    /// document order, so the first invocation of a chain that decomposes to a query root is
    /// its outermost link, and everything inside it - the inner links, a subquery in a lambda,
    /// a root inside a Join argument - is part of that query, not one of its own. A chain the
    /// code keeps in a variable it only continues is the beginning of the queries that continue
    /// it, not a query (<see cref="QueryVariables"/>).
    ///
    /// Reading every chain rather than the first makes one more line necessary. The root of
    /// EF Core is recognized by its position - <c>x.Customers</c> is a DbSet on whatever x is
    /// (decision 026) - and while the first chain alone was read, the first chain was the
    /// query. Code around it may walk the rows it loaded, though, and <c>o.Lines.Sum(…)</c> on
    /// an element is the same shape: so a root that is a member of an element - of a lambda
    /// parameter or a foreach variable - is a navigation over objects in memory, not a query.
    /// A root that is a call, <c>Set&lt;T&gt;()</c> or <c>Query&lt;T&gt;()</c>, names the
    /// query API whatever it is called on and stays a root.
    /// </summary>
    private List<QueryPlace> FindQueries(SyntaxNode root, QueryExpressionRewriter rewriter)
    {
        var queries = new List<QueryPlace>();
        var taken = new List<TextSpan>();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (taken.Any(span => span.Contains(invocation.Span)))
            {
                continue;
            }

            // A hand-over in another language is asked for before the chain, because a FromSql
            // over a root decomposes as a chain too, and as a whole query it is the other one
            // (decision 113).
            if (TryReadForeignQuery(invocation) is { } foreign)
            {
                taken.Add(invocation.Span);
                queries.Add(new QueryPlace(invocation, null, null, foreign));
                continue;
            }

            if (!TryDecompose(invocation, out _, out _, out var rootExpression)
                || IsElementNavigation(rootExpression!))
            {
                continue;
            }

            taken.Add(invocation.Span);

            var (kind, variable) = variables.StorageOf(invocation);
            if (kind == QueryVariables.StorageKind.Prefix)
            {
                continue;
            }

            queries.Add(new QueryPlace(
                invocation,
                invocation,
                kind == QueryVariables.StorageKind.RunTime ? variable : null));
        }

        // A query expression refused before it became a chain over a root - a let right after
        // the from clause leaves the bare DbSet - is a query of the unit all the same, refused.
        foreach (var (_, _, anchor) in rewriter.Refusals)
        {
            foreach (var node in root.GetAnnotatedNodes(anchor))
            {
                if (!taken.Any(span => span.Contains(node.Span)))
                {
                    queries.Add(new QueryPlace(node, null, null));
                }
            }
        }

        return [.. queries.OrderBy(query => query.Position)];
    }

    /// <summary>
    /// Reads one query into the builder of the moment, and reports on it every clause the
    /// rewrite refused within what the reading covered: the chain itself and every chain a
    /// variable on its way held.
    /// </summary>
    private void ReadQuery(SyntaxNode root, QueryPlace query, QueryExpressionRewriter rewriter)
    {
        if (query.Foreign is { } foreign)
        {
            foreign.Read(queryBuilder);
            return;
        }

        if (query.Chain is null)
        {
            queryBuilder.Push();
            ReportRefusedClauses(root, rewriter, node => node.SpanStart == query.Position);
            queryBuilder.Pop();
            return;
        }

        if (query.ComposedIn is { } variable)
        {
            queryBuilder.Push();
            Report(
                ConversionRecordKind.Failure,
                $"The query is kept in the variable '{variable}', which the code continues in another statement but whose value the text does not fix - it is assigned more than once or chosen by a condition - so the query that reaches the provider is composed at run time; no artifact was generated.");
            ReportRefusedClauses(root, rewriter, node => query.Chain.Span.Contains(node.Span));
            queryBuilder.Pop();
            return;
        }

        TryDecompose(query.Chain, out var queryRoot, out var steps);
        EmitChain(queryRoot!, steps);

        var covered = followed.Prepend(query.Chain).ToList();
        ReportRefusedClauses(root, rewriter, node => covered.Any(read => read.Span.Contains(node.Span)));
    }

    private void ReportRefusedClauses(SyntaxNode root, QueryExpressionRewriter rewriter, Func<SyntaxNode, bool> belongs)
    {
        foreach (var (reason, feature, anchor) in rewriter.Refusals)
        {
            if (root.GetAnnotatedNodes(anchor).Any(belongs))
            {
                Report(ConversionRecordKind.Failure, reason, feature);
            }
        }
    }

    /// <summary>
    /// Whether the root of a chain is a member of an element variable - a lambda parameter or a
    /// foreach variable - rather than of the context: <c>o.Lines</c> on an order the code
    /// loaded. Only a root written as a member access can be one; a root that is a call names
    /// the query API.
    /// </summary>
    private static bool IsElementNavigation(ExpressionSyntax rootExpression)
    {
        if (rootExpression is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax head })
        {
            return false;
        }

        var name = head.Identifier.Text;

        foreach (var ancestor in rootExpression.Ancestors())
        {
            switch (ancestor)
            {
                case SimpleLambdaExpressionSyntax lambda when lambda.Parameter.Identifier.Text == name:
                case ParenthesizedLambdaExpressionSyntax parenthesized when parenthesized.ParameterList.Parameters.Any(p => p.Identifier.Text == name):
                case AnonymousMethodExpressionSyntax anonymous when anonymous.ParameterList?.Parameters.Any(p => p.Identifier.Text == name) == true:
                case ForEachStatementSyntax loop when loop.Identifier.Text == name && loop.Statement.Span.Contains(rootExpression.Span):
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// One link of the chain. <paramref name="RewrittenFrom"/> is the terminal a step stands
    /// in for (decision 103): a Take(1) that was First(), a Where that was the predicate of
    /// FirstOrDefault(predicate), so that a record about the step names what was written.
    /// </summary>
    private sealed record ChainStep(string Name, InvocationExpressionSyntax Node, string? RewrittenFrom = null, string? AfterVariable = null)
    {
        public string Written => RewrittenFrom ?? Name;
    }

    /// <summary>
    /// The links of a chain, with where the chain crossed a variable that holds a query
    /// (decision 112): <see cref="ChainStep.AfterVariable"/> on the first link written after
    /// it, and <see cref="EndsWithVariable"/> where nothing in the expression continues the
    /// variable - the chain is the variable's. Where a step past such a boundary has to read
    /// the rows of the variable as an intermediate result, the variable is the name the
    /// source gave them.
    /// </summary>
    private sealed class ChainSteps : List<ChainStep>
    {
        public ChainSteps()
        {
        }

        public ChainSteps(IEnumerable<ChainStep> steps)
            : base(steps)
        {
        }

        public string? EndsWithVariable { get; set; }
    }

    private bool TryDecompose(
        ExpressionSyntax expression,
        out LinqQueryRoot? root,
        out List<ChainStep> steps)
        => TryDecompose(expression, out root, out steps, out _);

    /// <summary>
    /// The chain from its root to its last link. A head that is a variable holding a query the
    /// text fixes is read on from the chain the variable holds (decision 109): the provider gets
    /// one tree composed of both, and the tool reads that tree, as it reads a query expression
    /// as the chain the language rewrites it into (decision 103). What a variable held is kept
    /// in <see cref="followed"/>, so that what was refused inside it is reported on this query.
    /// </summary>
    private bool TryDecompose(
        ExpressionSyntax expression,
        out LinqQueryRoot? root,
        out List<ChainStep> steps,
        out ExpressionSyntax? rootExpression)
    {
        var chain = new ChainSteps();
        steps = chain;
        var current = expression;

        while (true)
        {
            if (TryReadQueryRoot(current, out root))
            {
                steps.Reverse();
                rootExpression = current;
                return true;
            }

            // (from c in ctx.Customers select c).ToList(): the parentheses a query expression
            // needs before a terminal are no link of the chain.
            if (current is ParenthesizedExpressionSyntax parenthesized)
            {
                current = parenthesized.Expression;
                continue;
            }

            if (current is InvocationExpressionSyntax invocation
                && invocation.Expression is MemberAccessExpressionSyntax member)
            {
                steps.Add(new ChainStep(member.Name.Identifier.Text, invocation));
                current = member.Expression;
                continue;
            }

            if (current is IdentifierNameSyntax head && variables.TryFollow(head, out var held))
            {
                // The boundary of the variable, for a step that has to read its rows as an
                // intermediate result named after it (decision 112). The steps are collected
                // from the outermost in, so the last one so far is the first written after it.
                var name = head.Identifier.Text;
                if (chain.Count == 0)
                {
                    chain.EndsWithVariable ??= name;
                }
                else if (chain[^1].AfterVariable is null)
                {
                    chain[^1] = chain[^1] with { AfterVariable = name };
                }

                followed.Add(held!);
                current = held!;
                continue;
            }

            root = null;
            rootExpression = null;
            return false;
        }
    }

    private void EmitSource(LinqQueryRoot root, string? elementParameter)
    {
        sourceAlias = elementParameter
            ?? (root.Name.Length > 0 ? root.Name[..1].ToLowerInvariant() : "t");
        row = new EntityRow(sourceAlias, MapFor(root.Name));
        queryBuilder.From(ResolveTable(root.Name), sourceAlias);
    }

    /// <summary>
    /// The parameter name of the chain's first lambda that ranges over source elements. The
    /// operand qualifiers read from lambda bodies are these names, so the source alias has
    /// to be the same name; the table's first letter is only the fallback for a chain that
    /// opens without such a lambda. Deriving the alias from the table alone broke exactly
    /// where C# forbids a nested lambda from reusing the enclosing parameter (decision 061):
    /// a subquery over the outer query's own table would have claimed the outer alias and
    /// with it the correlated references. A chain that opens with a join has such a lambda
    /// too - the outer key selector ranges over the source - and taking its name keeps the
    /// source alias apart from the one the result selector gives the joined row, which the
    /// first letter of the table (o for OrderLines, o for Orders) did not.
    ///
    /// A terminal aggregate is not a step of the chain, so its lambda is not seen here:
    /// <c>ctx.Products.Average(x =&gt; x.ListPrice)</c> has no step at all, and the caller
    /// that reads the aggregate supplies the parameter of its lambda as the fallback before
    /// the table's first letter, so that the nested scope keeps the name the source gave it
    /// instead of shadowing the outer alias with the table initial.
    /// </summary>
    private static string? FirstElementLambdaParameter(List<ChainStep> steps)
    {
        if (steps.Count == 0)
        {
            return null;
        }

        var first = steps[0];
        var arguments = first.Node.ArgumentList.Arguments;

        if (first.Name is "Join" or "LeftJoin" or "RightJoin")
        {
            return arguments.Count > 1 && arguments[1].Expression is SimpleLambdaExpressionSyntax outerKey
                ? outerKey.Parameter.Identifier.Text
                : null;
        }

        // The collection selector of SelectMany ranges over the source too (decision 101).
        if (first.Name is not ("Where" or "Select" or "OrderBy" or "OrderByDescending" or "GroupBy" or "SelectMany"))
        {
            return null;
        }

        return arguments.FirstOrDefault()?.Expression is SimpleLambdaExpressionSyntax lambda
            ? lambda.Parameter.Identifier.Text
            : null;
    }

    /// <summary>
    /// Emits one whole chain into its own subquery scope. A set-operation step closes the
    /// scope as the left operand, arms the operation and reads its argument as a chain of its
    /// own, recursively (rule Q12); whatever follows a set operation applies to the composed
    /// result, for which the representation has no slot, so it is reported instead of
    /// emitted - as a failure where dropping it would change which rows come back
    /// (decision 053), as a loss elsewhere.
    /// </summary>
    private void EmitChain(
        LinqQueryRoot root,
        List<ChainStep> steps,
        Action? beforeClose = null,
        string? elementParameter = null)
    {
        var enclosingGroupingKeys = groupingKeys;
        var enclosingRow = row;
        var enclosingProjected = scopeProjected;
        var enclosingProjections = scopeProjections;
        groupingKeys = [];
        scopeProjected = false;
        scopeProjections = [];
        enclosingRows.Add(enclosingRow);

        try
        {
            EmitChainCore(root, steps, beforeClose, elementParameter);
        }
        finally
        {
            groupingKeys = enclosingGroupingKeys;
            row = enclosingRow;
            scopeProjected = enclosingProjected;
            scopeProjections = enclosingProjections;
            enclosingRows.RemoveAt(enclosingRows.Count - 1);
        }
    }

    /// <summary>
    /// Whether the scope being read has projected columns - a Select or a result selector
    /// that named them - after which the lambdas of the chain range over the projected rows,
    /// which no clause of the scope can point at (decision 112). Saved and restored around a
    /// nested chain like the grouping keys.
    /// </summary>
    private bool scopeProjected;

    /// <summary>The names the projection of the scope gave its columns, which an ordering after it may name.</summary>
    private List<string> scopeProjections = [];

    /// <summary>
    /// Whether the provider composes a query over the rows of a projection or a slice, which
    /// the representation carries as an intermediate result (decision 112). EF Core does and
    /// turns it into a derived table; NHibernate's provider translates into HQL, which has
    /// neither a derived table nor WITH, and the default reads no such composition.
    /// </summary>
    protected virtual bool ReadsIntermediateResults => false;

    /// <summary>
    /// Whether a step cannot be read in the scope it follows, because what it ranges over
    /// is the result of that scope (decision 112): a filter, a grouping, a join or another
    /// projection over projected rows; an ordering by anything but a projected column;
    /// after a slice, a step the slice does not commute with; after the collapse of
    /// DISTINCT, a step that reads differently before it. Only a scope that projected
    /// columns qualifies - over the whole entity those steps keep the reading they had.
    /// </summary>
    private bool SplitsTheScope(ChainStep step, bool sliced, bool distinct)
    {
        if (!scopeProjected)
        {
            return false;
        }

        switch (step.Name)
        {
            case "Where" or "GroupBy" or "Join" or "LeftJoin" or "RightJoin" or "SelectMany":
                return true;

            case "Select":
                return !(TryReadLambdaBody(step.Node, out var body)
                         && body is IdentifierNameSyntax identity
                         && LambdaParameterOf(step) == identity.Identifier.Text);

            case "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending":
                if (ProjectedColumnOrdered(step) is null)
                {
                    return true;
                }

                break;
        }

        return (sliced && step.Name is not ("Skip" or "Take") && !CommutesWithPagination(step.Name))
               || (distinct && DoesNotCommuteWithDistinct(step.Name));
    }

    /// <summary>The projected column an ordering after the projection names - <c>p =&gt; p.Total</c> -, or null.</summary>
    private string? ProjectedColumnOrdered(ChainStep step)
        => TryReadLambdaBody(step.Node, out var body)
           && body is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax parameter } member
           && parameter.Identifier.Text == LambdaParameterOf(step)
           && scopeProjections.Contains(member.Name.Identifier.Text, StringComparer.Ordinal)
            ? member.Name.Identifier.Text
            : null;

    /// <summary>The parameter of the step's lambda that ranges over its rows: the outer key selector of a join, the first lambda of the rest.</summary>
    private static string? LambdaParameterOf(ChainStep step)
    {
        var arguments = step.Node.ArgumentList.Arguments;
        var lambda = step.Name is "Join" or "LeftJoin" or "RightJoin"
            ? (arguments.Count > 1 ? arguments[1].Expression : null)
            : arguments.FirstOrDefault()?.Expression;

        return lambda is SimpleLambdaExpressionSyntax simple ? simple.Parameter.Identifier.Text : null;
    }

    private void EmitChainCore(
        LinqQueryRoot root,
        List<ChainStep> steps,
        Action? beforeClose,
        string? elementParameter)
    {
        steps = ExpandSingleRowTerminals(steps);
        var endsWithVariable = (steps as ChainSteps)?.EndsWithVariable;

        queryBuilder.Push();
        EmitSource(root, FirstElementLambdaParameter(steps) ?? elementParameter);

        bool inSetOperation = false;
        bool distinct = false;
        RowCount? pendingOffset = null;
        RowCount? pendingLimit = null;
        string? refusedTerminal = null;

        // Pagination is recorded when the scope closes, so that Skip and Take collected
        // along the chain end up as one instruction in offset-then-limit normal form
        // (decision 060).
        void FlushPagination()
        {
            queryBuilder.Paginate(pendingOffset, pendingLimit);
            pendingOffset = pendingLimit = null;
        }

        // The scope read so far becomes an intermediate result of the query, and the chain
        // goes on in a scope of its own over it (decision 112): the name is the variable
        // that holds the rows where the chain crossed one, else the parameter the next step
        // gives them, and the alias of the new scope is that parameter - the name the
        // lambdas over the rows use. A variable read again - joined once and aggregated
        // once - is one definition, read once; a parameter name met twice would name two.
        bool SplitScope(string? name, bool fromVariable, string? alias)
        {
            if (!ReadsIntermediateResults)
            {
                Report(
                    ConversionRecordKind.Failure,
                    "The chain goes on past its projection or its slice with a step that ranges over their result, which this provider does not compose into a query; no artifact was generated.",
                    QueryFeature.IntermediateResult);
                scopeProjected = false;
                return false;
            }

            if (name is null)
            {
                Report(
                    ConversionRecordKind.Failure,
                    "The chain goes on past its projection or its slice with a step that ranges over their result and names it nowhere - no variable holds the rows and no lambda gives them a name -, and the representation does not invent one; no artifact was generated.",
                    QueryFeature.IntermediateResult);
                scopeProjected = false;
                return false;
            }

            FlushPagination();
            var body = queryBuilder.PopOperand();

            if (!queryBuilder.Defines(name))
            {
                queryBuilder.Define(name, body);
            }
            else if (!fromVariable)
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"The chain names the rows of two intermediate results '{name}', and the representation names each once per query; no artifact was generated.",
                    QueryFeature.IntermediateResult);
            }

            queryBuilder.Push();
            sourceAlias = alias ?? name;
            row = new EntityRow(sourceAlias);
            queryBuilder.From(name, sourceAlias);
            groupingKeys = [];
            scopeProjected = false;
            scopeProjections = [];
            return true;
        }

        bool HandleSkipOrTake(ChainStep step)
        {
            if (step.Name == "Skip" && pendingLimit is not null)
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"{step.Written}() after Take() slices differently from the offset-then-limit form the query representation carries; no artifact was generated.",
                    QueryFeature.Pagination);
                return false;
            }

            if ((step.Name == "Skip" ? pendingOffset : pendingLimit) is not null)
            {
                Report(
                    ConversionRecordKind.Failure,
                    step.RewrittenFrom is null
                        ? $"A repeated {step.Name}() has no counterpart in the query representation; no artifact was generated."
                        : $"{step.Written}() after {step.Name}() would slice a slice, which the query representation carries only once; no artifact was generated.",
                    QueryFeature.Pagination);
                return false;
            }

            var argument = step.Node.ArgumentList.Arguments.FirstOrDefault()?.Expression;

            RowCount? count;

            // A value from the enclosing scope is the parameter of the same name, which is
            // the shape a page size has in any real chain (decision 085). It is the same
            // reading the parser does in operand position (decision 083); the scalar is the
            // builder template's to fill in, and here it comes from the clause.
            if (argument is IdentifierNameSyntax fromScope)
            {
                count = RowCount.Bound(QueryParameter.Named(fromScope.Identifier.Text));
            }
            else if (argument is LiteralExpressionSyntax literal && literal.Token.Value is int value && value >= 0)
            {
                count = RowCount.Literal(value);
            }
            else
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"The argument of {step.Written}() is neither a non-negative integer literal nor a value from the enclosing scope, and a pagination the artifact does not carry would change which rows the query returns; no artifact was generated.",
                    QueryFeature.Pagination);
                return false;
            }

            if (step.Name == "Skip")
            {
                pendingOffset = count;
            }
            else
            {
                pendingLimit = count;
            }

            return true;
        }

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            // The steps a terminal expanded into are one construct: once one of them is
            // refused, the rest would only repeat the refusal in other words.
            if (step.RewrittenFrom is not null && step.RewrittenFrom == refusedTerminal)
            {
                continue;
            }

            if (TryMapSetOperation(step.Name, out var operation))
            {
                var argument = step.Node.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                if (argument is null || !TryDecompose(argument, out var innerRoot, out var innerSteps))
                {
                    Report(
                        ConversionRecordKind.Failure,
                        $"The argument of {step.Name}() is not a query chain, so the set operation could not be read; no artifact was generated.",
                        QueryFeature.SetOperation);
                    continue;
                }

                if (!inSetOperation)
                {
                    FlushPagination();
                    queryBuilder.Pop();
                }

                queryBuilder.SetOperation(operation);
                queryBuilder.Push();
                EmitChain(innerRoot!, innerSteps);
                queryBuilder.Pop();
                inSetOperation = true;
                continue;
            }

            if (inSetOperation)
            {
                ReportStepAfterSetOperation(step);
                continue;
            }

            // A step over the result of the scope so far reads it as an intermediate result
            // (decision 112); the refusals below are what remains for a scope that cannot.
            if (SplitsTheScope(step, pendingOffset is not null || pendingLimit is not null, distinct)
                && SplitScope(step.AfterVariable ?? LambdaParameterOf(step), step.AfterVariable is not null, LambdaParameterOf(step)))
            {
                distinct = false;
            }

            if (step.Name is "Skip" or "Take")
            {
                if (!HandleSkipOrTake(step))
                {
                    refusedTerminal = step.RewrittenFrom;
                }
                else if (step.Name == "Take" && step.RewrittenFrom is { } terminal)
                {
                    ReportSingleRowTerminal(terminal);
                }

                continue;
            }

            // The representation carries pagination as the last relational operation, so a
            // step that does not commute with the slice cannot follow it: a filter moved in
            // front of the slice selects different rows (decision 060). Projection and
            // materialization commute and pass.
            if ((pendingOffset is not null || pendingLimit is not null) && !CommutesWithPagination(step.Name))
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"{step.Written}() after Skip() or Take() does not commute with the slice, which the query representation carries only as the last operation; no artifact was generated.",
                    QueryFeature.Pagination);
                refusedTerminal = step.RewrittenFrom;
                continue;
            }

            if (step.Name == "Distinct")
            {
                queryBuilder.Distinct();
                distinct = true;
                continue;
            }

            // The representation carries DISTINCT over the final projection (decision 073),
            // so a step that does not commute with the collapse cannot follow it: a
            // projection after it returns duplicates SELECT DISTINCT would fold, a grouping
            // or a join after it groups or multiplies other rows. Filters, orderings, the
            // slice and materialization commute and pass.
            if (distinct && DoesNotCommuteWithDistinct(step.Name))
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"{step.Written}() after Distinct() does not commute with the collapse, which the query representation carries over the final projection; no artifact was generated.",
                    step.Name switch
                    {
                        "Select" => QueryFeature.Projection,
                        "GroupBy" => QueryFeature.Grouping,
                        _ => QueryFeature.Join,
                    });
                continue;
            }

            EmitStep(step, followsGrouping: i > 0 && steps[i - 1].Name == "GroupBy");
        }

        if (!inSetOperation)
        {
            // A terminal aggregate over projected rows - the maximum of counts - aggregates
            // the result of the scope, so it stands over that result (decision 112).
            if (beforeClose is not null && scopeProjected)
            {
                SplitScope(endsWithVariable ?? elementParameter, endsWithVariable is not null, elementParameter);
            }

            beforeClose?.Invoke();
            FlushPagination();
            queryBuilder.Pop();
        }
        else if (beforeClose is not null)
        {
            Report(
                ConversionRecordKind.Failure,
                "An aggregate over a set operation cannot be carried as a subquery; no artifact was generated.",
                QueryFeature.Subquery);
        }
    }

    private static bool CommutesWithPagination(string method) => method is
        "Select" or "ToList" or "ToArray" or "ToListAsync" or "ToArrayAsync"
        or "AsQueryable" or "AsNoTracking" or "AsNoTrackingWithIdentityResolution";

    /// <summary>
    /// The steps that read differently before and after a Distinct() (decision 073). An
    /// unknown step is not among them on purpose: the parser cannot tell what it would
    /// change, and it stays the loss decision 070 made it.
    /// </summary>
    private static bool DoesNotCommuteWithDistinct(string method) => method is
        "Select" or "GroupBy" or "Join" or "LeftJoin" or "RightJoin" or "SelectMany";

    private static bool TryMapSetOperation(string method, out SetOperationType operation)
    {
        switch (method)
        {
            case "Union": operation = SetOperationType.Union; return true;
            case "Concat": operation = SetOperationType.UnionAll; return true;
            case "Intersect": operation = SetOperationType.Intersect; return true;
            case "Except": operation = SetOperationType.Except; return true;
            default: operation = default; return false;
        }
    }

    private void ReportStepAfterSetOperation(ChainStep step)
    {
        switch (step.Name)
        {
            // Materialisation and tracking say nothing about the query's structure.
            case "ToList":
            case "ToArray":
            case "ToListAsync":
            case "ToArrayAsync":
            case "AsQueryable":
            case "AsNoTracking":
            case "AsNoTrackingWithIdentityResolution":
                return;

            case "OrderBy":
            case "OrderByDescending":
            case "ThenBy":
            case "ThenByDescending":
                Report(
                    ConversionRecordKind.Loss,
                    "An ordering applied after a set operation has no place in the query representation; it was dropped.",
                    QueryFeature.Ordering);
                return;

            case "Take":
            case "Skip":
                Report(
                    ConversionRecordKind.Failure,
                    $"{step.Written}() applied after a set operation cannot be carried, and dropping it would change which rows the query returns; no artifact was generated.",
                    QueryFeature.Pagination);
                return;

            case "Select":
                Report(
                    ConversionRecordKind.Loss,
                    "Select() applied after a set operation has no place in the query representation; the operands' own shape is kept.",
                    QueryFeature.Projection);
                return;

            // A Distinct() over the composed result is recorded beside the set operation;
            // what it means there - an identity, a UNION ALL turned UNION, or a refusal - is
            // the template's to say (decision 073).
            case "Distinct":
                queryBuilder.Distinct();
                return;

            case "Where":
            case "Join":
            case "LeftJoin":
            case "RightJoin":
            case "SelectMany":
            case "GroupBy":
                Report(
                    ConversionRecordKind.Failure,
                    $"{step.Written}() applied after a set operation cannot be carried, and dropping it would change which rows the query returns; no artifact was generated.",
                    step.Name switch
                    {
                        "Where" => QueryFeature.Filtering,
                        "GroupBy" => QueryFeature.Grouping,
                        _ => QueryFeature.Join,
                    });
                return;

            default:
                // The same enumeration as on the ordinary chain: a step that decides what
                // comes back is refused wherever it stands, and a set operation behind it
                // changes nothing about that.
                if ((ChangesTheRowSet(step.Name) ?? ProviderStepChangesTheRowSet(step.Name)) is { } feature)
                {
                    Report(
                        ConversionRecordKind.Failure,
                        $"{step.Name}() applied after a set operation decides what the query returns and the representation does not carry it; no artifact was generated.",
                        feature);
                    return;
                }

                ReportUnsupported(step.Name, null);
                return;
        }
    }

    private void EmitStep(ChainStep step, bool followsGrouping)
    {
        // Every step names the rows with a lambda parameter of its own, and only the first
        // one became the alias of the source. A later step that calls the same rows by
        // another name means the same alias, so its name stands for the alias while the step
        // is read - the qualifier used to be the parameter itself, an alias no clause
        // declared. A scope that grouped ranges over groups instead, and a joined row is
        // resolved through its members, so neither is touched.
        var parameter = groupingKeys.Count == 0 && row is EntityRow ? LambdaParameterOf(step) : null;
        var renamed = parameter is not null
                      && row is EntityRow { Alias: var alias }
                      && parameter != alias
                      && aliasSubstitutions.TryAdd(parameter, alias);

        try
        {
            EmitStepCore(step, followsGrouping);
        }
        finally
        {
            if (renamed)
            {
                aliasSubstitutions.Remove(parameter!);
            }
        }
    }

    private void EmitStepCore(ChainStep step, bool followsGrouping)
    {
        switch (step.Name)
        {
            // A Where directly after a GroupBy filters the groups, which is HAVING.
            case "Where" when followsGrouping:
                HandleHaving(step.Node);
                break;
            case "Where":
                HandleWhere(step.Node);
                break;

            case "Join":
                HandleJoin(step.Node, JoinKind.Inner);
                break;

            // EF Core 10 added explicit outer joins; before it, LINQ could only express
            // an inner join directly.
            case "LeftJoin":
                HandleJoin(step.Node, JoinKind.Left);
                break;
            case "RightJoin":
                HandleJoin(step.Node, JoinKind.Right);
                break;

            // A second `from` over a collection of the row: the join along an association
            // path, derived from the relation (decision 101).
            case "SelectMany":
                HandleSelectMany(step.Node);
                break;

            case "Select":
                HandleSelect(step.Node);
                break;

            case "OrderBy":
            case "ThenBy":
                HandleOrderBy(step.Node, asc: true);
                break;
            case "OrderByDescending":
            case "ThenByDescending":
                HandleOrderBy(step.Node, asc: false);
                break;

            case "GroupBy":
                HandleGroupBy(step.Node);
                break;

            // Materialisation and tracking say nothing about the query's structure.
            case "ToList":
            case "ToArray":
            case "ToListAsync":
            case "ToArrayAsync":
            case "AsQueryable":
            case "AsNoTracking":
            case "AsNoTrackingWithIdentityResolution":
                break;

            default:
                // Last() is the one single-row terminal that is not a slice of the rows as
                // ordered: it is the first row of the reversed ordering, and reversing every
                // ordering key is a rewrite of another instruction, not of this step.
                if (WithoutAsync(step.Name) is "Last" or "LastOrDefault")
                {
                    Report(
                        ConversionRecordKind.Failure,
                        $"{step.Name}() selects the last row of the ordering, which the query representation could carry only by reversing every ordering key, and an artifact emitted without it would answer something else; no artifact was generated.",
                        QueryFeature.Pagination);
                    break;
                }

                if ((ChangesTheRowSet(step.Name) ?? ProviderStepChangesTheRowSet(step.Name)) is { } feature)
                {
                    Report(
                        ConversionRecordKind.Failure,
                        $"{step.Name}() decides what the query returns and the representation does not carry it, so an artifact emitted without it would answer something else; no artifact was generated.",
                        feature);
                    break;
                }

                ReportUnsupported(step.Name, null);
                break;
        }
    }

    /// <summary>
    /// Steps of <c>System.Linq</c> the representation does not carry and which nevertheless
    /// decide what comes back: a filter, a slice, a join, or a terminal that answers with one
    /// value instead of rows. Naming them is what decision 070 asks for - the enumeration
    /// used to stop at Where, Join, GroupBy, Skip and Take, so OfType() (a filter), Last()
    /// (the row of a reversed ordering) and a terminal Count() (a number instead of rows)
    /// fell through to the unknown step and left with a loss record while the artifact went
    /// out returning every row. The async forms EF Core adds are the same steps. First(),
    /// Single() and ElementAt() are not here: they are the slices the chain already carries,
    /// and the chain is expanded to say so before it is read (decision 103). SelectMany() is
    /// not here either: it is the join along an association path, read from the relation
    /// (decision 101), and what it cannot be read as it refuses by itself. Null means the
    /// step is not one of them.
    /// </summary>
    private static QueryFeature? ChangesTheRowSet(string method) => WithoutAsync(method) switch
    {
        "OfType" => QueryFeature.Filtering,
        "SkipWhile" or "TakeWhile" or "DefaultIfEmpty" => QueryFeature.Pagination,
        "Last" or "LastOrDefault" => QueryFeature.Pagination,
        "GroupJoin" or "Zip" => QueryFeature.Join,
        "Count" or "LongCount" or "Sum" or "Average" or "Min" or "Max" or "Aggregate" => QueryFeature.Aggregation,
        "Any" or "All" or "Contains" => QueryFeature.Filtering,
        _ => null,
    };

    private static string WithoutAsync(string method)
        => method.Length > 5 && method.EndsWith("Async", StringComparison.Ordinal) ? method[..^5] : method;

    /// <summary>
    /// Replaces a single-row terminal by the steps it is short for (decision 103): First(),
    /// FirstOrDefault(), Single() and SingleOrDefault() by Take(1), with the predicate form
    /// putting the predicate in a Where() first, exactly as First(predicate) is defined;
    /// ElementAt(n) and ElementAtOrDefault(n) by Skip(n).Take(1); the async forms EF Core
    /// adds alike. The rows the chain describes are the same. What the terminal adds - one
    /// object instead of a list, for Single() the check that there is no second row - is a
    /// fact of the calling code, which the representation does not describe (decision 065),
    /// so it is said in a convention record when the slice is recorded. Every step carries
    /// the terminal it stands in for, so that a refusal names what was written.
    /// </summary>
    private static List<ChainStep> ExpandSingleRowTerminals(List<ChainStep> steps)
    {
        ChainSteps? expanded = null;

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var terminal = WithoutAsync(step.Name);

            if (terminal is not ("First" or "FirstOrDefault" or "Single" or "SingleOrDefault" or "ElementAt" or "ElementAtOrDefault"))
            {
                expanded?.Add(step);
                continue;
            }

            expanded ??= new ChainSteps(steps.Take(i)) { EndsWithVariable = (steps as ChainSteps)?.EndsWithVariable };
            var receiver = ((MemberAccessExpressionSyntax)step.Node.Expression).Expression;

            // The boundary of a variable the terminal stood right after stays on the first
            // step it expands into (decision 112).
            var boundary = step.AfterVariable;

            if (terminal is "ElementAt" or "ElementAtOrDefault")
            {
                expanded.Add(new ChainStep("Skip", Synthesized(receiver, "Skip", step.Node.ArgumentList), step.Name, boundary));
                boundary = null;
            }
            else if (step.Node.ArgumentList.Arguments.FirstOrDefault(a => a.Expression is LambdaExpressionSyntax) is { } predicate)
            {
                // A cancellation token beside the predicate says nothing about the rows and
                // is left alone, as it is on ToListAsync().
                expanded.Add(new ChainStep(
                    "Where",
                    Synthesized(receiver, "Where", SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(predicate))),
                    step.Name,
                    boundary));
                boundary = null;
            }

            expanded.Add(new ChainStep(
                "Take",
                Synthesized(receiver, "Take", SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(1)))))),
                step.Name,
                boundary));
        }

        return expanded ?? steps;
    }

    private static InvocationExpressionSyntax Synthesized(ExpressionSyntax receiver, string method, ArgumentListSyntax arguments)
        => SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, receiver, SyntaxFactory.IdentifierName(method)),
            arguments);

    /// <summary>
    /// The convention record of a single-row terminal read as a slice (decision 103): the
    /// artifact says Take(1) where the source said First(), and what the source said beyond
    /// the rows is named, because the artifact does not carry it.
    /// </summary>
    private void ReportSingleRowTerminal(string terminal)
    {
        var slice = WithoutAsync(terminal) is "ElementAt" or "ElementAtOrDefault" ? "Skip(n).Take(1)" : "Take(1)";
        var beyondTheRows = WithoutAsync(terminal) is "Single" or "SingleOrDefault"
            ? "the check that the query yields no second row"
            : "that the caller receives it as a single value rather than as a list of one";

        Report(
            ConversionRecordKind.Convention,
            $"{terminal}() is carried as {slice}, which selects the same row; {beyondTheRows} is a fact of the calling code, not of the query, and the artifact does not carry it.",
            QueryFeature.Pagination);
    }

    /// <summary>
    /// A step the parser does not know. It stays a loss on purpose (decision 070): the
    /// parser cannot tell what an unknown call would change, and refusing every one of
    /// them would refuse Include() or TagWith() too, which change no rows. The steps known
    /// to change rows are named above; the record therefore claims only that the call was
    /// left out, not that the output is merely poorer.
    /// </summary>
    private void ReportUnsupported(string method, QueryFeature? feature)
        => Report(
            ConversionRecordKind.Loss,
            $"The query calls {method}(), which the query representation does not carry; the call was left out.",
            feature);

    private void HandleWhere(InvocationExpressionSyntax node)
    {
        if (!TryReadLambdaBody(node, out var body))
        {
            Report(
                ConversionRecordKind.Failure,
                "A Where() whose argument is not a lambda cannot be read, and a query emitted without its filter would return different rows; no artifact was generated.",
                QueryFeature.Filtering);
            return;
        }

        var condition = ParseCondition(body!);
        if (condition is null)
        {
            // Silence here used to lose the whole predicate, then a loss record let the
            // query go out without it; both returned rows the source excluded (decision 070).
            Refuse($"predicate '{body}'", "a query emitted without its filter would return different rows", QueryFeature.Filtering);
            return;
        }

        queryBuilder.Where(condition);
    }

    /// <summary>
    /// Refuses the artifact for a clause the condition tree cannot carry (decision 070). A
    /// query emitted without its filter, join or grouping returns different rows, which is
    /// the line decision 053 drew for the builders; the parser holds it on the way in, over
    /// the same channel, so no artifact comes out. Reading goes on afterwards so that every
    /// reason reaches the caller at once.
    /// </summary>
    private void Refuse(string clause, string consequence, QueryFeature feature)
    {
        var (what, category) = unread ?? ("a construct the condition tree cannot carry", (QueryFeature?)null);
        unread = null;

        Report(
            ConversionRecordKind.Failure,
            $"The {clause} uses {what}, and {consequence}; no artifact was generated.",
            category ?? feature);
    }

    private void HandleJoin(InvocationExpressionSyntax node, JoinKind kind)
    {
        // A join both filters and multiplies, so a query emitted without one returns
        // different rows (decisions 065 and 070): refused, never dropped.
        var args = node.ArgumentList.Arguments;
        if (args.Count < 3)
        {
            Report(
                ConversionRecordKind.Failure,
                "A join with too few arguments cannot be read, and a query emitted without its join would return different rows; no artifact was generated.",
                QueryFeature.Join);
            return;
        }

        string rightName = NameOfSource(args[0].Expression);
        string rightTable = ResolveTable(rightName);
        var rightMap = MapFor(rightName);

        // A variable that holds a query is the inner sequence of the join: the rows it holds,
        // an intermediate result named after it (decision 112). A variable that holds the
        // bare root is that table.
        if (args[0].Expression is IdentifierNameSyntax variable
            && variables.TryFollow(variable, out var held)
            && TryDecompose(held!, out var heldRoot, out var heldSteps))
        {
            if (heldSteps.Count == 0)
            {
                rightName = heldRoot!.Name;
                rightTable = ResolveTable(rightName);
                rightMap = MapFor(rightName);
            }
            else if (DefineVariable(variable.Identifier.Text, held!, heldRoot!, heldSteps) is { } definition)
            {
                rightTable = definition;
                rightMap = null;
            }
            else
            {
                return;
            }
        }

        var selector = args.Count > 3 ? args[3].Expression as ParenthesizedLambdaExpressionSyntax : null;
        string rightAlias = JoinedAlias(selector, rightTable);

        if (args[1].Expression is not SimpleLambdaExpressionSyntax outer
            || args[2].Expression is not SimpleLambdaExpressionSyntax inner
            || outer.Body is not ExpressionSyntax outerBody
            || inner.Body is not ExpressionSyntax innerBody)
        {
            Report(
                ConversionRecordKind.Failure,
                "A join whose key selectors are not lambdas cannot be read, and a query emitted without its join would return different rows; no artifact was generated.",
                QueryFeature.Join);
            return;
        }

        var onCondition = BuildJoinCondition(outerBody, innerBody, sourceAlias, rightAlias);
        if (onCondition is null)
        {
            Report(
                ConversionRecordKind.Failure,
                "A join whose key selectors do not pair up column for column has no shape the query representation carries, and a query emitted without its join would return different rows; no artifact was generated.",
                QueryFeature.Join);
            return;
        }

        queryBuilder.Join(kind, sourceAlias, rightTable, onCondition, rightAlias);
        ReadResultSelector(selector, new EntityRow(rightAlias, rightMap), "join");
    }

    /// <summary>
    /// The query a variable holds, read as an intermediate result named after the variable
    /// (decision 112) - once per query, so that a variable joined here and aggregated in a
    /// subquery elsewhere is one definition. The chain is read as a scope of its own that
    /// sees no row of the query around it, as a definition does not. Null when the provider
    /// does not compose a query over another, having reported why.
    /// </summary>
    private string? DefineVariable(string name, ExpressionSyntax held, LinqQueryRoot root, List<ChainStep> steps)
    {
        if (queryBuilder.Defines(name))
        {
            return name;
        }

        if (!ReadsIntermediateResults)
        {
            Report(
                ConversionRecordKind.Failure,
                $"The join reads the query the variable '{name}' holds as its inner sequence, which this provider does not compose into a query; no artifact was generated.",
                QueryFeature.IntermediateResult);
            return null;
        }

        var outerRow = row;
        var outerAlias = sourceAlias;
        var outerRows = enclosingRows.ToList();
        enclosingRows.Clear();
        row = new JoinedRow(new Dictionary<string, RowShape>(StringComparer.Ordinal));

        followed.Add(held);

        queryBuilder.Push();
        EmitChain(root, steps);
        var body = queryBuilder.PopOperand();

        row = outerRow;
        sourceAlias = outerAlias;
        enclosingRows.Clear();
        enclosingRows.AddRange(outerRows);

        queryBuilder.Define(name, body);
        return name;
    }

    /// <summary>
    /// <c>SelectMany(c =&gt; c.Orders, (c, o) =&gt; …)</c>, which is what a second <c>from</c>
    /// over a collection of the row rewrites to (decision 103): the join along an
    /// association path, in the shape LINQ writes it (decision 101, paper rule Q7). The
    /// predicate is not in the query but in the mapping, so it is derived from the relation
    /// the navigation names - FK(left) = PK(right) over its column pairs - and the join
    /// reaches the builder in the very shape a Join with key selectors takes; the result
    /// selector is read as a join's. What the maps of the conversion do not hold is refused
    /// by name and never guessed (decision 067), because a query emitted without its join
    /// would return different rows (decision 070). A SelectMany whose collection is not a
    /// navigation from the row - a second source, so a cross join - stays refused as the
    /// comma join is in T-SQL and HQL.
    /// </summary>
    private void HandleSelectMany(InvocationExpressionSyntax node)
    {
        var args = node.ArgumentList.Arguments;
        const string consequence = "a query emitted without its join would return different rows; no artifact was generated.";

        if (args.Count is < 1 or > 2
            || args[0].Expression is not SimpleLambdaExpressionSyntax collection
            || collection.Body is not ExpressionSyntax collectionBody)
        {
            Report(
                ConversionRecordKind.Failure,
                $"SelectMany() whose collection selector is not a lambda over the row cannot be read, and {consequence}",
                QueryFeature.Join);
            return;
        }

        var path = PathOf(collectionBody);
        if (path is not { Count: >= 2 } || path[0] != collection.Parameter.Identifier.Text)
        {
            Report(
                ConversionRecordKind.Failure,
                $"SelectMany() over '{collectionBody}', which is not an association path from the row, is not a join the query representation carries - over a second source it is a cross join - and {consequence}",
                QueryFeature.Join);
            return;
        }

        var written = string.Join('.', path);
        var association = ResolveAssociation(path, written, out var failure);
        if (association is null)
        {
            Report(ConversionRecordKind.Failure, failure!, QueryFeature.Join);
            return;
        }

        var (relation, owner, target) = association;
        var rightTable = Qualify(target, target.Table ?? relation.TargetEntity);
        var selector = args.Count > 1 ? args[1].Expression as ParenthesizedLambdaExpressionSyntax : null;
        var rightAlias = JoinedAlias(selector, rightTable);
        var joined = new EntityRow(rightAlias, target);

        queryBuilder.Join(JoinKind.Inner, sourceAlias, rightTable, AssociationCondition(relation, owner.Alias, rightAlias), rightAlias);

        // Without a result selector the rows are the collection's alone: one side of the
        // join, which is the shape the representation does not carry (decision 048), so it
        // is said and the whole joined row is materialized, as for a selector that names
        // one side.
        if (args.Count == 1)
        {
            var reach = AliasesOf(row);
            reach.Add(rightAlias);
            ReportRowsLeftOut(reach, AliasesOf(joined), "SelectMany() without a result selector", $"materializes the rows of '{written}' alone and leaves out");
            row = joined;
            return;
        }

        ReadResultSelector(selector, joined, "SelectMany()");
    }

    private sealed record AssociationJoin(Relation Relation, EntityRow Owner, EntityMap Target);

    /// <summary>
    /// The relation a navigation path names, or null with the sentence that says what the
    /// maps of the conversion are missing (decision 101) - the same four sentences the HQL
    /// and JPQL parsers say, because the maps are the same and so is what is missing. The
    /// path starts at the lambda parameter, goes through the members of the joined row where
    /// there is one, and ends with the one navigation; a longer one crosses an intermediate
    /// association the model does not carry as a path. A many-to-many relation stands on its
    /// junction entity (decision 005), so a path across it would be two joins and an invented
    /// alias - a stated limit, refused by name.
    /// </summary>
    private AssociationJoin? ResolveAssociation(List<string> path, string written, out string? failure)
    {
        const string consequence = "a query emitted without its join would return different rows; no artifact was generated.";

        // Through the joined row to the entity row the navigation is written from.
        RowShape? shape = row;
        var index = 1;
        while (index < path.Count - 1 && shape is JoinedRow joinedRow && joinedRow.Members.TryGetValue(path[index], out var member))
        {
            shape = member;
            index++;
        }

        if (shape is not EntityRow owner)
        {
            failure = $"The join along the association path '{written}' does not start from a row of an entity in scope, so no relation was there to derive the join condition from; {consequence}";
            return null;
        }

        if (path.Count - index > 1)
        {
            failure = $"The join along the path '{written}' crosses more than one association, and exactly one association is read from a row; {consequence}";
            return null;
        }

        var navigation = path[^1];
        if (owner.Map is null)
        {
            failure = $"The join along the association path '{written}' needs the mapping of the entity behind '{owner.Alias}', which is not part of the conversion, so no relation was there to derive the join condition from; {consequence}";
            return null;
        }

        var relation = owner.Map.Relations.FirstOrDefault(r =>
            string.Equals(r.SourceNavigationProperty, navigation, StringComparison.OrdinalIgnoreCase));
        if (relation is null)
        {
            failure = $"The join along the association path '{written}' names no association the mapping of '{owner.Map.Entity.Name}' declares, so no relation was there to derive the join condition from; {consequence}";
            return null;
        }

        if (relation.Cardinality == Cardinality.ManyToMany)
        {
            failure = $"The join along the association path '{written}' crosses the many-to-many relation to '{relation.TargetEntity}', which would take two joins over its junction entity, and that is not derived; {consequence}";
            return null;
        }

        var target = entityMaps?.FirstOrDefault(m =>
            string.Equals(m.Entity?.Name, relation.TargetEntity, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            failure = $"The join along the association path '{written}' leads to the entity '{relation.TargetEntity}', which is not part of the conversion; {consequence}";
            return null;
        }

        if (relation.ColumnPairs.Count == 0)
        {
            failure = $"The join along the association path '{written}' has no foreign key columns to derive its condition from: the relation to '{relation.TargetEntity}' states none and the database catalog supplied none; {consequence}";
            return null;
        }

        failure = null;
        return new AssociationJoin(relation, owner, target);
    }

    /// <summary>
    /// FK(left) = PK(right) over the column pairs, in their order (decision 101). Which alias
    /// holds the foreign key follows the role of the relation: the entity behind the path
    /// for an owning one, the joined entity for an inverse one - and a collection navigation
    /// is the inverse side, so the key sits on the joined entity. The same derivation the
    /// JPQL parser makes; it is written here again because this layer reads C# and knows no
    /// JPA (S1).
    /// </summary>
    private static ConditionNode AssociationCondition(Relation relation, string pathAlias, string joinAlias)
    {
        var (keyHolder, referenced) = relation.Role == RelationRole.Owning
            ? (pathAlias, joinAlias)
            : (joinAlias, pathAlias);

        var conjuncts = relation.ColumnPairs
            .Select(pair => (ConditionNode)new ComparisonCondition(
                QueryOperand.Column(keyHolder, pair.Source.ColumnName ?? pair.Source.Property.Name),
                ComparisonOperator.Equal,
                QueryOperand.Column(referenced, pair.Target.ColumnName ?? pair.Target.Property.Name)))
            .ToList();

        return conjuncts.Count == 1 ? conjuncts[0] : new LogicalCondition(LogicalOperator.And, conjuncts);
    }

    /// <summary>
    /// The alias of the joined table: the name the result selector gives the joined row -
    /// <c>(ol, o) =&gt; …</c> names it o, and o is what the steps after the join then write
    /// - unless the scope has that alias already, and the table's own name otherwise, made
    /// unique the same way. Until the selector was read the alias was always the table's,
    /// so a source that wrote <c>o.CustomerId</c> came out as <c>customerorders.CustomerId</c>.
    /// </summary>
    private string JoinedAlias(ParenthesizedLambdaExpressionSyntax? selector, string rightTable)
    {
        var taken = AliasesInScope();

        if (selector?.ParameterList.Parameters.Count == 2)
        {
            var named = selector.ParameterList.Parameters[1].Identifier.Text;
            if (!taken.Contains(named))
            {
                return named;
            }
        }

        var derived = (rightTable.Split('.').LastOrDefault() ?? rightTable).ToLowerInvariant();
        var candidate = derived;
        for (int n = 2; taken.Contains(candidate); n++)
        {
            candidate = derived + n;
        }

        return candidate;
    }

    /// <summary>
    /// Reads the fourth argument of a join, the result selector, which says what the joined
    /// row carries from here on. Both rows under whatever names - <c>(ol, o) =&gt; new { ol, o
    /// }</c>, or <c>(x, a) =&gt; new { x.ol, x.o, a }</c> after a second join, which is the
    /// shape the EF Core builder writes - is the whole joined row, exactly what the
    /// representation materializes for a join with no projection, so nothing is recorded
    /// and only the row is shaped for the steps that follow. Columns in the selector are the
    /// projection of the query. A row left out, or a whole row beside columns, is a shape
    /// the representation does not carry: the row set is the same, the columns are not, so
    /// it is a loss with the artifact (decision 070), and it is said (decision 048).
    /// </summary>
    private void ReadResultSelector(ParenthesizedLambdaExpressionSyntax? selector, EntityRow joined, string what)
    {
        if (selector is null
            || selector.ParameterList.Parameters.Count != 2
            || selector.Body is not ExpressionSyntax body)
        {
            Report(
                ConversionRecordKind.Loss,
                $"The result selector of the {what} is not a lambda over the two rows, so the shape it materializes was not read; the whole joined row is materialized instead.",
                QueryFeature.Projection);
            row = new JoinedRow(new Dictionary<string, RowShape>(StringComparer.Ordinal));
            return;
        }

        var bindings = new Dictionary<string, RowShape>(StringComparer.Ordinal)
        {
            [selector.ParameterList.Parameters[0].Identifier.Text] = row,
            [selector.ParameterList.Parameters[1].Identifier.Text] = joined,
        };

        var reach = AliasesOf(row);
        reach.Add(joined.Alias);

        row = ReadMaterializedShape(
            body,
            (ExpressionSyntax expression, out RowShape? endsOnRow, out (string Alias, string Column)? endsOnColumn)
                => TryResolveBound(expression, bindings, out endsOnRow, out endsOnColumn),
            reach,
            $"result selector of the {what}");
    }

    /// <summary>
    /// Reads what a selector materializes - a result selector, or a Select after a join -
    /// as rows and columns. Rows only, all of them, is the whole joined row under the names
    /// the selector gave, and no projection; columns only is the projection; a row left out
    /// or a row beside columns is reported and the columns still projected. Returns the row
    /// the lambdas after the selector range over: what it composed, or a row with no members
    /// after a projection, because the representation has no clause to point at a projected
    /// member through.
    /// </summary>
    private RowShape ReadMaterializedShape(ExpressionSyntax body, Resolve resolve, HashSet<string> reach, string clause)
    {
        var members = new Dictionary<string, RowShape>(StringComparer.Ordinal);
        var columns = new List<(ExpressionSyntax Expression, string? Alias)>();

        if (body is AnonymousObjectCreationExpressionSyntax anon)
        {
            foreach (var initializer in anon.Initializers)
            {
                var name = initializer.NameEquals?.Name.Identifier.Text ?? PathOf(initializer.Expression)?[^1];
                if (name is not null
                    && resolve(initializer.Expression, out var endsOnRow, out _)
                    && endsOnRow is not null)
                {
                    members[name] = endsOnRow;
                }
                else
                {
                    columns.Add((initializer.Expression, name));
                }
            }
        }
        else if (resolve(body, out var endsOnRow, out _) && endsOnRow is not null)
        {
            ReportRowsLeftOut(reach, AliasesOf(endsOnRow), clause, $"'{body}' materializes one side of the join and leaves out");
            return endsOnRow;
        }
        else
        {
            columns.Add((body, MemberName(body)));
        }

        if (columns.Count == 0)
        {
            var composed = new JoinedRow(members);
            ReportRowsLeftOut(reach, AliasesOf(composed), clause, "leaves out");
            return composed;
        }

        if (members.Count > 0)
        {
            Report(
                ConversionRecordKind.Loss,
                $"The {clause} puts the whole row{(members.Count == 1 ? string.Empty : "s")} {Listed(members.Keys)} beside columns, which is not a shape the query representation carries; {(members.Count == 1 ? "that member was" : "those members were")} dropped and the columns are projected.",
                QueryFeature.Projection);
        }

        foreach (var (expression, alias) in columns)
        {
            EmitProjection(expression, alias, resolve);
        }

        // From here on the lambdas range over the projected rows (decision 112).
        scopeProjected = true;
        return new JoinedRow(new Dictionary<string, RowShape>(StringComparer.Ordinal));
    }

    private void ReportRowsLeftOut(HashSet<string> reach, HashSet<string> carried, string clause, string how)
    {
        var leftOut = reach.Where(alias => !carried.Contains(alias)).ToList();
        if (leftOut.Count == 0)
        {
            return;
        }

        Report(
            ConversionRecordKind.Loss,
            $"The {clause} {how} the row{(leftOut.Count == 1 ? string.Empty : "s")} {Listed(leftOut)} of the joined row, which is not a shape the query representation carries; the whole joined row is materialized instead.",
            QueryFeature.Projection);
    }

    private static string Listed(IEnumerable<string> names)
        => string.Join(", ", names.Select(name => $"'{name}'"));

    /// <summary>
    /// The identifiers of a member path, root first - <c>x.o.PlacedAt</c> gives x, o,
    /// PlacedAt - or null when the expression is not one (a call, a literal, an index).
    /// </summary>
    private static List<string>? PathOf(ExpressionSyntax expression)
    {
        var path = new List<string>();
        var current = expression;

        while (true)
        {
            switch (current)
            {
                case MemberAccessExpressionSyntax member:
                    path.Add(member.Name.Identifier.Text);
                    current = member.Expression;
                    continue;

                case IdentifierNameSyntax identifier:
                    path.Add(identifier.Identifier.Text);
                    path.Reverse();
                    return path;

                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// Follows a member path from a row shape: through the members of a joined row, down to
    /// the row it ends on or to the column of the entity row it ends in. A path that goes
    /// on past an entity row is a navigation, which is read in one place only - as the
    /// collection of a SelectMany (decision 101) - and nowhere as an operand.
    /// </summary>
    private static bool TryWalk(
        RowShape shape,
        List<string> path,
        int from,
        out RowShape? endsOnRow,
        out (string Alias, string Column)? endsOnColumn)
    {
        endsOnRow = null;
        endsOnColumn = null;
        var index = from;

        while (true)
        {
            var remaining = path.Count - index;
            switch (shape)
            {
                case EntityRow entity when remaining == 0:
                    endsOnRow = entity;
                    return true;

                case EntityRow entity when remaining == 1:
                    endsOnColumn = (entity.Alias, path[index]);
                    return true;

                case JoinedRow joined when remaining == 0:
                    endsOnRow = joined;
                    return true;

                case JoinedRow joined when joined.Members.TryGetValue(path[index], out var member):
                    shape = member;
                    index++;
                    continue;

                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Resolves a path whose root is a parameter the caller has bound to a row - the two
    /// parameters of a result selector.
    /// </summary>
    private static bool TryResolveBound(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, RowShape> bindings,
        out RowShape? endsOnRow,
        out (string Alias, string Column)? endsOnColumn)
    {
        endsOnRow = null;
        endsOnColumn = null;

        return PathOf(expression) is { Count: > 0 } path
            && bindings.TryGetValue(path[0], out var shape)
            && TryWalk(shape, path, 1, out endsOnRow, out endsOnColumn);
    }

    /// <summary>
    /// Resolves a path written in a lambda whose parameter is not tracked - every step after
    /// the join - against the joined row of this scope, then of the enclosing ones (a
    /// correlated reference, decision 061). The first identifier is the parameter, whatever
    /// it is called, and only a path with a member of the joined row between it and the
    /// column is resolved here: a one-member path keeps the reading it always had, in which
    /// the parameter name is the alias, because that is what a correlated reference to an
    /// outer scope looks like.
    /// </summary>
    private bool TryResolveInScope(
        ExpressionSyntax expression,
        out RowShape? endsOnRow,
        out (string Alias, string Column)? endsOnColumn)
    {
        endsOnRow = null;
        endsOnColumn = null;

        if (PathOf(expression) is not { Count: >= 2 } path)
        {
            return false;
        }

        foreach (var candidate in ScopeRows())
        {
            if (candidate is JoinedRow joined && joined.Members.ContainsKey(path[1]))
            {
                return TryWalk(joined, path, 1, out endsOnRow, out endsOnColumn);
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the expression names a column of a row in scope: a member of the alias of
    /// the scope's own row (<c>p.ProductName</c>) or a member reached through a joined row
    /// (<c>x.o.PlacedAt</c>). A lambda parameter is never the context, so the two-identifier
    /// shape the root recognizer accepts is a column here.
    /// </summary>
    private bool IsColumnOfScope(ExpressionSyntax expression)
        => (expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax head }
            && AliasesInScope().Contains(head.Identifier.Text))
           || (TryResolveInScope(expression, out _, out var column) && column is not null);

    private IEnumerable<RowShape> ScopeRows()
    {
        yield return row;

        for (int i = enclosingRows.Count - 1; i >= 0; i--)
        {
            yield return enclosingRows[i];
        }
    }

    private HashSet<string> AliasesInScope()
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { sourceAlias };
        foreach (var shape in ScopeRows())
        {
            Collect(shape, aliases);
        }

        return aliases;
    }

    private static HashSet<string> AliasesOf(RowShape shape)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(shape, aliases);
        return aliases;
    }

    private static void Collect(RowShape shape, HashSet<string> into)
    {
        switch (shape)
        {
            case EntityRow entity:
                into.Add(entity.Alias);
                break;

            case JoinedRow joined:
                foreach (var member in joined.Members.Values)
                {
                    Collect(member, into);
                }

                break;
        }
    }

    /// <summary>
    /// Simple keys (ol =&gt; ol.OrderId) yield one equality; composite keys expressed with
    /// anonymous types are paired positionally into an AND of several equalities. The outer
    /// key ranges over the row as the joins so far shaped it, so <c>x =&gt; x.o.OrderId</c>
    /// joins on the table an earlier join brought in; the inner key always ranges over the
    /// table being joined.
    /// </summary>
    private ConditionNode? BuildJoinCondition(
        ExpressionSyntax outerBody,
        ExpressionSyntax innerBody,
        string leftAlias,
        string rightAlias)
    {
        if (outerBody is AnonymousObjectCreationExpressionSyntax outerAnon
            && innerBody is AnonymousObjectCreationExpressionSyntax innerAnon)
        {
            if (outerAnon.Initializers.Count == 0
                || outerAnon.Initializers.Count != innerAnon.Initializers.Count)
            {
                return null;
            }

            var equalities = new List<ConditionNode>();
            for (int i = 0; i < outerAnon.Initializers.Count; i++)
            {
                var outerKey = outerAnon.Initializers[i].Expression;
                var left = MemberName(outerKey);
                var right = MemberName(innerAnon.Initializers[i].Expression);
                if (left is null || right is null)
                {
                    return null;
                }

                equalities.Add(new ComparisonCondition(
                    QueryOperand.Column(OuterKeyAlias(outerKey, leftAlias), left),
                    ComparisonOperator.Equal,
                    QueryOperand.Column(rightAlias, right)));
            }

            return equalities.Count == 1
                ? equalities[0]
                : new LogicalCondition(LogicalOperator.And, equalities);
        }

        var outerName = MemberName(outerBody);
        var innerName = MemberName(innerBody);
        if (outerName is null || innerName is null)
        {
            return null;
        }

        return new ComparisonCondition(
            QueryOperand.Column(OuterKeyAlias(outerBody, leftAlias), outerName),
            ComparisonOperator.Equal,
            QueryOperand.Column(rightAlias, innerName));
    }

    private string OuterKeyAlias(ExpressionSyntax outerKey, string leftAlias)
        => TryResolveInScope(outerKey, out _, out var column) && column is { } resolved
            ? resolved.Alias
            : leftAlias;

    private void HandleSelect(InvocationExpressionSyntax node)
    {
        if (!TryReadLambdaBody(node, out var body))
        {
            Report(ConversionRecordKind.Loss, "A Select() argument was not a lambda and was dropped.", QueryFeature.Projection);
            return;
        }

        switch (body)
        {
            // Select(c => c) materializes the whole entity: rule Q3's default, so no
            // projection instruction is recorded.
            case IdentifierNameSyntax:
                return;

            // The members may be columns, or after a join the rows of the joined result
            // (Select(x => new { x.ol, x.o }) is that whole row under its names, as much
            // as Select(x => x) is), or a mix, which is read the way a result selector is.
            case AnonymousObjectCreationExpressionSyntax anon:
                row = ReadMaterializedShape(anon, TryResolveInScope, AliasesOf(row), "projection");
                return;

            // Select(c => c.Name) is a projection of one column - the commonest shape there
            // is, and one an earlier version dropped entirely. Select(x => x.o) after a join
            // is one side of the joined row, which goes the same way as in a selector.
            case MemberAccessExpressionSyntax member:
                row = ReadMaterializedShape(member, TryResolveInScope, AliasesOf(row), "projection");
                return;

            default:
                Report(
                    ConversionRecordKind.Loss,
                    $"The projection '{body}' is not a shape the query representation carries; the whole entity is materialized instead.",
                    QueryFeature.Projection);
                return;
        }
    }

    private void EmitProjection(ExpressionSyntax expression, string? alias, Resolve? resolve = null)
    {
        if (alias is not null)
        {
            scopeProjections.Add(alias);
        }

        if (TryReadAggregate(expression, out var aggregate))
        {
            queryBuilder.Project(aggregate!, alias);
            return;
        }

        if (NamesTheGroupingKey(expression))
        {
            if (ResolveGroupingKey(expression) is { } key)
            {
                queryBuilder.Project(key.Table, key.Attribute, alias);
                return;
            }

            Report(
                ConversionRecordKind.Loss,
                $"The projected expression '{expression}' names the grouping key, which this query does not group by in a shape the representation can point at; the column was dropped.",
                QueryFeature.Projection);
            return;
        }

        // A path the rows in scope resolve - ol.Description inside a result selector,
        // x.o.PlacedAt after a join - says which table the column belongs to.
        Resolve resolver = resolve ?? TryResolveInScope;
        if (resolver(expression, out _, out var resolved))
        {
            if (resolved is { } column)
            {
                queryBuilder.Project(column.Alias, column.Column, alias);
                return;
            }

            Report(
                ConversionRecordKind.Loss,
                $"The projected expression '{expression}' names a whole row of the joined result, which is not a shape the query representation carries; the member was dropped.",
                QueryFeature.Projection);
            return;
        }

        // A column of the row, or an expression over it (decision 107). A bare constant is
        // not a shape any target names a column by and stays the loss it was; a construct
        // outside the vocabulary is dropped with a record that names it.
        var operand = ReadOperand(expression);
        if (operand is null || (operand.IsConstant && !operand.IsAggregate) || operand.IsParameter || operand.IsValueList)
        {
            var (what, category) = unread ?? ($"'{expression}'", QueryFeature.Projection);
            unread = null;
            Report(
                ConversionRecordKind.Loss,
                $"The projected expression {what} is not a column, an aggregate or an expression the query representation carries, and was dropped.",
                category ?? QueryFeature.Projection);
            return;
        }

        queryBuilder.Project(operand, alias);
    }

    /// <summary>
    /// An ordering key (decision 107): the grouping key, an aggregate of the group
    /// (<c>OrderByDescending(g =&gt; g.Count())</c>), a column, or an expression over the row.
    /// </summary>
    private void HandleOrderBy(InvocationExpressionSyntax node, bool asc)
    {
        if (!TryReadLambdaBody(node, out var body))
        {
            Report(ConversionRecordKind.Loss, "An ordering argument was not a lambda and was dropped.", QueryFeature.Ordering);
            return;
        }

        // After the projection the lambda ranges over the projected rows, and a member of
        // them is a column the projection named: an ordering by that alias (decision 073).
        // Any other key over them reads the scope as an intermediate result before it comes
        // here (decision 112).
        if (scopeProjected
            && body is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax } projected
            && scopeProjections.Contains(projected.Name.Identifier.Text, StringComparer.Ordinal))
        {
            queryBuilder.OrderBy(null, projected.Name.Identifier.Text, asc);
            return;
        }

        if (NamesTheGroupingKey(body!) && ResolveGroupingKey(body!) is { } groupingKey)
        {
            queryBuilder.OrderBy(groupingKey.Table, groupingKey.Attribute, asc);
            return;
        }

        if (TryReadAggregate(body!, out var aggregate))
        {
            queryBuilder.OrderBy(aggregate!, asc);
            return;
        }

        var key = ReadOperand(body!);
        if (key is null || key.IsConstant || key.IsParameter || key.IsValueList)
        {
            var (what, category) = unread ?? ($"'{body}'", QueryFeature.Ordering);
            unread = null;
            Report(
                ConversionRecordKind.Loss,
                $"The ordering key {what} is not a column, an aggregate or an expression the query representation carries, and was dropped.",
                category ?? QueryFeature.Ordering);
            return;
        }

        queryBuilder.OrderBy(key, asc);
    }

    private void HandleGroupBy(InvocationExpressionSyntax node)
    {
        // Grouping decides which rows come back, so a key left out is a different query
        // (decision 070); a key inside an anonymous type used to vanish without a record.
        if (!TryReadLambdaBody(node, out var body))
        {
            Report(
                ConversionRecordKind.Failure,
                "A GroupBy() whose argument is not a lambda cannot be read, and a query grouped differently would return different rows; no artifact was generated.",
                QueryFeature.Grouping);
            return;
        }

        // GroupBy(key, element) and GroupBy(key, (key, group) => …) group the same rows;
        // what the second lambda changes is what the steps after the grouping range over,
        // which the representation has no place for. The groups are the same, so it is a
        // loss with the artifact (decision 070), said rather than skipped (decision 048). The
        // first shape is what `group c.Name by c.City` rewrites to (decision 103).
        if (node.ArgumentList.Arguments.Count > 1)
        {
            Report(
                ConversionRecordKind.Loss,
                "Only the key selector of GroupBy() was read; its further argument - an element or a result selector - has no place in the query representation, and the steps after the grouping range over the whole rows of each group.",
                QueryFeature.Grouping);
        }

        if (body is AnonymousObjectCreationExpressionSyntax anon)
        {
            foreach (var initializer in anon.Initializers)
            {
                var key = MemberName(initializer.Expression);
                if (key is null || IsExpression(initializer.Expression))
                {
                    RefuseGroupingKey(initializer.Expression);
                    continue;
                }

                RecordGroupingKey(
                    AliasOf(initializer.Expression),
                    key,
                    initializer.NameEquals?.Name.Identifier.Text ?? key);
            }

            return;
        }

        var name = MemberName(body!);
        if (name is null || IsExpression(body!))
        {
            RefuseGroupingKey(body!);
            return;
        }

        RecordGroupingKey(AliasOf(body!), name, name);
    }

    private void RecordGroupingKey(string table, string attribute, string name)
    {
        queryBuilder.GroupBy(table, attribute);
        groupingKeys.Add(new GroupingKey(table, attribute, name));
    }

    /// <summary>Whether the expression reads as a computed value rather than as a column - <c>c.Name.Length</c> has a member name and is no column.</summary>
    private bool IsExpression(ExpressionSyntax expression)
    {
        var read = ReadOperand(expression);
        unread = null;
        return read is { IsExpression: true };
    }

    /// <summary>
    /// A grouping key the representation does not carry (decision 070): a grouping by an
    /// expression is the one position the expression does not take (decision 107), and is
    /// refused under its own category.
    /// </summary>
    private void RefuseGroupingKey(ExpressionSyntax key)
    {
        if (IsExpression(key))
        {
            Report(
                ConversionRecordKind.Failure,
                $"The grouping key '{key}' is an expression, which the query representation carries in every position but the grouping, and a query grouped differently would return different rows; no artifact was generated.",
                QueryFeature.Expression);
            return;
        }

        Report(
            ConversionRecordKind.Failure,
            $"The grouping key '{key}' is not a column reference, and a query grouped differently would return different rows; no artifact was generated.",
            QueryFeature.Grouping);
    }

    /// <summary>
    /// Whether the expression reaches for the grouping key rather than for a column of the
    /// source: <c>g.Key</c>, or <c>g.Key.Part</c> for a key of several columns. It is the
    /// spelling LINQ gives the key of a group and the one the EF Core builder emits, so the
    /// parser has to recognise it for the round trip to close.
    ///
    /// Two things keep an entity whose column happens to be called Key out of this: the scope
    /// has to have grouped at all, and the qualifier has to be something other than the source
    /// alias - after a GroupBy the lambda ranges over the group, so a row's own column is not
    /// reachable under that name.
    /// </summary>
    private bool NamesTheGroupingKey(ExpressionSyntax expression)
        => groupingKeys.Count > 0
           && ReachesForKey(expression)
           && !string.Equals(RootIdentifier(expression), sourceAlias, StringComparison.Ordinal);

    private static bool ReachesForKey(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Key" } => true,
        MemberAccessExpressionSyntax member => ReachesForKey(member.Expression),
        _ => false,
    };

    private static string? RootIdentifier(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax member => RootIdentifier(member.Expression),
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        _ => null,
    };

    /// <summary>
    /// The column of the grouping the expression names, or null when this scope groups by
    /// several columns and the expression does not say which - <c>g.Key</c> over a composite
    /// key is the key object itself, which no clause of the representation points at.
    /// </summary>
    private GroupingKey? ResolveGroupingKey(ExpressionSyntax expression)
    {
        // g.Key.Part names one column of a composite key by the name the grouping gave it.
        if (expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: not "Key" } part
            && part.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Key" })
        {
            var named = part.Name.Identifier.ValueText;
            foreach (var key in groupingKeys)
            {
                if (string.Equals(key.Name, named, StringComparison.Ordinal))
                {
                    return key;
                }
            }

            return null;
        }

        return groupingKeys.Count == 1 ? groupingKeys[0] : null;
    }

    private void HandleHaving(InvocationExpressionSyntax node)
    {
        if (!TryReadLambdaBody(node, out var body) || body is not BinaryExpressionSyntax binary)
        {
            Report(
                ConversionRecordKind.Failure,
                "A post-aggregation filter that is not a simple comparison cannot be read, and a query emitted without it would return different rows; no artifact was generated.",
                QueryFeature.PostAggregationFiltering);
            return;
        }

        var op = MapOperator(binary.Kind());
        var left = ReadHavingOperand(binary.Left);
        var right = ReadHavingOperand(binary.Right);

        if (op is null || left is null || right is null)
        {
            Refuse($"post-aggregation filter '{binary}'", "a query emitted without it would return different rows", QueryFeature.PostAggregationFiltering);
            return;
        }

        queryBuilder.Having(new ComparisonCondition(left, op.Value, right));
    }

    private QueryOperand? ReadHavingOperand(ExpressionSyntax expression)
    {
        if (TryReadAggregate(expression, out var aggregate))
        {
            return aggregate;
        }

        return ReadOperand(expression);
    }

    /// <summary>
    /// Reads g.Sum(x =&gt; x.Total), g.Count() and their kin as the aggregate operand of the
    /// group. The element the lambda ranges over is a row of the source, so its columns are
    /// qualified by the source alias rather than by the lambda's own parameter name, which is
    /// not a table alias at all; the body may be a column or, since decision 107, an
    /// expression over the element (<c>g.Sum(x =&gt; x.Price * x.Quantity)</c>).
    ///
    /// The receiver may also be <c>g.Select(x =&gt; x.Total)</c>, optionally followed by
    /// <c>.Distinct()</c>, with the aggregate then taking no lambda: the column comes from
    /// the Select, and the Distinct() between them is the modifier of the aggregate
    /// (decision 102) - <c>g.Select(x =&gt; x.Id).Distinct().Count()</c> is
    /// <c>COUNT(DISTINCT Id)</c>, which is the shape EF Core translates it to.
    /// </summary>
    private bool TryReadAggregate(ExpressionSyntax expression, out QueryOperand? aggregate)
    {
        aggregate = null;

        if (expression is not InvocationExpressionSyntax invocation
            || invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return false;
        }

        var name = member.Name.Identifier.Text.ToUpperInvariant();
        if (name is not ("COUNT" or "SUM" or "MIN" or "MAX" or "AVG"))
        {
            return false;
        }

        // The receiver has to be a bare identifier - the grouping's own lambda parameter -
        // or a one-column Select over it, optionally collapsed. An aggregate whose receiver
        // is a chain over a query root is a scalar subquery, not a group aggregate, and
        // belongs to ReadScalarSubQuery (decision 061).
        var distinct = false;
        SimpleLambdaExpressionSyntax? selector = null;
        var receiver = member.Expression;
        if (receiver is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Distinct" } collapse } collapsed
            && collapsed.ArgumentList.Arguments.Count == 0)
        {
            distinct = true;
            receiver = collapse.Expression;
        }

        if (receiver is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Select" } select } projection
            && select.Expression is IdentifierNameSyntax
            && projection.ArgumentList.Arguments.Count == 1
            && projection.ArgumentList.Arguments[0].Expression is SimpleLambdaExpressionSyntax selected
            && invocation.ArgumentList.Arguments.Count == 0)
        {
            selector = selected;
        }
        else if (receiver is not IdentifierNameSyntax || distinct)
        {
            return false;
        }

        var argument = invocation.ArgumentList.Arguments.FirstOrDefault();
        if (argument is null && selector is null)
        {
            // g.Count() counts rows, not a column.
            aggregate = QueryOperand.Column(sourceAlias, "*", name);
            return true;
        }

        var lambda = selector ?? argument!.Expression as SimpleLambdaExpressionSyntax;
        if (lambda?.Body is not ExpressionSyntax body)
        {
            return false;
        }

        // The lambda's parameter is the element, a row of the source: while the body is
        // read, its name stands for the source alias.
        var element = lambda.Parameter.Identifier.Text;
        var shadowed = aliasSubstitutions.TryGetValue(element, out var previous);
        aliasSubstitutions[element] = sourceAlias;
        QueryOperand? ranged;
        try
        {
            ranged = ReadLeaf(body);
        }
        finally
        {
            if (shadowed)
            {
                aliasSubstitutions[element] = previous!;
            }
            else
            {
                aliasSubstitutions.Remove(element);
            }
        }

        if (ranged is null)
        {
            return false;
        }

        if (ranged.IsColumn)
        {
            aggregate = QueryOperand.Column(ranged.Table ?? sourceAlias, ranged.Property!, name, distinct);
            return true;
        }

        if (ranged.IsExpression)
        {
            aggregate = QueryOperand.Computed(ranged.Expression!, name, distinct);
            return true;
        }

        return false;
    }

    private ConditionNode? ParseCondition(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return ParseCondition(parenthesized.Expression);

            case PrefixUnaryExpressionSyntax unary when unary.IsKind(SyntaxKind.LogicalNotExpression):
                {
                    var operand = ParseCondition(unary.Operand);
                    return operand is null ? null : new NotCondition(operand);
                }

            case BinaryExpressionSyntax logical when logical.IsKind(SyntaxKind.LogicalAndExpression)
                                                  || logical.IsKind(SyntaxKind.LogicalOrExpression):
                {
                    var op = logical.IsKind(SyntaxKind.LogicalAndExpression)
                        ? LogicalOperator.And
                        : LogicalOperator.Or;

                    var left = ParseCondition(logical.Left);
                    var right = ParseCondition(logical.Right);
                    if (left is null || right is null)
                    {
                        return null;
                    }

                    // Chains of the same operator (a && b && c) are flattened into one node.
                    var operands = new List<ConditionNode>();
                    Flatten(left, op, operands);
                    Flatten(right, op, operands);
                    return new LogicalCondition(op, operands);
                }

            case BinaryExpressionSyntax comparison:
                return ParseComparison(comparison);

            case InvocationExpressionSyntax invocation:
                // The subquery, list and collection-parameter shapes first: they decline a
                // receiver that is none of theirs without a word, and only then is a string
                // method over a column what remains to be read.
                return ReadSubQueryCondition(invocation) ?? ReadPatternCondition(invocation);

            default:
                return null;
        }
    }

    /// <summary>
    /// The canonical escape character of a pattern this parser has to escape itself. A
    /// LINQ string method states no escape - the provider chooses one when it translates -,
    /// so the reader chooses this one, and escapes it with itself where the core carries it.
    /// One character for every query, because the same call has to read to the same text
    /// (S2), and one every SQL-shaped target accepts as a literal.
    /// </summary>
    private const char PatternEscape = '!';

    /// <summary>
    /// Reads a string method over a column as a LIKE comparison - the table of decision 051
    /// inverted: <c>StartsWith("x")</c> is <c>x%</c>, <c>EndsWith("x")</c> is <c>%x</c>,
    /// <c>Contains("x")</c> over a column is <c>%x%</c> -, and the provider's own pattern
    /// function as LIKE with the pattern as written. Until these were read, the filter around
    /// them stayed unread and decision 070 refused the whole artifact, so EF Core was a source
    /// of no pattern and its own StartsWith did not survive the identity direction.
    ///
    /// The argument of a string method has to be a literal: a value from the enclosing scope
    /// would make the pattern that value joined with a wildcard, which is an expression the
    /// condition tree has no operand for (decision 083 carries a parameter, not a
    /// concatenation) - reading it as the bare parameter would match other rows, so it stays
    /// unread by name. Where the provider escapes the argument (decision 102 gave the escape
    /// a place), a wildcard in the literal is a literal character and goes out escaped; a
    /// character class opener goes with them, because the SQL Server dialect reads it as one.
    /// The provider's pattern function takes a literal or a parameter as the pattern, and an
    /// escape that is a literal - one that is not is refused, as the SQL and JPQL readers
    /// refuse <c>ESCAPE @e</c>.
    /// </summary>
    private ConditionNode? ReadPatternCondition(InvocationExpressionSyntax invocation)
    {
        if (TryReadProviderPatternFunction(invocation, out var columnExpression, out var patternExpression, out var escapeExpression))
        {
            var column = ReadOperand(columnExpression!);
            if (column is null || !column.IsColumn || column.Function is not null)
            {
                unread ??= ($"'{columnExpression}' as the first argument of the pattern function, which is not a column", null);
                return null;
            }

            // The pattern is a literal, a value from the enclosing scope, or since decision
            // 107 an expression the caller composed - prefix + "%" -, which goes out as
            // written: the caller made the pattern, so a wildcard in the value is a wildcard.
            var pattern = ReadLeaf(patternExpression!);
            if (pattern is null || pattern.Function is not null || !(pattern.IsConstant || pattern.IsParameter || pattern.IsExpression))
            {
                unread ??= ($"'{patternExpression}' as the pattern of the pattern function, which is neither a literal, a value from the enclosing scope nor an expression", null);
                return null;
            }

            if (pattern.IsConstant && pattern.Constant!.Type is not (ScalarType.String or ScalarType.Char))
            {
                unread ??= ($"'{patternExpression}' as the pattern of the pattern function, which is not a string", null);
                return null;
            }

            string? escape = null;
            if (escapeExpression is not null)
            {
                var escapeConstant = escapeExpression is LiteralExpressionSyntax escapeLiteral ? ReadConstant(escapeLiteral) : null;
                if (escapeConstant?.Type is not (ScalarType.String or ScalarType.Char))
                {
                    unread ??= ($"'{escapeExpression}' as the escape of the pattern function, which is not a string literal - the escape is a fact about reading the pattern, not a value from the caller", null);
                    return null;
                }

                escape = escapeConstant.Text;
            }

            return new ComparisonCondition(column, ComparisonOperator.Like, pattern, Escape: escape);
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return null;
        }

        var (leading, trailing) = member.Name.Identifier.Text switch
        {
            "StartsWith" => (false, true),
            "EndsWith" => (true, false),
            "Contains" => (true, true),
            _ => (false, false),
        };

        if (!leading && !trailing)
        {
            return null;
        }

        var receiver = ReadOperand(member.Expression);
        if (receiver is null || !receiver.IsColumn || receiver.Function is not null)
        {
            return null;
        }

        var method = member.Name.Identifier.Text;
        if (invocation.ArgumentList.Arguments.Count != 1)
        {
            unread ??= ($"{method}() with {invocation.ArgumentList.Arguments.Count} arguments, an overload the provider does not translate to a pattern", null);
            return null;
        }

        var argument = invocation.ArgumentList.Arguments[0].Expression;

        if (argument is LiteralExpressionSyntax literal)
        {
            var core = ReadConstant(literal);
            if (core.Type is not (ScalarType.String or ScalarType.Char))
            {
                unread ??= ($"{method}() with '{argument}', which is not a string literal", null);
                return null;
            }

            var (text, escape2) = ProviderEscapesStringMethodArguments
                ? EscapeCore(core.Text)
                : (core.Text, (string?)null);

            var likePattern = (leading ? "%" : string.Empty) + text + (trailing ? "%" : string.Empty);

            return new ComparisonCondition(
                receiver,
                ComparisonOperator.Like,
                QueryOperand.Value(QueryConstant.Of(likePattern, ScalarType.String)),
                Escape: escape2);
        }

        // An argument that is not a literal - a value from the enclosing scope, a column, an
        // expression - is the pattern that value joined with the wildcard (decision 107):
        // LIKE over a concatenation. Where the provider escapes the argument at run time (EF
        // Core), the value goes in as EscapePattern under the canonical escape, so that a
        // wildcard in it stays literal in every target as it does in the source (decisions
        // 053 and 065); NHibernate's provider concatenates as written, and so does the model.
        var bound = ReadLeaf(argument);
        if (bound is null)
        {
            unread ??= ($"{method}() with '{argument}', which is neither a string literal nor a value the query representation carries", null);
            return null;
        }

        QueryOperand value = ProviderEscapesStringMethodArguments
            ? QueryOperand.Computed(QueryExpression.Call(QueryFunction.EscapePattern, [bound]))
            : bound;

        var wildcard = QueryOperand.Value(QueryConstant.Of("%", ScalarType.String));
        var anchored = value;
        if (leading)
        {
            anchored = QueryOperand.Computed(QueryExpression.Binary(ExpressionOperator.Concat, wildcard, anchored));
        }

        if (trailing)
        {
            anchored = QueryOperand.Computed(QueryExpression.Binary(ExpressionOperator.Concat, anchored, wildcard));
        }

        return new ComparisonCondition(
            receiver,
            ComparisonOperator.Like,
            anchored,
            Escape: ProviderEscapesStringMethodArguments ? PatternEscape.ToString() : null);
    }

    /// <summary>
    /// The core of a pattern as the provider would escape it: every wildcard the SQL Server
    /// dialect knows (<c>%</c>, <c>_</c>, <c>[</c>) behind the canonical escape, and the escape
    /// behind itself, so that the LINQ target's split (decision 051) reads the same core back.
    /// A core without a wildcard needs no escape and gets none - the text is what the source
    /// wrote, and a stray escape character in it is a literal without a clause.
    /// </summary>
    private static (string Text, string? Escape) EscapeCore(string core)
    {
        if (core.IndexOfAny(['%', '_', '[']) < 0)
        {
            return (core, null);
        }

        var escaped = new StringBuilder(core.Length * 2);
        foreach (var character in core)
        {
            if (character is '%' or '_' or '[' or PatternEscape)
            {
                escaped.Append(PatternEscape);
            }

            escaped.Append(character);
        }

        return (escaped.ToString(), PatternEscape.ToString());
    }

    /// <summary>
    /// Reads Contains and Any over a query root as a subquery condition (decision 061):
    /// <c>chain.Select(x =&gt; x.Col).Contains(value)</c> is IN,
    /// <c>chain.Any()</c> is EXISTS and <c>chain.Any(predicate)</c> is
    /// <c>Where(predicate).Any()</c> - the Any invocation itself has exactly the one-lambda
    /// shape the Where handler reads. A Contains whose receiver is an inline collection of
    /// literals - <c>new[] { 1, 2, 3 }</c>, <c>new int[] { … }</c>, <c>new List&lt;int&gt;
    /// { … }</c> - is IN over a list of values (decision 074); a receiver that is a bare
    /// identifier is a collection from the enclosing scope, which is a collection parameter
    /// (decision 083). Any other receiver - a member access, a string - is no subquery and
    /// no list and stays unread.
    /// </summary>
    private ConditionNode? ReadSubQueryCondition(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return null;
        }

        switch (member.Name.Identifier.Text)
        {
            case "Contains":
                {
                    // A receiver that is a column of a row in scope - p.ProductName - is a
                    // string and Contains over it is a pattern (ReadPatternCondition), not
                    // the head of a chain, whatever the root recognizer would make of the
                    // same two-identifier shape.
                    if (IsColumnOfScope(member.Expression))
                    {
                        return null;
                    }

                    QueryOperand? right;
                    if (TryDecompose(member.Expression, out var root, out var steps))
                    {
                        right = null;
                    }
                    else if (TryReadInlineCollection(member.Expression, out var values))
                    {
                        if (values is null)
                        {
                            return null;
                        }

                        right = values;
                    }
                    else if (member.Expression is IdentifierNameSyntax collection)
                    {
                        right = QueryOperand.Bound(
                            QueryParameter.Named(collection.Identifier.Text, isCollection: true));
                    }
                    else
                    {
                        return null;
                    }

                    var value = invocation.ArgumentList.Arguments.Count == 1
                        ? ReadOperand(invocation.ArgumentList.Arguments[0].Expression)
                        : null;
                    if (value is null)
                    {
                        return null;
                    }

                    right ??= QueryOperand.Nested(ReadSubQueryOperand(root!, steps));
                    return new ComparisonCondition(value, ComparisonOperator.In, right);
                }

            case "Any":
                {
                    if (!TryDecompose(member.Expression, out var root, out var steps))
                    {
                        return null;
                    }

                    if (invocation.ArgumentList.Arguments.Count == 1)
                    {
                        steps.Add(new ChainStep("Where", invocation));
                    }

                    var sub = ReadSubQueryOperand(root!, steps);
                    return new ComparisonCondition(QueryOperand.Nested(sub), ComparisonOperator.Exists);
                }

            default:
                return null;
        }
    }

    /// <summary>
    /// Reads an inline collection of literals as the values of an IN list (decision 074).
    /// Three C# spellings carry one: an implicit array, a typed array with an initializer,
    /// and an object creation with a collection initializer. A bare identifier among the
    /// elements is a value from the enclosing scope, so a parameter, which stands among the
    /// values since decision 102. Returns false when the expression is none of the three
    /// spellings; returns true with a null operand when it is one but an element sinks it -
    /// a null literal is no value the model carries (decision 002), and an empty
    /// initializer is a predicate no target writes as a filter.
    /// </summary>
    private bool TryReadInlineCollection(ExpressionSyntax expression, out QueryOperand? values)
    {
        values = null;

        var initializer = expression switch
        {
            ImplicitArrayCreationExpressionSyntax implicitArray => implicitArray.Initializer,
            ArrayCreationExpressionSyntax array => array.Initializer,
            ObjectCreationExpressionSyntax creation when creation.Initializer?.IsKind(SyntaxKind.CollectionInitializerExpression) == true
                => creation.Initializer,
            _ => null,
        };

        if (initializer is null)
        {
            return false;
        }

        if (initializer.Expressions.Count == 0)
        {
            unread ??= ("an empty collection as the receiver of Contains, which is a predicate no target writes as a filter", null);
            return true;
        }

        var elements = new List<QueryOperand>(initializer.Expressions.Count);
        foreach (var element in initializer.Expressions)
        {
            if (IsNullLiteral(element))
            {
                unread ??= ("null among the values of an inline collection, which is no value the query representation carries", null);
                return true;
            }

            var operand = ReadOperand(element);
            if (operand is not null && operand.IsParameter)
            {
                elements.Add(operand);
                continue;
            }

            if (operand is null || !operand.IsConstant || operand.Function is not null)
            {
                unread ??= ($"'{element}' among the values of an inline collection, which is not a literal", null);
                return true;
            }

            elements.Add(operand);
        }

        values = QueryOperand.ValueList(elements);
        return true;
    }

    /// <summary>
    /// Reads a nested chain into a subquery operand (decision 061). The scope is closed
    /// with PopOperand, so its instructions become the operand's body rather than
    /// instructions of the enclosing query, and the enclosing source alias survives the
    /// nested source step. The element parameter, when given, names the nested scope
    /// where the chain itself has no lambda to take the name from.
    /// </summary>
    private SubQueryInstruction ReadSubQueryOperand(
        LinqQueryRoot root,
        List<ChainStep> steps,
        Action? beforeClose = null,
        string? elementParameter = null)
    {
        var enclosingAlias = sourceAlias;

        queryBuilder.Push();
        EmitChain(root, steps, beforeClose, elementParameter);
        sourceAlias = enclosingAlias;

        return queryBuilder.PopOperand();
    }

    private static void Flatten(ConditionNode node, LogicalOperator op, List<ConditionNode> into)
    {
        if (node is LogicalCondition logical && logical.Operator == op)
        {
            into.AddRange(logical.Operands);
            return;
        }

        into.Add(node);
    }

    private ConditionNode? ParseComparison(BinaryExpressionSyntax binary)
    {
        var op = MapOperator(binary.Kind());
        if (op is null)
        {
            return null;
        }

        bool leftIsNull = IsNullLiteral(binary.Left);
        bool rightIsNull = IsNullLiteral(binary.Right);

        if (leftIsNull && rightIsNull)
        {
            return null;
        }

        // A comparison with a null literal is normalized to IS NULL / IS NOT NULL
        // (decision 002).
        if (leftIsNull || rightIsNull)
        {
            if (op is not (ComparisonOperator.Equal or ComparisonOperator.NotEqual))
            {
                return null;
            }

            var operand = ReadOperand(leftIsNull ? binary.Right : binary.Left);
            if (operand is null)
            {
                return null;
            }

            return new ComparisonCondition(
                operand,
                op == ComparisonOperator.Equal ? ComparisonOperator.IsNull : ComparisonOperator.IsNotNull);
        }

        var left = ReadOperand(binary.Left);
        var right = ReadOperand(binary.Right);
        if (left is null || right is null)
        {
            return null;
        }

        return new ComparisonCondition(left, op.Value, right);
    }

    /// <summary>
    /// One operand of the six shapes (decisions 024, 061, 074, 083, 107): a column of a row
    /// in scope, a literal, a moment, a terminal aggregate over a chain as a scalar subquery,
    /// a value from the enclosing scope as a parameter, and since decision 107 an expression
    /// - the arithmetic operators, <c>+</c> as a concatenation where a side is a string
    /// literal, <c>??</c>, the conditional operator, the members and methods of System.String
    /// and System.DateTime that the vocabulary names, <c>Math.Abs</c>, <c>DateTime.Now</c> -
    /// and the aggregate of a group over a column or an expression. Null for what the
    /// representation does not carry, with the construct named in <see cref="unread"/>
    /// where the parser can name it.
    /// </summary>
    private QueryOperand? ReadOperand(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return ReadOperand(parenthesized.Expression);

            // DateTime.Now is the current moment; UtcNow is another moment and stays refused
            // by name (decision 107). Before the column reading, which the two-identifier
            // shape would otherwise take for a column Now of a table DateTime.
            case MemberAccessExpressionSyntax { Name.Identifier.Text: "Now" or "UtcNow" } clock
                when WrittenName(clock.Expression) is "DateTime" or "System.DateTime" or "global::System.DateTime":
                if (clock.Name.Identifier.Text == "UtcNow")
                {
                    unread ??= ("DateTime.UtcNow, which is another moment than the current timestamp of the database and outside the vocabulary of expressions", QueryFeature.Expression);
                    return null;
                }

                return QueryOperand.Computed(QueryExpression.Call(QueryFunction.CurrentTimestamp, []));

            case MemberAccessExpressionSyntax member when member.Expression is IdentifierNameSyntax identifier:
                return QueryOperand.Column(AliasFor(identifier.Identifier.Text), member.Name.Identifier.Text);

            // x.o.PlacedAt after a join: the middle member is a row of the joined result, in
            // this scope or an enclosing one, and names the table.
            case MemberAccessExpressionSyntax nested when TryResolveInScope(nested, out _, out var resolved) && resolved is { } column:
                return QueryOperand.Column(column.Alias, column.Column);

            // The members of the vocabulary over an operand: c.Name.Length, o.PlacedAt.Year.
            case MemberAccessExpressionSyntax { Name.Identifier.Text: "Length" or "Year" or "Month" or "Day" } part:
                {
                    var receiver = ReadLeaf(part.Expression);
                    if (receiver is null)
                    {
                        return null;
                    }

                    var function = part.Name.Identifier.Text switch
                    {
                        "Length" => QueryFunction.Length,
                        "Year" => QueryFunction.Year,
                        "Month" => QueryFunction.Month,
                        _ => QueryFunction.Day,
                    };

                    return QueryOperand.Computed(QueryExpression.Call(function, [receiver]));
                }

            case LiteralExpressionSyntax literal:
                return QueryOperand.Value(ReadConstant(literal));

            case PrefixUnaryExpressionSyntax negation when negation.IsKind(SyntaxKind.UnaryMinusExpression)
                                                          && negation.Operand is LiteralExpressionSyntax inner:
                return QueryOperand.Value(Negate(ReadConstant(inner)));

            case ObjectCreationExpressionSyntax creation when ReadMoment(creation) is { } moment:
                return QueryOperand.Value(moment);

            case BinaryExpressionSyntax binary:
                return ReadBinary(binary);

            case ConditionalExpressionSyntax conditional:
                return ReadConditional(conditional);

            case InvocationExpressionSyntax invocation:
                if (ReadScalarSubQuery(invocation) is { } scalarSubQuery)
                {
                    return scalarSubQuery;
                }

                if (TryReadAggregate(invocation, out var aggregate))
                {
                    return aggregate;
                }

                return ReadFunctionInvocation(invocation);

            case IdentifierNameSyntax identifier:
                return ValueFromScope(identifier);

            case InterpolatedStringExpressionSyntax:
                unread ??= ($"the interpolated string '{expression}', which composes text in a way the vocabulary of expressions does not carry", QueryFeature.Expression);
                return null;

            default:
                return null;
        }
    }

    /* ---- expressions (decision 107) --------------------------------------------------- */

    /// <summary>
    /// The lambda parameters that stand for a row of the source without being its alias:
    /// the element of a group inside an aggregate's lambda (<c>g.Sum(x =&gt; x.Price * x.Quantity)</c>),
    /// whose columns belong to the source. Consulted by the column reading, so that an
    /// expression inside the lambda qualifies its columns the way a bare column there always
    /// has been.
    /// </summary>
    private readonly Dictionary<string, string> aliasSubstitutions = new(StringComparer.Ordinal);

    private string AliasFor(string identifier)
        => aliasSubstitutions.GetValueOrDefault(identifier, identifier);

    /// <summary>An operand that may be a leaf of an expression: the lists and collections the factory refuses are refused here first, by name.</summary>
    private QueryOperand? ReadLeaf(ExpressionSyntax expression)
    {
        var operand = ReadOperand(expression);
        if (operand is null)
        {
            return null;
        }

        if (operand.IsValueList || operand.Parameter?.IsCollection == true)
        {
            unread ??= ($"the collection '{expression}' inside an expression, which no target computes with", QueryFeature.Expression);
            return null;
        }

        return operand;
    }

    /// <summary>
    /// The arithmetic operators and <c>??</c> as COALESCE; <c>+</c> is a concatenation where
    /// a side is a string literal or an expression that yields a string, and an addition
    /// otherwise, which the builder's gate resolves where the mapping says a side is a
    /// string. C# takes a null operand of a string concatenation as the empty string, where
    /// SQL and JPQL yield NULL; the model does not carry the difference (decision 048), so a
    /// concatenation over a column says so in a record.
    /// </summary>
    private QueryOperand? ReadBinary(BinaryExpressionSyntax binary)
    {
        ExpressionOperator? op = binary.Kind() switch
        {
            SyntaxKind.AddExpression => ExpressionOperator.Add,
            SyntaxKind.SubtractExpression => ExpressionOperator.Subtract,
            SyntaxKind.MultiplyExpression => ExpressionOperator.Multiply,
            SyntaxKind.DivideExpression => ExpressionOperator.Divide,
            SyntaxKind.ModuloExpression => ExpressionOperator.Modulo,
            _ => null,
        };

        if (binary.IsKind(SyntaxKind.CoalesceExpression))
        {
            var first = ReadLeaf(binary.Left);
            var rest = ReadLeaf(binary.Right);
            if (first is null || rest is null)
            {
                return null;
            }

            // a ?? b ?? c parses right-nested; the model carries one COALESCE of three.
            var arguments = new List<QueryOperand> { first };
            if (rest is { IsExpression: true, IsAggregate: false } && rest.Expression!.Function == QueryFunction.Coalesce)
            {
                arguments.AddRange(rest.Expression.Arguments!);
            }
            else
            {
                arguments.Add(rest);
            }

            return QueryOperand.Computed(QueryExpression.Call(QueryFunction.Coalesce, arguments));
        }

        if (op is null)
        {
            return null;
        }

        var left = ReadLeaf(binary.Left);
        var right = ReadLeaf(binary.Right);
        if (left is null || right is null)
        {
            return null;
        }

        if (op == ExpressionOperator.Add && (ReadsAsString(left) || ReadsAsString(right)))
        {
            return Concatenation(left, right);
        }

        return QueryOperand.Computed(QueryExpression.Binary(op.Value, left, right));
    }

    private QueryOperand Concatenation(QueryOperand left, QueryOperand right)
    {
        foreach (var side in new[] { left, right })
        {
            if (side.IsColumn)
            {
                Report(
                    ConversionRecordKind.Loss,
                    $"The concatenation over the column '{side}' takes a NULL as the empty string in C#, where SQL, HQL and JPQL yield NULL; the query representation does not carry the difference, and the targets concatenate as their language does.",
                    QueryFeature.Expression);
                break;
            }
        }

        return QueryOperand.Computed(QueryExpression.Binary(ExpressionOperator.Concat, left, right));
    }

    private static bool ReadsAsString(QueryOperand operand)
    {
        if (operand.IsAggregate)
        {
            return false;
        }

        if (operand.IsConstant)
        {
            return operand.Constant!.Type is ScalarType.String or ScalarType.Char;
        }

        return operand.IsExpression
               && (operand.Expression!.Operator == ExpressionOperator.Concat
                   || operand.Expression.Function is QueryFunction.Upper or QueryFunction.Lower or QueryFunction.Trim
                       or QueryFunction.Substring or QueryFunction.EscapePattern);
    }

    /// <summary>
    /// The conditional operator as a searched CASE (decision 107): the condition is a
    /// condition tree, a nested conditional in the else position is a further branch, and a
    /// null literal there is a CASE without an ELSE.
    /// </summary>
    private QueryOperand? ReadConditional(ConditionalExpressionSyntax conditional)
    {
        var branches = new List<CaseBranch>();
        ExpressionSyntax current = conditional;

        while (current is ConditionalExpressionSyntax ternary)
        {
            var when = ParseCondition(ternary.Condition);
            var then = ReadLeaf(ternary.WhenTrue);
            if (when is null || then is null)
            {
                unread ??= ($"the conditional '{conditional}', a branch of which the query representation does not carry", QueryFeature.Expression);
                return null;
            }

            branches.Add(new CaseBranch(when, then));
            current = ternary.WhenFalse;
        }

        QueryOperand? otherwise = null;
        if (!IsNullLiteral(current))
        {
            otherwise = ReadLeaf(current);
            if (otherwise is null)
            {
                unread ??= ($"the conditional '{conditional}', whose else the query representation does not carry", QueryFeature.Expression);
                return null;
            }
        }

        return QueryOperand.Computed(QueryExpression.Case(branches, otherwise));
    }

    /// <summary>
    /// The methods of the vocabulary: <c>ToUpper()</c>, <c>ToLower()</c>, <c>Trim()</c>
    /// and <c>Substring(start[, length])</c> over an operand, <c>string.Concat(…)</c> and
    /// <c>Math.Abs(…)</c>. The start of a Substring is carried counted from one, as three of
    /// the four languages count it (decision 107): a literal has one added, anything else
    /// goes as <c>x + 1</c>, which the LINQ visitor takes off again. A Substring without a
    /// length takes the length of the text as its third argument, which selects the same
    /// characters. A method the vocabulary does not name is refused by name.
    /// </summary>
    private QueryOperand? ReadFunctionInvocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return null;
        }

        var arguments = invocation.ArgumentList.Arguments;
        var name = member.Name.Identifier.Text;

        if (WrittenName(member.Expression) is "string" or "String" or "System.String" && name == "Concat")
        {
            if (arguments.Count < 2)
            {
                unread ??= ($"'{invocation}', a Concat of fewer than two arguments", QueryFeature.Expression);
                return null;
            }

            QueryOperand? result = null;
            foreach (var argument in arguments)
            {
                var value = ReadLeaf(argument.Expression);
                if (value is null)
                {
                    return null;
                }

                result = result is null ? value : Concatenation(result, value);
            }

            return result;
        }

        if (WrittenName(member.Expression) is "Math" or "System.Math" && name == "Abs")
        {
            var value = arguments.Count == 1 ? ReadLeaf(arguments[0].Expression) : null;
            return value is null ? null : QueryOperand.Computed(QueryExpression.Call(QueryFunction.Abs, [value]));
        }

        switch (name)
        {
            case "ToUpper" or "ToLower" or "Trim" when arguments.Count == 0:
                {
                    var receiver = ReadLeaf(member.Expression);
                    if (receiver is null)
                    {
                        return null;
                    }

                    var function = name switch
                    {
                        "ToUpper" => QueryFunction.Upper,
                        "ToLower" => QueryFunction.Lower,
                        _ => QueryFunction.Trim,
                    };

                    return QueryOperand.Computed(QueryExpression.Call(function, [receiver]));
                }

            case "Substring" when arguments.Count is 1 or 2:
                {
                    var receiver = ReadLeaf(member.Expression);
                    var start = receiver is null ? null : ReadLeaf(arguments[0].Expression);
                    if (receiver is null || start is null)
                    {
                        return null;
                    }

                    var position = start is { IsConstant: true, Function: null }
                                   && int.TryParse(start.Constant!.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zeroBased)
                        ? QueryOperand.Value(QueryConstant.Of((zeroBased + 1).ToString(CultureInfo.InvariantCulture), ScalarType.Int))
                        : QueryOperand.Computed(QueryExpression.Binary(ExpressionOperator.Add, start, QueryOperand.Value(QueryConstant.Of("1", ScalarType.Int))));

                    var length = arguments.Count == 2
                        ? ReadLeaf(arguments[1].Expression)
                        : QueryOperand.Computed(QueryExpression.Call(QueryFunction.Length, [receiver]));
                    if (length is null)
                    {
                        return null;
                    }

                    return QueryOperand.Computed(QueryExpression.Call(QueryFunction.Substring, [receiver, position, length]));
                }

            case "ToUpper" or "ToLower" or "Trim" or "Substring":
                unread ??= ($"{name}() with {arguments.Count} argument(s), an overload outside the vocabulary of expressions", QueryFeature.Expression);
                return null;

            default:
                // Replace, PadLeft, ToString, Round, AddDays and the rest: outside the
                // vocabulary, and named where the receiver is a column of a row in scope - a
                // chain over a query root is no expression and keeps the reading it had.
                if (IsColumnOfScope(member.Expression))
                {
                    unread ??= ($"the method {name}() in '{invocation}', which is outside the vocabulary of expressions the query representation carries", QueryFeature.Expression);
                }

                return null;
        }
    }

    /// <summary>
    /// Reads <c>new DateTime(2025, 1, 1)</c> in operand position as a DateTime constant
    /// (decision 024). The constructor is the one way C# spells a moment inside a
    /// predicate, so until it was read the filter around it stayed unread and decision 070
    /// refused the whole artifact - which is what took the date filter out of the EF Core
    /// sample. Three spellings of the type are the same type (<c>DateTime</c>,
    /// <c>System.DateTime</c>, <c>global::System.DateTime</c>) and three arities are read:
    /// the date, the date with a time of day, and the same with milliseconds.
    ///
    /// Every argument has to be a non-negative integer literal. Anything computed is a
    /// value this parser cannot evaluate without running the program, and components that
    /// do not make a real date - month 13 - stay unread rather than leaving as text no
    /// target could use; both keep the refusal they have today instead of inventing a
    /// moment. Other arities stay unread too: <c>new DateTime(ticks)</c> says the same
    /// thing in a unit no target writes, and the overloads taking a DateTimeKind or a
    /// Calendar carry a fact the model has no place for.
    ///
    /// The value goes into the model undecorated, in the ISO spelling and always with the
    /// time of day - a .NET DateTime has one even when the source left it at midnight, and
    /// the JDBC escape the JPQL builder writes it into (<c>{ts '…'}</c>) is defined for no
    /// shorter form. Quoting is each target's own, as decision 024 divided the work.
    /// </summary>
    private static QueryConstant? ReadMoment(ObjectCreationExpressionSyntax creation)
    {
        if (TypeName(creation.Type) is not ("DateTime" or "System.DateTime" or "global::System.DateTime"))
        {
            return null;
        }

        var arguments = creation.ArgumentList?.Arguments ?? default;
        if (arguments.Count is not (3 or 6 or 7))
        {
            return null;
        }

        var parts = new int[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].Expression is not LiteralExpressionSyntax literal
                || literal.Token.Value is not int part
                || part < 0)
            {
                return null;
            }

            parts[i] = part;
        }

        DateTime moment;
        try
        {
            moment = arguments.Count == 3
                ? new DateTime(parts[0], parts[1], parts[2])
                : new DateTime(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5],
                    arguments.Count == 7 ? parts[6] : 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }

        var text = moment.ToString(
            arguments.Count == 7 ? "yyyy-MM-dd HH:mm:ss.fff" : "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture);

        return QueryConstant.Of(text, ScalarType.DateTime);
    }

    /// <summary>
    /// The written name of a type standing as an expression - the receiver of a static
    /// member such as <c>DateTime.Now</c>, <c>string.Concat</c>, <c>Math.Abs</c> -, dotted
    /// and without an alias qualifier's separator changed; null for an expression that is
    /// no name.
    /// </summary>
    private static string? WrittenName(ExpressionSyntax expression) => expression switch
    {
        PredefinedTypeSyntax predefined => predefined.Keyword.Text,
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        MemberAccessExpressionSyntax member when WrittenName(member.Expression) is { } head => $"{head}.{member.Name.Identifier.Text}",
        AliasQualifiedNameSyntax aliased => $"{aliased.Alias.Identifier.Text}::{aliased.Name.Identifier.Text}",
        _ => null,
    };

    /// <summary>
    /// The written name of a type in source, with the generic arguments and the nullable
    /// question mark left out - what is left is what the operand reader compares against.
    /// </summary>
    private static string? TypeName(TypeSyntax type) => type switch
    {
        NullableTypeSyntax nullable => TypeName(nullable.ElementType),
        AliasQualifiedNameSyntax aliased => $"{aliased.Alias.Identifier.Text}::{TypeName(aliased.Name)}",
        QualifiedNameSyntax qualified => $"{TypeName(qualified.Left)}.{TypeName(qualified.Right)}",
        SimpleNameSyntax simple => simple.Identifier.Text,
        _ => null,
    };

    /// <summary>
    /// A bare identifier in operand position is a value captured from the enclosing scope -
    /// the LINQ form of a query parameter, and the one form that needs no decoration
    /// stripping, because C# writes the name itself (decision 083). What the value is, the
    /// chain does not say; the builder template derives the scalar from the other side of
    /// the comparison.
    /// </summary>
    private static QueryOperand ValueFromScope(IdentifierNameSyntax identifier)
        => QueryOperand.Bound(QueryParameter.Named(identifier.Identifier.Text));

    /// <summary>
    /// Reads a terminal aggregate over a query root - <c>ctx.Set&lt;T&gt;().Max(x =&gt;
    /// x.Total)</c> - as a scalar subquery operand (decision 061): the aggregate becomes the
    /// subquery's own projection, recorded just before the nested scope closes so that it
    /// carries the nested source's alias. Count(predicate) folds its predicate into a Where
    /// the way Any(predicate) does. The aggregate's own lambda names the nested scope when
    /// the chain before it has no lambda step - <c>ctx.Products.Average(x =&gt;
    /// x.ListPrice)</c> ranges over x, and the table initial would shadow an outer alias
    /// that happens to be the same letter.
    /// </summary>
    private QueryOperand? ReadScalarSubQuery(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return null;
        }

        var function = member.Name.Identifier.Text switch
        {
            "Max" => "MAX",
            "Min" => "MIN",
            "Sum" => "SUM",
            "Average" => "AVG",
            "Count" => "COUNT",
            _ => null,
        };

        // A receiver that is a lambda parameter standing for a row - the element of a group
        // - is no query root, whatever the root recognizer makes of a bare identifier.
        if (function is null
            || (member.Expression is IdentifierNameSyntax head && aliasSubstitutions.ContainsKey(head.Identifier.Text))
            || !TryDecompose(member.Expression, out var root, out var steps))
        {
            return null;
        }

        string attribute;
        string? elementParameter = null;
        var distinct = false;
        var argument = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;

        // A one-column Select just before the aggregate names the column the aggregate
        // ranges over, and a Distinct() between them is the modifier of the aggregate
        // (decision 102): .Select(o => o.CustomerId).Distinct().Count() is
        // COUNT(DISTINCT CustomerId). Both steps leave the chain, being the aggregate's.
        if (argument is null && TryTakeSelectedColumn(steps, out var selectedColumn, out var selectedParameter, out distinct))
        {
            attribute = selectedColumn!;
            elementParameter = selectedParameter;
        }
        else if (argument is null)
        {
            // Count() counts rows; every other aggregate needs a column to range over.
            if (function != "COUNT")
            {
                return null;
            }

            attribute = "*";
        }
        else if (function == "COUNT" && argument is SimpleLambdaExpressionSyntax)
        {
            steps.Add(new ChainStep("Where", invocation));
            attribute = "*";
        }
        else if (argument is SimpleLambdaExpressionSyntax lambda
            && lambda.Body is ExpressionSyntax body
            && MemberName(body) is { } column)
        {
            attribute = column;
            elementParameter = lambda.Parameter.Identifier.Text;
        }
        else
        {
            return null;
        }

        // A terminal aggregate over a Distinct() of the whole entity (decision 073): Count,
        // Sum and Average aggregate over the collapsed rows - a count of distinct rows, or a
        // sum that SUM(DISTINCT ...) is not - and refuse; Max and Min do not depend on the
        // collapse, so the call is left out with a record. Either way the marker goes, so
        // that the scope does not also report the collapse over its single-row aggregate
        // projection. A Distinct() over a one-column Select is the modifier instead, read
        // above (decision 102).
        if (steps.Any(s => s.Name == "Distinct"))
        {
            var terminal = member.Name.Identifier.Text;
            if (function is "MAX" or "MIN")
            {
                Report(
                    ConversionRecordKind.Convention,
                    $"Distinct() before {terminal}() changes nothing, as the extreme of a set does not depend on duplicates; the call was left out.",
                    QueryFeature.Aggregation);
            }
            else
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"{terminal}() over Distinct() aggregates over the collapsed set - {function}(DISTINCT ...), which the query representation does not carry; no artifact was generated.",
                    QueryFeature.Aggregation);
            }

            steps.RemoveAll(s => s.Name == "Distinct");
        }

        var sub = ReadSubQueryOperand(
            root!,
            steps,
            () => queryBuilder.Project(sourceAlias, attribute, null, function, distinct),
            elementParameter);

        return QueryOperand.Nested(sub);
    }

    /// <summary>
    /// Takes a trailing <c>Select(x =&gt; x.Col)</c>, optionally followed by
    /// <c>Distinct()</c>, off the chain of a scalar subquery whose terminal aggregate has
    /// no lambda of its own (decision 102). Returns false and leaves the chain as it was
    /// when the tail is not that shape.
    /// </summary>
    private static bool TryTakeSelectedColumn(
        List<ChainStep> steps,
        out string? column,
        out string? elementParameter,
        out bool distinct)
    {
        column = elementParameter = null;
        distinct = false;

        var last = steps.Count - 1;
        if (last >= 0 && steps[last].Name == "Distinct" && steps[last].Node.ArgumentList.Arguments.Count == 0)
        {
            distinct = true;
            last--;
        }

        if (last < 0
            || steps[last].Name != "Select"
            || steps[last].Node.ArgumentList.Arguments.Count != 1
            || steps[last].Node.ArgumentList.Arguments[0].Expression is not SimpleLambdaExpressionSyntax lambda
            || lambda.Body is not ExpressionSyntax body
            || MemberName(body) is not { } selected)
        {
            distinct = false;
            return false;
        }

        column = selected;
        elementParameter = lambda.Parameter.Identifier.Text;

        // The Select taken off was the first step after a variable: what is left is the
        // variable's chain, and its rows keep the variable's name (decision 112).
        if (steps[last].AfterVariable is { } variable && steps is ChainSteps chain)
        {
            chain.EndsWithVariable ??= variable;
        }

        steps.RemoveRange(last, steps.Count - last);
        return true;
    }

    /// <summary>
    /// Turns a C# literal into a typed constant (decision 024). The token's own value
    /// carries the type, so 2000m arrives as the decimal 2000 and leaves the suffix behind
    /// in the source where it belongs.
    /// </summary>
    private QueryConstant ReadConstant(LiteralExpressionSyntax literal)
    {
        var value = literal.Token.Value;

        switch (value)
        {
            case string text: return QueryConstant.Of(text, ScalarType.String);
            case char character: return QueryConstant.Of(character.ToString(), ScalarType.Char);
            case bool flag: return QueryConstant.Of(flag ? "true" : "false", ScalarType.Bool);
            case int number: return QueryConstant.Of(number.ToString(CultureInfo.InvariantCulture), ScalarType.Int);
            case long number: return QueryConstant.Of(number.ToString(CultureInfo.InvariantCulture), ScalarType.Long);
            case decimal number: return QueryConstant.Of(number.ToString(CultureInfo.InvariantCulture), ScalarType.Decimal);
            case double number: return QueryConstant.Of(number.ToString(CultureInfo.InvariantCulture), ScalarType.Double);
            case float number: return QueryConstant.Of(number.ToString(CultureInfo.InvariantCulture), ScalarType.Float);
        }

        Report(
            ConversionRecordKind.Incompleteness,
            $"The literal '{literal}' has no counterpart in the scalar vocabulary; it is carried verbatim.",
            QueryFeature.Filtering);

        return QueryConstant.Unrecognized(literal.Token.ValueText);
    }

    private static QueryConstant Negate(QueryConstant constant)
        => constant.Type is null
            ? QueryConstant.Unrecognized("-" + constant.Text)
            : QueryConstant.Of("-" + constant.Text, constant.Type.Value);

    private static bool IsNullLiteral(ExpressionSyntax expression)
        => expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NullLiteralExpression);

    private static ComparisonOperator? MapOperator(SyntaxKind kind) => kind switch
    {
        SyntaxKind.EqualsExpression => ComparisonOperator.Equal,
        SyntaxKind.NotEqualsExpression => ComparisonOperator.NotEqual,
        SyntaxKind.GreaterThanExpression => ComparisonOperator.GreaterThan,
        SyntaxKind.GreaterThanOrEqualExpression => ComparisonOperator.GreaterThanOrEqual,
        SyntaxKind.LessThanExpression => ComparisonOperator.LessThan,
        SyntaxKind.LessThanOrEqualExpression => ComparisonOperator.LessThanOrEqual,
        _ => null,
    };

    private static bool TryReadLambdaBody(InvocationExpressionSyntax node, out ExpressionSyntax? body)
    {
        body = null;

        if (node.ArgumentList.Arguments.FirstOrDefault()?.Expression is SimpleLambdaExpressionSyntax lambda
            && lambda.Body is ExpressionSyntax lambdaBody)
        {
            body = lambdaBody;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Member name of a column reference, or null when the expression is not one. Returning
    /// null rather than throwing is the point: an unsupported shape is a record, never an
    /// exception escaping to the caller.
    /// </summary>
    private static string? MemberName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        _ => null,
    };

    /// <summary>
    /// The table alias a column reference is qualified by: the row of the joined result it
    /// goes through (<c>x.o.PlacedAt</c>), else the identifier before the column, which is
    /// the lambda parameter and, for the chain's first lambda, the source alias itself.
    /// </summary>
    private string AliasOf(ExpressionSyntax expression)
    {
        if (TryResolveInScope(expression, out _, out var resolved) && resolved is { } column)
        {
            return column.Alias;
        }

        return expression switch
        {
            MemberAccessExpressionSyntax member when member.Expression is IdentifierNameSyntax identifier
                => AliasFor(identifier.Identifier.Text),
            _ => sourceAlias,
        };
    }

    private static string NameOfSource(ExpressionSyntax expression) => expression switch
    {
        InvocationExpressionSyntax invocation when invocation.Expression is MemberAccessExpressionSyntax member
            => TypeArgumentOf(member.Name) ?? member.Name.Identifier.Text,
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        _ => "unknown_table",
    };

    /// <summary>
    /// Resolves the name the source wrote — a DbSet name or an entity name — to a qualified
    /// table, using the mapping IR built by the entity parsers (rule Q2).
    /// </summary>
    private string ResolveTable(string name)
        => MapFor(name) is { } map ? Qualify(map, map.Table ?? name) : name;

    /// <summary>
    /// The mapping behind the name the source wrote - by table, by entity, or by the naming
    /// convention between the two - or null when the conversion holds none. The row of a
    /// source or of a joined table keeps it, so that a navigation written from that row
    /// can be matched to a relation of the entity (decision 101).
    /// </summary>
    private EntityMap? MapFor(string name)
    {
        if (entityMaps is not { Count: > 0 })
        {
            return null;
        }

        var byTable = entityMaps.FirstOrDefault(m =>
            string.Equals(m.Table, name, StringComparison.OrdinalIgnoreCase));
        if (byTable is not null)
        {
            return byTable;
        }

        var byEntity = entityMaps.FirstOrDefault(m =>
            string.Equals(m.Entity?.Name, name, StringComparison.OrdinalIgnoreCase));
        if (byEntity is not null)
        {
            return byEntity;
        }

        // The other grammatical number comes from the one rule (decision 050); a glued
        // "s" used to answer differently for an entity already ending in s.
        return entityMaps.FirstOrDefault(m =>
            m.Entity?.Name is { Length: > 0 } entityName
            && EntityTableNaming.TableCandidatesFor(entityName)
                .Any(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)));
    }

    private static string Qualify(EntityMap map, string table)
        => string.IsNullOrWhiteSpace(map.Schema) ? table : $"{map.Schema}.{table}";

    protected static string? TypeArgumentOf(SimpleNameSyntax name)
        => name is GenericNameSyntax generic && generic.TypeArgumentList.Arguments.Count > 0
            ? generic.TypeArgumentList.Arguments[0] switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.Text,
                QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
                GenericNameSyntax nested => nested.Identifier.Text,
                var other => other.ToString(),
            }
            : null;

    private void Report(ConversionRecordKind kind, string reason, QueryFeature? feature = null)
        => queryBuilder.Report(new ConversionRecord
        {
            Kind = kind,
            Framework = queryBuilder.Descriptor.Framework,
            Artifact = ConversionContentType.CSharpQuery,
            Feature = feature,
            Reason = reason,
        });
}
