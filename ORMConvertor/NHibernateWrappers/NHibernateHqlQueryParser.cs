using System.Text;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace NHibernateWrappers;

/// <summary>
/// Reads a bare HQL query into the query IR (decision 062). A hand-written recursive
/// descent rather than a package: the only reference parser of HQL lives inside NHibernate
/// itself, which S1 forbids the wrapper to reference, and the language read here is not all
/// of HQL but the closed subset the query IR carries — the same subset the HQL builder
/// emits, so the round-trip test pins parser and builder to one grammar.
///
/// The discipline is that the parser may fail to understand, never understand differently:
/// a syntax error refuses the artifact with a line and a column, and a construct the model
/// has no place for gets the same record the other two parsers issue in the same situation.
///
/// HQL names entities and properties where the IR holds tables and columns, so every name
/// goes through the mapping IR — the exact inverse of the builder's visitor.
///
/// HQL itself states no parameter's type. Where the text comes from an hbm.xml
/// &lt;query&gt;, its &lt;query-param&gt; elements may, and the caller hands those scalars over
/// as <c>statedScalars</c>, keyed by the undecorated name: they travel on the
/// parameter as stated, and the builder template keeps them over what it would derive
/// (decision 083).
/// </summary>
public class NHibernateHqlQueryParser(
    Func<AbstractQueryBuilder> queryBuilders,
    IReadOnlyDictionary<string, ScalarType>? statedScalars = null) : IQueryParser
{
    /// <summary>
    /// The builder of the query being read. Assigned at the start of every Parse from the
    /// factory the orchestration supplied: one query, one fresh builder (decision 081). The
    /// parser may not make one itself - a builder belongs to the target framework and this
    /// parser to the source (S1) - and it is never touched outside a Parse call.
    /// </summary>
    private AbstractQueryBuilder queryBuilder = default!;

    private enum TokenKind { Identifier, Number, String, Symbol, Parameter, End }

    private readonly record struct Token(TokenKind Kind, string Text, int Line, int Column);

    private sealed class HqlParseError(int line, int column, string message) : Exception(message)
    {
        public int Line { get; } = line;

        public int Column { get; } = column;
    }

    /// <summary>
    /// Every keyword of the read subset. An identifier on this list is never taken for an
    /// alias, which is how "from Customer order by ..." keeps its ordering.
    /// </summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "select", "distinct", "from", "as", "inner", "left", "right", "full", "outer",
        "join", "fetch", "with", "where", "group", "having", "order", "by", "asc", "desc",
        "and", "or", "not", "like", "in", "is", "null", "between", "exists", "escape",
        "case", "when", "then", "else", "end",

        // Not part of the read subset - HQL in NHibernate 5.7.0 has no set operations - but
        // reserved so that "from Customer union ..." fails as a syntax error instead of
        // taking "union" for an alias. "all" doubles as the quantifier of decision 119,
        // beside "any" and "some".
        "union", "intersect", "except", "all", "any", "some",
    };

    private List<Token> tokens = [];
    private int position;
    private IReadOnlyList<EntityMap>? maps;
    private string sourceAlias = "t";

    /// <summary>
    /// The aliases the query has declared so far, each bound to the entity it stands for
    /// (null when the maps do not know it). Doubles as the scope for correlated references:
    /// a subquery sees the enclosing aliases and its own shadow them (decision 061).
    /// </summary>
    private Dictionary<string, EntityMap?> aliases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The construct that sank the condition being read, when the parser can name it - a
    /// parameter, a null among the values of an in list - so that the clause's refusal says
    /// what the caller would have to change (F11). The category overrides the clause's own
    /// only for a parameter, which has a category of its own (decision 070).
    /// </summary>
    private (string What, QueryFeature? Category)? unread;

    /// <summary>
    /// The limits this parser reads its input under (decision 092). The orchestration sets
    /// them on every parser it creates; one constructed by hand - in a test - runs under the
    /// default, which is the cap the application uses unless its operator moved it.
    /// </summary>
    public ParseLimits Limits { get; set; } = ParseLimits.Default;

    public bool CanParse(ConversionContentType contentType)
        => contentType == ConversionContentType.HqlQuery;

    /// <summary>
    /// The content type is not consulted: bare HQL is the only language this parser claims
    /// (see CanParse). It is in the signature because the unit declares its language and the
    /// orchestration routes by it (decision 047).
    /// </summary>
    public IReadOnlyCollection<AbstractQueryBuilder> Parse(ConversionContentType contentType, string source, IReadOnlyList<EntityMap>? entityMaps = null)
    {
        queryBuilder = queryBuilders();
        maps = entityMaps;
        aliases = new Dictionary<string, EntityMap?>(StringComparer.OrdinalIgnoreCase);

        queryBuilder.Push();
        try
        {
            tokens = Lex(source);

            // Before the descent, because the descent is what the cap protects: from here on
            // the depth of the input is the depth of the recursion, and an overflow is not a
            // record but the end of the process (decision 092). Lexing is a loop, so reaching
            // this line is safe at any depth, and the tokens are already in hand.
            if (NestingDepthGuard.FirstBeyond(Tracked(tokens), Limits) is { } tooDeep)
            {
                Report(ConversionRecordKind.Failure, NestingDepthGuard.Reason(tooDeep, Limits));
            }
            else
            {
                position = 0;
                ParseQueryBody();

                if (Current.Kind != TokenKind.End)
                {
                    throw Error("expected the end of the query");
                }
            }
        }
        catch (HqlParseError error)
        {
            // A parse error carries a line and a column, which is what S7 asks the UI to
            // show — the same sentence the Dapper parser gets from TSql160Parser.
            Report(
                ConversionRecordKind.Failure,
                $"The HQL could not be parsed at line {error.Line}, column {error.Column}: {error.Message}.");
        }

        queryBuilder.Pop();

        // The builder leaves even when it was refused: it holds the records of what went
        // wrong, and only the parser can say that this unit yielded a query (decision 081).
        return [queryBuilder];
    }

    /* ---- lexer ---------------------------------------------------------------------- */

    private static List<Token> Lex(string source)
    {
        var read = new List<Token>();
        int line = 1, column = 1, i = 0;

        while (i < source.Length)
        {
            char c = source[i];

            if (c == '\n')
            {
                line++;
                column = 1;
                i++;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                column++;
                i++;
                continue;
            }

            int startLine = line, startColumn = column;

            if (c == '\'')
            {
                var text = new StringBuilder();
                i++;
                column++;
                while (true)
                {
                    if (i >= source.Length)
                    {
                        throw new HqlParseError(startLine, startColumn, "unterminated string literal");
                    }

                    if (source[i] == '\'')
                    {
                        if (i + 1 < source.Length && source[i + 1] == '\'')
                        {
                            text.Append('\'');
                            i += 2;
                            column += 2;
                            continue;
                        }

                        i++;
                        column++;
                        break;
                    }

                    if (source[i] == '\n')
                    {
                        line++;
                        column = 1;
                    }
                    else
                    {
                        column++;
                    }

                    text.Append(source[i]);
                    i++;
                }

                read.Add(new Token(TokenKind.String, text.ToString(), startLine, startColumn));
                continue;
            }

            if (char.IsAsciiDigit(c))
            {
                int start = i;
                while (i < source.Length && char.IsAsciiDigit(source[i]))
                {
                    i++;
                    column++;
                }

                if (i + 1 < source.Length && source[i] == '.' && char.IsAsciiDigit(source[i + 1]))
                {
                    i++;
                    column++;
                    while (i < source.Length && char.IsAsciiDigit(source[i]))
                    {
                        i++;
                        column++;
                    }
                }

                read.Add(new Token(TokenKind.Number, source[start..i], startLine, startColumn));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                {
                    i++;
                    column++;
                }

                read.Add(new Token(TokenKind.Identifier, source[start..i], startLine, startColumn));
                continue;
            }

            if (c == ':')
            {
                int start = i;
                i++;
                column++;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                {
                    i++;
                    column++;
                }

                read.Add(new Token(TokenKind.Parameter, source[start..i], startLine, startColumn));
                continue;
            }

            if (c == '?')
            {
                read.Add(new Token(TokenKind.Parameter, "?", startLine, startColumn));
                i++;
                column++;
                continue;
            }

            if (i + 1 < source.Length && source.Substring(i, 2) is ("<>" or "<=" or ">=" or "!=" or "||") and var pair)
            {
                read.Add(new Token(TokenKind.Symbol, pair, startLine, startColumn));
                i += 2;
                column += 2;
                continue;
            }

            // The arithmetic operators and the concatenation entered with decision 107.
            if (c is '(' or ')' or ',' or '.' or '*' or '=' or '<' or '>' or '-' or '+' or '/' or '%')
            {
                read.Add(new Token(TokenKind.Symbol, c.ToString(), startLine, startColumn));
                i++;
                column++;
                continue;
            }

            throw new HqlParseError(startLine, startColumn, $"unexpected character '{c}'");
        }

        read.Add(new Token(TokenKind.End, string.Empty, line, column));
        return read;
    }

    /// <summary>
    /// The lexer's own tokens as the shared nesting guard reads them - text and position, and
    /// nothing else, because that is all a token type has in common across five languages
    /// (decision 092). Lazy on purpose: the guard stops at the first token past the cap.
    /// </summary>
    private static IEnumerable<SourceToken> Tracked(IEnumerable<Token> read)
        => read.Select(token => new SourceToken(token.Text, token.Line, token.Column));

    /* ---- token helpers -------------------------------------------------------------- */

    private Token Current => tokens[position];

    private Token Next => tokens[Math.Min(position + 1, tokens.Count - 1)];

    /// <summary>The token n places ahead, clamped to the end marker.</summary>
    private Token Ahead(int n) => tokens[Math.Min(position + n, tokens.Count - 1)];

    private void Advance() => position++;

    private bool AtKeyword(string keyword)
        => Current.Kind == TokenKind.Identifier
           && string.Equals(Current.Text, keyword, StringComparison.OrdinalIgnoreCase);

    private bool TryConsumeKeyword(string keyword)
    {
        if (!AtKeyword(keyword))
        {
            return false;
        }

        Advance();
        return true;
    }

    private void ConsumeKeyword(string keyword)
    {
        if (!TryConsumeKeyword(keyword))
        {
            throw Error($"expected '{keyword}'");
        }
    }

    private bool AtSymbol(string symbol)
        => Current.Kind == TokenKind.Symbol && Current.Text == symbol;

    private bool TryConsumeSymbol(string symbol)
    {
        if (!AtSymbol(symbol))
        {
            return false;
        }

        Advance();
        return true;
    }

    private void ConsumeSymbol(string symbol)
    {
        if (!TryConsumeSymbol(symbol))
        {
            throw Error($"expected '{symbol}'");
        }
    }

    private HqlParseError Error(string message)
        => new(
            Current.Line,
            Current.Column,
            Current.Kind == TokenKind.End
                ? $"{message}, found the end of the query"
                : $"{message}, found '{Current.Text}'");

    /* ---- clauses -------------------------------------------------------------------- */

    /// <summary>
    /// One (sub)query body in HQL's clause order. The select clause is read first but
    /// emitted only after from and the joins, because an unqualified projection needs the
    /// source alias and a whole-entity projection needs the declared aliases — the textual
    /// permutation the relational step order of decision 023 undoes on the builder side.
    /// </summary>
    private void ParseQueryBody()
    {
        // The select clause is skipped on the first pass and read after the from clause and
        // the joins have declared the aliases (decision 107): a projected expression
        // resolves its columns through the aliases the same way a condition does, and the
        // tokens are a list, so the reader comes back to the clause by position.
        int? selectStart = null;
        if (TryConsumeKeyword("select"))
        {
            // DISTINCT is a property of the whole projection, carried per (sub)query scope
            // (decision 073); it used to be refused here (decision 070).
            if (TryConsumeKeyword("distinct"))
            {
                queryBuilder.Distinct();
            }

            selectStart = position;
            SkipToFrom();
        }

        ConsumeKeyword("from");
        var (table, alias) = ParseEntityReference();
        sourceAlias = alias;
        queryBuilder.From(table, alias);

        // A cross join multiplies rows, so reading the first source alone would translate a
        // different query (decision 070). The rest is still consumed so that the clauses
        // after it can report their own reasons.
        while (TryConsumeSymbol(","))
        {
            Report(
                ConversionRecordKind.Failure,
                "Comma-separated entity references are a cross join the query representation cannot carry, and a query emitted without it would return different rows; no artifact was generated.",
                QueryFeature.Join);
            ParseEntityReference();
        }

        while (AtKeyword("inner") || AtKeyword("left") || AtKeyword("right") || AtKeyword("full") || AtKeyword("join"))
        {
            ParseJoin();
        }

        if (selectStart is { } selectAt)
        {
            var afterJoins = position;
            position = selectAt;

            var projections = new List<Projection>();
            do
            {
                projections.Add(ParseProjection());
            }
            while (TryConsumeSymbol(","));

            if (!AtKeyword("from"))
            {
                throw Error("expected 'from' after the select clause");
            }

            position = afterJoins;
            EmitProjections(projections);
        }

        if (TryConsumeKeyword("where"))
        {
            var condition = ParseCondition();
            if (condition is null)
            {
                Refuse("where clause", "a query emitted without its filter would return different rows", QueryFeature.Filtering);
            }
            else
            {
                queryBuilder.Where(condition);
            }
        }

        if (TryConsumeKeyword("group"))
        {
            ConsumeKeyword("by");
            do
            {
                var start = position;
                var key = ParseOperand();
                if (key is null && position == start)
                {
                    throw Error("expected a grouping key");
                }

                if (key is { IsColumn: true, IsAggregate: false } && key.Property != "*")
                {
                    queryBuilder.GroupBy(key.Table ?? sourceAlias, key.Property!);
                }
                else if (key is { IsExpression: true, IsAggregate: false })
                {
                    // An expression is a grouping key since decision 113, which HQL 5.7 groups
                    // by as written; what the rest of the scope may name beside it is the
                    // template's rule.
                    queryBuilder.GroupBy(key);
                }
                else
                {
                    // Grouping decides which rows come back (decision 070).
                    var (what, category) = unread ?? ("a grouping key that is neither a property reference nor an expression", QueryFeature.Grouping);
                    unread = null;
                    Report(
                        ConversionRecordKind.Failure,
                        $"The grouping uses {what}, and a query grouped differently would return different rows; no artifact was generated.",
                        category ?? QueryFeature.Grouping);
                }
            }
            while (TryConsumeSymbol(","));
        }

        if (TryConsumeKeyword("having"))
        {
            var condition = ParseCondition();
            if (condition is null)
            {
                Refuse("having clause", "a query emitted without its post-aggregation filter would return different rows", QueryFeature.PostAggregationFiltering);
            }
            else
            {
                queryBuilder.Having(condition);
            }
        }

        if (TryConsumeKeyword("order"))
        {
            ConsumeKeyword("by");
            do
            {
                // A property, an aggregate (order by count(*) desc), an expression or a
                // projection alias (decision 107); a bare constant is no ordering key the
                // representation carries and stays the loss it was.
                var start = position;
                var key = ParseOperand();
                if (key is null && position == start)
                {
                    throw Error("expected an ordering key");
                }

                bool asc = true;
                if (TryConsumeKeyword("desc"))
                {
                    asc = false;
                }
                else
                {
                    TryConsumeKeyword("asc");
                }

                if (key is null || key.IsConstant || key.IsParameter)
                {
                    var (what, category) = unread ?? ("a construct that is not a property reference", null);
                    unread = null;
                    Report(
                        ConversionRecordKind.Loss,
                        $"An ordering key that is {what} was dropped.",
                        category ?? QueryFeature.Ordering);
                }
                else
                {
                    queryBuilder.OrderBy(key, asc);
                }
            }
            while (TryConsumeSymbol(","));
        }
    }

    /// <summary>
    /// Skips the select list up to the <c>from</c> that closes it, at the nesting depth the
    /// clause opened at, so that a subquery in the list does not end the skip early.
    /// </summary>
    private void SkipToFrom()
    {
        var depth = 0;
        while (Current.Kind != TokenKind.End)
        {
            if (AtSymbol("("))
            {
                depth++;
            }
            else if (AtSymbol(")"))
            {
                depth--;
            }
            else if (depth == 0 && AtKeyword("from"))
            {
                return;
            }

            Advance();
        }
    }

    /// <summary>One projected value as read (decision 107): the operand, or null for a construct the representation does not carry, and the alias the source gave it.</summary>
    private sealed record Projection(QueryOperand? Operand, string? Alias);

    private Projection ParseProjection()
    {
        var start = position;
        var operand = ParseOperand();
        if (operand is null && position == start)
        {
            throw Error("expected a projected value");
        }

        string? alias = null;
        if (TryConsumeKeyword("as"))
        {
            if (Current.Kind != TokenKind.Identifier)
            {
                throw Error("expected an alias after 'as'");
            }

            alias = Current.Text;
            Advance();
        }

        return new Projection(operand, alias);
    }

    private void EmitProjections(List<Projection> projections)
    {
        foreach (var projection in projections)
        {
            var operand = projection.Operand;

            // A constant is carried under its alias, which names its column (decision 113);
            // without one it names nothing.
            if (operand is null || (operand.IsConstant && !operand.IsAggregate && projection.Alias is null) || operand.IsParameter)
            {
                var (what, category) = unread ?? ("a property reference, an aggregate, an expression or a constant under an alias", null);
                unread = null;
                Report(
                    ConversionRecordKind.Loss,
                    $"A projected expression that is not {what} was dropped.",
                    category ?? QueryFeature.Projection);
                continue;
            }

            // A bare declared alias projects the whole entity, which rule Q3 spells as the
            // absence of a projection — the same reading LINQ's Select(c => c) gets.
            if (operand is { IsColumn: true, IsAggregate: false, Property: "*" })
            {
                if (projections.Count > 1)
                {
                    Report(
                        ConversionRecordKind.Loss,
                        $"The whole-entity projection '{operand.Table}' next to other columns is not carried by the query representation; it was dropped.",
                        QueryFeature.Projection);
                }

                continue;
            }

            if (operand is { IsColumn: true, Property: "*" })
            {
                queryBuilder.Project(sourceAlias, "*", projection.Alias, operand.Function, operand.Distinct);
                continue;
            }

            // The projection names its table; a column the text left unqualified belongs to
            // the source.
            if (operand is { IsColumn: true, Table: null })
            {
                queryBuilder.Project(sourceAlias, operand.Property!, projection.Alias, operand.Function, operand.Distinct);
                continue;
            }

            queryBuilder.Project(operand, projection.Alias);
        }
    }

    private (string Table, string Alias) ParseEntityReference()
    {
        var parts = ParseDottedName("expected an entity name");

        // A qualified name — Shop.Customer — carries its namespace, which the IR does not
        // name entities by; the last segment is the entity.
        var entity = parts[^1];
        var map = MapFor(entity);
        var alias = ParseOptionalAlias() ?? entity;
        aliases[alias] = map;

        return (TableFor(map, entity), alias);
    }

    private void ParseJoin()
    {
        JoinKind kind;
        if (TryConsumeKeyword("inner"))
        {
            kind = JoinKind.Inner;
        }
        else if (TryConsumeKeyword("left"))
        {
            TryConsumeKeyword("outer");
            kind = JoinKind.Left;
        }
        else if (TryConsumeKeyword("right"))
        {
            TryConsumeKeyword("outer");
            kind = JoinKind.Right;
        }
        else if (TryConsumeKeyword("full"))
        {
            TryConsumeKeyword("outer");
            kind = JoinKind.Full;
        }
        else
        {
            kind = JoinKind.Inner;
        }

        ConsumeKeyword("join");

        if (TryConsumeKeyword("fetch"))
        {
            Report(
                ConversionRecordKind.Loss,
                "The fetch modifier of a join only changes what is loaded eagerly; the join was read without it.",
                QueryFeature.Join);
        }

        var parts = ParseDottedName("expected an entity name after 'join'");

        // alias.Property is an association path, whose predicate lives in the mapping: it is
        // derived from the relation the path names (decision 101, paper rule Q7).
        if (parts.Count > 1 && aliases.ContainsKey(parts[0]))
        {
            ParseAssociationJoin(kind, parts);
            return;
        }

        var entity = parts[^1];
        var map = MapFor(entity);
        var alias = ParseOptionalAlias() ?? entity;
        aliases[alias] = map;

        if (!TryConsumeKeyword("with"))
        {
            Report(
                ConversionRecordKind.Failure,
                "An entity join without a with condition has no join predicate the query representation can carry, and a query emitted without its join would return different rows; no artifact was generated.",
                QueryFeature.Join);
            return;
        }

        var condition = ParseCondition();
        if (condition is null)
        {
            Refuse("join's with condition", "a query emitted without its join would return different rows", QueryFeature.Join);
            return;
        }

        queryBuilder.Join(kind, sourceAlias, TableFor(map, entity), condition, alias);
    }

    /// <summary>
    /// A join along an association path - <c>join o.Customer c</c> (decision 101, paper rule
    /// Q7). The predicate is not in the query but in the mapping, so it is derived from the
    /// relation the path names, FK(left) = PK(right) over its column pairs, and the join
    /// reaches the builder in the very shape an entity join with a written condition takes.
    /// What the maps of the conversion do not hold is refused by name and never guessed
    /// (decision 067): a query emitted without its join would return different rows
    /// (decision 070). The syntax is consumed either way, so that the clauses after it are
    /// still read and every reason arrives at once. The same reading as the JPQL parser's,
    /// with HQL's <c>with</c> as the written condition.
    /// </summary>
    private void ParseAssociationJoin(JoinKind kind, List<string> parts)
    {
        var association = ResolveAssociation(parts, out var failure);

        // The alias is declared before the written condition is read, because that
        // condition may refer to it; an unresolved path still declares it, as null.
        var alias = ParseOptionalAlias() ?? parts[^1];
        aliases[alias] = association?.Target;

        ConditionNode? written = null;
        if (TryConsumeKeyword("with"))
        {
            written = ParseCondition();
            if (written is null)
            {
                if (failure is null)
                {
                    Refuse("join's with condition", "a query emitted without its join would return different rows", QueryFeature.Join);
                    return;
                }

                unread = null;
            }
        }

        if (association is null)
        {
            Report(ConversionRecordKind.Failure, failure!, QueryFeature.Join);
            return;
        }

        var condition = AssociationCondition(association.Relation, association.Pairs, parts[0], alias, written);
        queryBuilder.Join(kind, sourceAlias, TableFor(association.Target, association.Relation.TargetEntity), condition, alias);
    }

    private sealed record AssociationJoin(Relation Relation, EntityMap Target, IReadOnlyList<ColumnPair> Pairs);

    /// <summary>
    /// The relation a path names, or null with the sentence that says what the maps of the
    /// conversion are missing (decision 101). The path is the alias and one association: a
    /// longer one crosses a component or an intermediate join the model does not carry.
    /// A many-to-many relation stands on its junction entity (decision 005), so a path
    /// across it would be two joins and an invented alias - a stated limit, refused by name.
    /// </summary>
    private AssociationJoin? ResolveAssociation(List<string> parts, out string? failure)
    {
        var path = string.Join('.', parts);
        const string consequence = "a query emitted without its join would return different rows; no artifact was generated.";

        if (parts.Count > 2)
        {
            failure = $"The join along the path '{path}' crosses more than one association, and exactly one association is read from an alias; {consequence}";
            return null;
        }

        var owner = aliases[parts[0]];
        if (owner is null)
        {
            failure = $"The join along the association path '{path}' needs the mapping of the entity behind '{parts[0]}', which is not part of the conversion, so no relation was there to derive the join condition from; {consequence}";
            return null;
        }

        var relation = owner.Relations.FirstOrDefault(r =>
            string.Equals(r.SourceNavigationProperty, parts[1], StringComparison.OrdinalIgnoreCase));
        if (relation is null)
        {
            failure = $"The join along the association path '{path}' names no association the mapping of '{owner.Entity.Name}' declares, so no relation was there to derive the join condition from; {consequence}";
            return null;
        }

        if (relation.Cardinality == Cardinality.ManyToMany)
        {
            failure = $"The join along the association path '{path}' crosses the many-to-many relation to '{relation.TargetEntity}', which would take two joins over its junction entity, and that is not derived; {consequence}";
            return null;
        }

        var target = MapFor(relation.TargetEntity);
        if (target is null)
        {
            failure = $"The join along the association path '{path}' leads to the entity '{relation.TargetEntity}', which is not part of the conversion; {consequence}";
            return null;
        }

        var pairs = ColumnPairsOf(relation, owner, target);
        if (pairs.Count == 0)
        {
            failure = $"The join along the association path '{path}' has no foreign key columns to derive its condition from: the relation to '{relation.TargetEntity}' states none and the database catalog supplied none; {consequence}";
            return null;
        }

        failure = null;
        return new AssociationJoin(relation, target, pairs);
    }

    /// <summary>
    /// The column pairs a relation stands on: its own, or those of its counterpart on the far
    /// side where it states none - the inverse one-to-one under property-ref carries no
    /// columns of its own, the many-to-one that owns the relation holds the foreign key and
    /// the pairs with it, and both sides of one relation share the same pairs (decision 012).
    /// The counterpart is the relation of the target with the opposite role that points back
    /// at the entity, pinned by the inverse navigation where either side names it; more than
    /// one candidate names nothing. Empty where neither side states columns. The same reading
    /// the JPQL and LINQ parsers take; it is written here again because the three share no
    /// query code (S1).
    /// </summary>
    private static IReadOnlyList<ColumnPair> ColumnPairsOf(Relation relation, EntityMap owner, EntityMap target)
    {
        if (relation.ColumnPairs.Count > 0)
        {
            return relation.ColumnPairs;
        }

        var candidates = target.Relations
            .Where(r => r.Role != relation.Role
                        && r.Cardinality != Cardinality.ManyToMany
                        && r.ColumnPairs.Count > 0
                        && string.Equals(r.TargetEntity, owner.Entity.Name, StringComparison.OrdinalIgnoreCase)
                        && (relation.InverseRelationName is null || string.Equals(r.SourceNavigationProperty, relation.InverseRelationName, StringComparison.OrdinalIgnoreCase))
                        && (r.InverseRelationName is null || string.Equals(r.InverseRelationName, relation.SourceNavigationProperty, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return candidates.Count == 1 ? candidates[0].ColumnPairs : [];
    }

    /// <summary>
    /// FK(left) = PK(right) over the column pairs, in their order (decision 101). Which alias
    /// holds the foreign key follows the role of the relation: the entity behind the path
    /// for an owning one, the joined entity for an inverse one. A condition the source
    /// wrote after the path narrows the join further and joins the conjunction.
    /// </summary>
    private static ConditionNode AssociationCondition(Relation relation, IReadOnlyList<ColumnPair> pairs, string pathAlias, string joinAlias, ConditionNode? written)
    {
        var (keyHolder, referenced) = relation.Role == RelationRole.Owning
            ? (pathAlias, joinAlias)
            : (joinAlias, pathAlias);

        var conjuncts = pairs
            .Select(pair => (ConditionNode)new ComparisonCondition(
                QueryOperand.Column(keyHolder, pair.Source.ColumnName ?? pair.Source.Property.Name),
                ComparisonOperator.Equal,
                QueryOperand.Column(referenced, pair.Target.ColumnName ?? pair.Target.Property.Name)))
            .ToList();

        if (written is not null)
        {
            conjuncts.Add(written);
        }

        return conjuncts.Count == 1 ? conjuncts[0] : new LogicalCondition(LogicalOperator.And, conjuncts);
    }

    private List<string> ParseDottedName(string expectation)
    {
        if (Current.Kind != TokenKind.Identifier)
        {
            throw Error(expectation);
        }

        var parts = new List<string> { Current.Text };
        Advance();

        while (TryConsumeSymbol("."))
        {
            if (Current.Kind != TokenKind.Identifier)
            {
                throw Error("expected a name after '.'");
            }

            parts.Add(Current.Text);
            Advance();
        }

        return parts;
    }

    private string? ParseOptionalAlias()
    {
        if (TryConsumeKeyword("as"))
        {
            if (Current.Kind != TokenKind.Identifier)
            {
                throw Error("expected an alias after 'as'");
            }

            var name = Current.Text;
            Advance();
            return name;
        }

        if (Current.Kind == TokenKind.Identifier && !Keywords.Contains(Current.Text))
        {
            var name = Current.Text;
            Advance();
            return name;
        }

        return null;
    }

    /* ---- conditions ----------------------------------------------------------------- */

    private ConditionNode? ParseCondition() => ParseOr();

    private ConditionNode? ParseOr()
    {
        var operands = new List<ConditionNode?> { ParseAnd() };
        while (TryConsumeKeyword("or"))
        {
            operands.Add(ParseAnd());
        }

        return Combine(operands, LogicalOperator.Or);
    }

    private ConditionNode? ParseAnd()
    {
        var operands = new List<ConditionNode?> { ParseNot() };
        while (TryConsumeKeyword("and"))
        {
            operands.Add(ParseNot());
        }

        return Combine(operands, LogicalOperator.And);
    }

    /// <summary>
    /// Chains of the same operator flatten into one node; a null anywhere sinks the whole
    /// condition, after every token of it has been consumed — the clause refuses the
    /// artifact (decision 070).
    /// </summary>
    private static ConditionNode? Combine(List<ConditionNode?> parts, LogicalOperator op)
    {
        if (parts.Count == 1)
        {
            return parts[0];
        }

        if (parts.Any(part => part is null))
        {
            return null;
        }

        var flattened = new List<ConditionNode>();
        foreach (var part in parts)
        {
            Flatten(part!, op, flattened);
        }

        return new LogicalCondition(op, flattened);
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

    private ConditionNode? ParseNot()
    {
        if (TryConsumeKeyword("not"))
        {
            var operand = ParseNot();
            return operand is null ? null : new NotCondition(operand);
        }

        return ParsePrimary();
    }

    private ConditionNode? ParsePrimary()
    {
        if (AtSymbol("("))
        {
            // A parenthesis opens either a grouped condition or a subquery standing as the
            // left operand of a comparison; the first keyword inside tells them apart.
            if (NextIsSubQuery())
            {
                var sub = ParseParenthesizedSubQuery();
                var op = ParseComparisonOperator() ?? throw Error("expected a comparison operator after the subquery");
                var right = ParseOperandOrSubQuery();
                return right is null
                    ? null
                    : new ComparisonCondition(QueryOperand.Nested(sub), op, right);
            }

            Advance();
            var grouped = ParseCondition();
            ConsumeSymbol(")");
            return grouped;
        }

        if (TryConsumeKeyword("exists"))
        {
            // EXISTS carries its subquery as the left operand, the way IS NULL carries its
            // column (decisions 002 and 061).
            var sub = ParseParenthesizedSubQuery();
            return new ComparisonCondition(QueryOperand.Nested(sub), ComparisonOperator.Exists);
        }

        return ParsePredicate();
    }

    private ConditionNode? ParsePredicate()
    {
        // A null operand that consumed nothing is not an uncarriable construct but a hole
        // in the syntax - "where" with nothing readable after it - and a hole is a Failure
        // with a position, never a dropped filter.
        var start = position;
        var left = ParseOperand();
        if (left is null && position == start)
        {
            throw Error("expected a condition");
        }

        if (TryConsumeKeyword("is"))
        {
            bool negated = TryConsumeKeyword("not");
            ConsumeKeyword("null");
            if (left is null)
            {
                return null;
            }

            return IsEntity(left)
                ? EntityNullness(left, negated)
                : new ComparisonCondition(left, negated ? ComparisonOperator.IsNotNull : ComparisonOperator.IsNull);
        }

        bool notPrefixed = TryConsumeKeyword("not");

        if (TryConsumeKeyword("like"))
        {
            var pattern = ParseRequiredOperand();

            // The escape character travels on the comparison (decision 102), as the
            // character the query wrote; an escape that is not a string literal - a
            // parameter, which the grammar admits - is a fact about how the pattern reads
            // that the caller would supply, which the representation does not carry.
            string? escape = null;
            if (TryConsumeKeyword("escape"))
            {
                if (Current.Kind != TokenKind.String)
                {
                    unread ??= (
                        $"the escape '{Current.Text}' of a like, which is not a string literal; the query representation carries the escape as a character the query states",
                        null);
                    Advance();
                    return null;
                }

                escape = Current.Text;
                Advance();
            }

            if (left is null || pattern is null)
            {
                return null;
            }

            ConditionNode like = new ComparisonCondition(left, ComparisonOperator.Like, pattern, escape);
            return notPrefixed ? new NotCondition(like) : like;
        }

        if (TryConsumeKeyword("in"))
        {
            if (!AtSymbol("("))
            {
                throw Error("expected '(' after 'in'");
            }

            // IN carries three right sides: a subquery (decision 061), a list of values
            // (decision 074) and a collection parameter (decision 083). The first token
            // inside the parenthesis tells them apart - a lone parameter is the collection
            // the caller binds, which NHibernate expands into as many placeholders as it has
            // members.
            QueryOperand? members;
            if (NextIsSubQuery())
            {
                members = QueryOperand.Nested(ParseParenthesizedSubQuery());
            }
            else if (Next.Kind == TokenKind.Parameter && Ahead(2) is { Kind: TokenKind.Symbol, Text: ")" })
            {
                Advance();
                members = QueryOperand.Bound(ReadParameter(isCollection: true));
                ConsumeSymbol(")");
            }
            else
            {
                Advance();
                members = ParseValueList();
                ConsumeSymbol(")");
            }

            if (left is null || members is null)
            {
                return null;
            }

            ConditionNode inNode = new ComparisonCondition(left, ComparisonOperator.In, members);
            return notPrefixed ? new NotCondition(inNode) : inNode;
        }

        if (TryConsumeKeyword("between"))
        {
            var low = ParseRequiredOperand();
            ConsumeKeyword("and");
            var high = ParseRequiredOperand();
            if (left is null || low is null || high is null)
            {
                return null;
            }

            Report(
                ConversionRecordKind.Convention,
                "A between predicate was rewritten as a pair of comparisons (rule Q14).",
                QueryFeature.Filtering);

            ConditionNode pair = new LogicalCondition(LogicalOperator.And,
            [
                new ComparisonCondition(left, ComparisonOperator.GreaterThanOrEqual, low),
                new ComparisonCondition(left, ComparisonOperator.LessThanOrEqual, high),
            ]);

            return notPrefixed ? new NotCondition(pair) : pair;
        }

        if (notPrefixed)
        {
            throw Error("expected 'like', 'in' or 'between' after 'not'");
        }

        var op = ParseComparisonOperator();
        if (op is null)
        {
            // A bare operand — a boolean property, say — is no comparison the tree carries.
            return null;
        }

        // all, any or some between the operator and a subquery: a quantified comparison
        // (decision 119). HQL takes nothing but a subquery after the quantifier.
        if (TryConsumeQuantifier(out var quantifier, out var word))
        {
            if (!(AtSymbol("(") && NextIsSubQuery()))
            {
                throw Error($"expected a subquery after '{word}'");
            }

            var subQuery = QueryOperand.Nested(ParseParenthesizedSubQuery());
            if (left is null)
            {
                return null;
            }

            if (QuantifiedComparisons.RewriteNote(op.Value, quantifier) is { } note)
            {
                Report(ConversionRecordKind.Convention, note, QueryFeature.Subquery);
            }

            return ComparisonCondition.Quantified(left, op.Value, quantifier, subQuery);
        }

        var beforeRight = position;
        var right = ParseOperandOrSubQuery();
        if (right is null && position == beforeRight)
        {
            throw Error("expected a value, a property or a subquery");
        }

        if (left is null || right is null)
        {
            return null;
        }

        return TryReadEntityComparison(left, op.Value, right, out var entityComparison)
            ? entityComparison
            : new ComparisonCondition(left, op.Value, right);
    }

    /// <summary>The quantifier of decision 119 at the current token, consumed; <c>some</c> is <c>any</c>.</summary>
    private bool TryConsumeQuantifier(out Quantifier quantifier, out string word)
    {
        foreach (var (keyword, value) in new[] { ("all", Quantifier.All), ("any", Quantifier.Any), ("some", Quantifier.Any) })
        {
            if (TryConsumeKeyword(keyword))
            {
                quantifier = value;
                word = keyword;
                return true;
            }
        }

        quantifier = default;
        word = string.Empty;
        return false;
    }

    /// <summary>
    /// A comparison that names a whole entity rather than a value - a declared alias, or a
    /// single-valued association reached from one. <c>o.Customer = c</c> says the reference
    /// points at the row, which is the equality of the reference's foreign key columns with
    /// the key they reference, and is read as that, derived from the relation the way a join
    /// along the association is (decision 101); <c>&lt;&gt;</c> is its negation. Any other
    /// comparison of an entity - with a value, a parameter, another kind of row, or over a
    /// relation whose columns nobody states - is not read: the representation has no operand
    /// for an entity, and read as a column it came out as <c>c.*</c>, which no target parses.
    /// False where neither side names an entity. The same reading the JPQL parser takes; it
    /// is written here again because the two layers share no query code (S1).
    /// </summary>
    private bool TryReadEntityComparison(QueryOperand left, ComparisonOperator op, QueryOperand right, out ConditionNode? condition)
    {
        condition = null;
        if (!IsEntity(left) && !IsEntity(right))
        {
            return false;
        }

        if (op is ComparisonOperator.Equal or ComparisonOperator.NotEqual
            && (ReferenceEquality(left, right) ?? ReferenceEquality(right, left)) is { } equalities)
        {
            condition = op == ComparisonOperator.Equal ? equalities : new NotCondition(equalities);
            return true;
        }

        unread ??= ($"a comparison of '{Spelling(left)}' with '{Spelling(right)}' that names a whole entity rather than a value, which the query representation carries only as a single-valued association compared with the row it points at", null);
        return true;

        static string Spelling(QueryOperand operand)
            => operand is { IsColumn: true, Property: "*" } ? operand.Table! : operand.ToString();
    }

    /// <summary>
    /// The equalities a single-valued association compared with a row stands for, or null
    /// where the path is not such an association, the row is not the entity it leads to, or
    /// no columns are stated on either side of the relation.
    /// </summary>
    private ConditionNode? ReferenceEquality(QueryOperand path, QueryOperand row)
    {
        if (path is not { IsColumn: true, IsAggregate: false, Table: { } holder } || path.Property == "*" || !IsEntity(path)
            || row is not { IsColumn: true, IsAggregate: false, Property: "*", Table: { } rowAlias }
            || !aliases.TryGetValue(holder, out var owner) || owner is null
            || !aliases.TryGetValue(rowAlias, out var target) || target is null)
        {
            return null;
        }

        var relation = owner.Relations.FirstOrDefault(r =>
            r.Cardinality is Cardinality.ManyToOne or Cardinality.OneToOne
            && string.Equals(r.SourceNavigationProperty, path.Property, StringComparison.OrdinalIgnoreCase));
        if (relation is null || !ReferenceEquals(MapFor(relation.TargetEntity), target))
        {
            return null;
        }

        var pairs = ColumnPairsOf(relation, owner, target);
        return pairs.Count == 0 ? null : AssociationCondition(relation, pairs, holder, rowAlias, written: null);
    }

    /// <summary>
    /// A declared alias standing alone, or an association of the entity behind one rather
    /// than a property it maps - a whole entity either way.
    /// </summary>
    private bool IsEntity(QueryOperand operand)
        => operand is { IsColumn: true, IsAggregate: false, Table: { } alias }
           && aliases.TryGetValue(alias, out var map)
           && (operand.Property == "*"
               || (map is not null
                   && map.Relations.Any(r => string.Equals(r.SourceNavigationProperty, operand.Property, StringComparison.OrdinalIgnoreCase))
                   && !map.PropertyMaps.Any(p => string.Equals(p.Property.Name, operand.Property, StringComparison.OrdinalIgnoreCase))));

    /// <summary>
    /// <c>is null</c> or <c>is not null</c> over a whole entity. Over a single-valued
    /// association that owns its foreign key - <c>o.Customer is null</c> - it is the
    /// nullness of the key's columns, which the relation states the way a join along the
    /// association takes them (decision 101): every column null, or every column not null, a
    /// conjunction over a composite key. Read as a column, the association came out under
    /// its own name, a column no table has. Nothing else is read: the reference of an
    /// inverse side is the absence of a row on the side holding the key, a collection is
    /// never null, a declared alias alone has no column of its own, and a relation whose
    /// columns nobody states leaves nothing to test - each refused by name, never guessed.
    /// The same reading the JPQL parser takes; it is written here again because the two
    /// layers share no query code (S1).
    /// </summary>
    private ConditionNode? EntityNullness(QueryOperand path, bool negated)
    {
        var test = negated ? "is not null" : "is null";
        if (path.Property == "*")
        {
            unread ??= ($"the test '{path.Table} {test}' of the whole row of a declared alias, which the query representation carries only as a test of a column of that row", null);
            return null;
        }

        var spelling = $"{path.Table}.{path.Property}";
        var relation = aliases[path.Table!]!.Relations.First(r =>
            string.Equals(r.SourceNavigationProperty, path.Property, StringComparison.OrdinalIgnoreCase));

        var why = relation switch
        {
            { Cardinality: Cardinality.OneToMany or Cardinality.ManyToMany } =>
                "a collection, which is never null and whose emptiness the query representation does not test",
            { Role: RelationRole.Inverse } =>
                "a reference whose foreign key the other side holds, so its nullness is the absence of a row there, which is not derived",
            { ColumnPairs.Count: 0 } =>
                "a reference whose foreign key columns neither the relation states nor the database catalog supplied, so no column was there to test",
            _ => null,
        };

        if (why is not null)
        {
            unread ??= ($"the test '{spelling} {test}' of {why}", null);
            return null;
        }

        var op = negated ? ComparisonOperator.IsNotNull : ComparisonOperator.IsNull;
        var conjuncts = relation.ColumnPairs
            .Select(pair => (ConditionNode)new ComparisonCondition(
                QueryOperand.Column(path.Table, pair.Source.ColumnName ?? pair.Source.Property.Name),
                op))
            .ToList();

        return conjuncts.Count == 1 ? conjuncts[0] : new LogicalCondition(LogicalOperator.And, conjuncts);
    }

    /// <summary>
    /// An operand in a position the grammar requires one: nothing readable there is a
    /// syntax error with a position, while a consumed-but-uncarriable operand (a parameter)
    /// stays null for the clause to refuse.
    /// </summary>
    private QueryOperand? ParseRequiredOperand()
    {
        var before = position;
        var operand = ParseOperand();
        if (operand is null && position == before)
        {
            throw Error("expected a value or a property");
        }

        return operand;
    }

    /// <summary>Consumes a parenthesized value list without keeping it (see the IN branch).</summary>
    /// <summary>
    /// Reads the values an in list enumerates into a list operand (decision 074). Every
    /// element has to be a literal or, since decision 102, a scalar parameter: a null is
    /// no value the model carries (decision 002) and would make <c>not in</c> mean
    /// different things in HQL and in LINQ, and a property path is no value at all. Each
    /// of those sinks the clause, named, for the clause to refuse; the list is consumed to
    /// its end either way, so that reading goes on and every reason reaches the caller.
    /// </summary>
    private QueryOperand? ParseValueList()
    {
        var values = new List<QueryOperand>();
        bool carried = true;

        do
        {
            if (AtKeyword("null"))
            {
                unread ??= ("null among the values of an in list, which is no value the query representation carries", null);
                Advance();
                carried = false;
                continue;
            }

            var before = position;
            var element = ParseOperand();
            if (position == before)
            {
                throw Error("expected a value");
            }

            if (element is null)
            {
                unread ??= ("an element of an in list that is not a literal", null);
                carried = false;
                continue;
            }

            // A scalar parameter stands among the values (decision 102); its scalar comes
            // from the left side of in through the builder's parameter gate.
            if (element.IsParameter)
            {
                values.Add(element);
                continue;
            }

            if (!element.IsConstant || element.Function is not null)
            {
                unread ??= ($"'{element}' among the values of an in list, which is not a literal", null);
                carried = false;
                continue;
            }

            values.Add(element);
        }
        while (TryConsumeSymbol(","));

        return carried && values.Count > 0 ? QueryOperand.ValueList(values) : null;
    }

    private ComparisonOperator? ParseComparisonOperator()
    {
        if (Current.Kind != TokenKind.Symbol)
        {
            return null;
        }

        ComparisonOperator? op = Current.Text switch
        {
            "=" => ComparisonOperator.Equal,
            "<>" or "!=" => ComparisonOperator.NotEqual,
            ">" => ComparisonOperator.GreaterThan,
            ">=" => ComparisonOperator.GreaterThanOrEqual,
            "<" => ComparisonOperator.LessThan,
            "<=" => ComparisonOperator.LessThanOrEqual,
            _ => null,
        };

        if (op is not null)
        {
            Advance();
        }

        return op;
    }

    private bool NextIsSubQuery()
        => Next.Kind == TokenKind.Identifier
           && (string.Equals(Next.Text, "select", StringComparison.OrdinalIgnoreCase)
               || string.Equals(Next.Text, "from", StringComparison.OrdinalIgnoreCase));

    private SubQueryInstruction ParseParenthesizedSubQuery()
    {
        ConsumeSymbol("(");
        var sub = ParseSubQuery();
        ConsumeSymbol(")");
        return sub;
    }

    /// <summary>
    /// Reads a nested query body into a subquery operand (decision 061). The scope closes
    /// with PopOperand, so its instructions become the operand's body; the enclosing source
    /// alias and alias scope survive, with the inner aliases shadowing only inside.
    /// </summary>
    private SubQueryInstruction ParseSubQuery()
    {
        var enclosingAlias = sourceAlias;
        var enclosing = new Dictionary<string, EntityMap?>(aliases, StringComparer.OrdinalIgnoreCase);

        queryBuilder.Push();
        ParseQueryBody();
        sourceAlias = enclosingAlias;
        aliases = enclosing;

        return queryBuilder.PopOperand();
    }

    /* ---- operands ------------------------------------------------------------------- */

    /// <summary>
    /// An operand, which since decision 107 is an expression over the primary operands:
    /// the additive level - <c>+</c>, <c>-</c> and the concatenation <c>||</c> - over the
    /// multiplicative one, over the primaries. A null anywhere sinks the whole operand
    /// after every token of it has been consumed, so that the clause refuses by name.
    /// </summary>
    private QueryOperand? ParseOperand()
    {
        var left = ParseMultiplicative();

        while (Current.Kind == TokenKind.Symbol && Current.Text is "+" or "-" or "||")
        {
            var op = Current.Text switch
            {
                "+" => ExpressionOperator.Add,
                "-" => ExpressionOperator.Subtract,
                _ => ExpressionOperator.Concat,
            };
            Advance();

            var right = ParseMultiplicative();
            left = Combine(op, left, right);
        }

        return left;
    }

    private QueryOperand? ParseMultiplicative()
    {
        var left = ParsePrimaryOperand();

        while (Current.Kind == TokenKind.Symbol && Current.Text is "*" or "/" or "%")
        {
            var op = Current.Text switch
            {
                "*" => ExpressionOperator.Multiply,
                "/" => ExpressionOperator.Divide,
                _ => ExpressionOperator.Modulo,
            };
            Advance();

            var right = ParsePrimaryOperand();
            left = Combine(op, left, right);
        }

        return left;
    }

    private QueryOperand? Combine(ExpressionOperator op, QueryOperand? left, QueryOperand? right)
    {
        if (left is null || right is null || !IsLeaf(left) || !IsLeaf(right))
        {
            return null;
        }

        return QueryOperand.Computed(QueryExpression.Binary(op, left, right));
    }

    /// <summary>A leaf of an expression: a collection parameter has no place in one (decision 107), and the factory would refuse it; refused here first, by name.</summary>
    private bool IsLeaf(QueryOperand operand)
    {
        if (operand.IsValueList || operand.Parameter?.IsCollection == true)
        {
            unread ??= ($"the collection '{operand}' inside an expression, which no target computes with", QueryFeature.Expression);
            return false;
        }

        return true;
    }

    private QueryOperand? ParsePrimaryOperand()
    {
        if (Current.Kind == TokenKind.String)
        {
            var text = Current.Text;
            Advance();
            return QueryOperand.Value(QueryConstant.Of(text, ScalarType.String));
        }

        if (Current.Kind == TokenKind.Number)
        {
            var text = Current.Text;
            Advance();
            return QueryOperand.Value(NumberConstant(text));
        }

        if (AtSymbol("-") && Next.Kind == TokenKind.Number)
        {
            Advance();
            var text = "-" + Current.Text;
            Advance();
            return QueryOperand.Value(NumberConstant(text));
        }

        // A parenthesis opens a scalar subquery standing as a leaf of an expression, or a
        // grouped expression (decision 107).
        if (AtSymbol("("))
        {
            if (NextIsSubQuery())
            {
                return QueryOperand.Nested(ParseParenthesizedSubQuery());
            }

            Advance();
            var grouped = ParseOperand();
            ConsumeSymbol(")");
            return grouped;
        }

        if (Current.Kind == TokenKind.Parameter)
        {
            return QueryOperand.Bound(ReadParameter());
        }

        if (Current.Kind != TokenKind.Identifier)
        {
            return null;
        }

        if (string.Equals(Current.Text, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Current.Text, "false", StringComparison.OrdinalIgnoreCase))
        {
            var text = Current.Text.ToLowerInvariant();
            Advance();
            return QueryOperand.Value(QueryConstant.Of(text, ScalarType.Bool));
        }

        if (AtKeyword("null"))
        {
            // A bare null belongs to IS NULL (decision 002); as a comparison operand it is
            // no value the model carries.
            Advance();
            return null;
        }

        if (AtKeyword("case"))
        {
            return ParseCase();
        }

        if (IsAggregate(Current.Text) && Next is { Kind: TokenKind.Symbol, Text: "(" })
        {
            return ParseAggregate();
        }

        if (Next is { Kind: TokenKind.Symbol, Text: "(" })
        {
            return ParseFunctionCall();
        }

        var reference = ParsePath();
        if (reference is null)
        {
            return null;
        }

        // A bare declared alias is the whole entity - the projection reads it as rule Q3's
        // absence of a projection.
        if (reference.Qualifier is null && aliases.ContainsKey(reference.Attribute))
        {
            return QueryOperand.Column(reference.Attribute, "*");
        }

        return QueryOperand.Column(reference.Qualifier, ColumnFor(reference.Qualifier, reference.Attribute));
    }

    /* ---- expressions (decision 107) --------------------------------------------------- */

    /// <summary>
    /// One of the five aggregates: over the whole row (<c>count(*)</c>, or the count of a
    /// declared alias), over a property, or over an expression (decision 107), with
    /// <c>distinct</c> inside the function as the modifier of the aggregate (decision 102).
    /// </summary>
    private QueryOperand? ParseAggregate()
    {
        var function = Current.Text.ToUpperInvariant();
        Advance();
        Advance();
        var distinct = TryConsumeKeyword("distinct");

        QueryOperand? aggregated;
        if (TryConsumeSymbol("*"))
        {
            aggregated = QueryOperand.Column(null, "*", function, distinct);
        }
        else
        {
            var argument = ParseOperand();
            if (argument is null || !IsLeaf(argument))
            {
                aggregated = null;
            }
            else if (argument.IsAggregate)
            {
                // max(sum(x)): the operand holds one aggregate, so rebuilt under the outer
                // function it would lose the inner one and compute another value. No target
                // writes an aggregate over an aggregate (decision 107); refused here, in the
                // words of the template's gate, which sees only one inside an expression.
                Report(ConversionRecordKind.Failure,
                    $"The aggregate {function} stands over '{argument}', which is an aggregate itself, and no target writes an aggregate over an aggregate; no artifact was generated.",
                    QueryFeature.Expression);
                aggregated = argument;
            }
            else if (argument is { IsColumn: true, IsAggregate: false, Property: "*" })
            {
                aggregated = QueryOperand.Column(null, "*", function, distinct);
            }
            else if (argument.IsColumn)
            {
                aggregated = QueryOperand.Column(argument.Table, argument.Property!, function, distinct);
            }
            else if (argument.IsConstant)
            {
                aggregated = QueryOperand.Value(argument.Constant!, function);
            }
            else if (argument.IsParameter)
            {
                aggregated = QueryOperand.Bound(argument.Parameter!, function);
            }
            else if (argument.IsExpression)
            {
                aggregated = QueryOperand.Computed(argument.Expression!, function, distinct);
            }
            else
            {
                unread ??= ($"an aggregate over '{argument}'", QueryFeature.Aggregation);
                aggregated = null;
            }
        }

        ConsumeSymbol(")");
        return aggregated;
    }

    /// <summary>
    /// A call of a function under its HQL name (decision 107): the vocabulary in lower case,
    /// <c>concat</c> of any number of arguments nested, <c>mod</c> as the modulo,
    /// <c>current_timestamp()</c> with its parentheses. A name outside the vocabulary is
    /// consumed to its closing parenthesis and refused by name.
    /// </summary>
    private QueryOperand? ParseFunctionCall()
    {
        var name = Current.Text.ToLowerInvariant();
        var line = Current.Line;
        var column = Current.Column;

        if (name == "cast")
        {
            return ParseCast();
        }

        Advance();
        ConsumeSymbol("(");

        var arguments = new List<QueryOperand>();
        var carried = true;
        if (!AtSymbol(")"))
        {
            do
            {
                var start = position;
                var argument = ParseOperand();
                if (argument is null && position == start)
                {
                    throw new HqlParseError(line, column, $"expected an argument of {name}");
                }

                if (argument is null || !IsLeaf(argument))
                {
                    carried = false;
                    continue;
                }

                arguments.Add(argument);
            }
            while (TryConsumeSymbol(","));
        }

        ConsumeSymbol(")");

        if (!carried)
        {
            return null;
        }

        switch (name)
        {
            case "concat":
                if (arguments.Count < 2)
                {
                    unread ??= ($"concat with {arguments.Count} argument(s), which takes at least two", QueryFeature.Expression);
                    return null;
                }

                return arguments.Skip(1).Aggregate(arguments[0], (left, right) => QueryOperand.Computed(QueryExpression.Binary(ExpressionOperator.Concat, left, right)));

            case "mod":
                if (arguments.Count != 2)
                {
                    unread ??= ($"mod with {arguments.Count} argument(s), which takes two", QueryFeature.Expression);
                    return null;
                }

                return QueryOperand.Computed(QueryExpression.Binary(ExpressionOperator.Modulo, arguments[0], arguments[1]));

            case "substring":
                // The two-argument form takes the rest of the text; the model carries three
                // arguments and the length of the text selects the same characters.
                if (arguments.Count == 2)
                {
                    arguments.Add(QueryOperand.Computed(QueryExpression.Call(QueryFunction.Length, [arguments[0]])));
                }

                return Call(QueryFunction.Substring, arguments, name);
        }

        QueryFunction? function = name switch
        {
            "upper" => QueryFunction.Upper,
            "lower" => QueryFunction.Lower,
            "trim" => QueryFunction.Trim,
            "length" => QueryFunction.Length,
            "coalesce" => QueryFunction.Coalesce,
            "abs" => QueryFunction.Abs,
            "year" => QueryFunction.Year,
            "month" => QueryFunction.Month,
            "day" => QueryFunction.Day,
            "current_timestamp" => QueryFunction.CurrentTimestamp,
            "round" => QueryFunction.Round,
            "sqrt" => QueryFunction.Sqrt,
            _ => null,
        };

        if (function is null)
        {
            unread ??= ($"the function {name}, which is outside the vocabulary of expressions the query representation carries", QueryFeature.Expression);
            return null;
        }

        return Call(function.Value, arguments, name);
    }

    /// <summary>
    /// <c>cast(x as type)</c> under the name of an NHibernate type (decision 113), read into
    /// the five scalars the vocabulary converts into: <c>int</c>/<c>Int32</c>,
    /// <c>long</c>/<c>Int64</c>, <c>float</c>/<c>single</c>, <c>double</c> and <c>string</c>,
    /// in any case. NHibernate writes the last as NVARCHAR(4000), which is the same text as
    /// the model's NVARCHAR(MAX) wherever the value converted is no text longer than that.
    /// Any other type is refused by name.
    /// </summary>
    private QueryOperand? ParseCast()
    {
        Advance();
        ConsumeSymbol("(");
        var value = ParseOperand();
        ConsumeKeyword("as");

        if (Current.Kind != TokenKind.Identifier)
        {
            throw Error("expected the name of a type after 'as'");
        }

        var type = Current.Text;
        Advance();
        ConsumeSymbol(")");

        ScalarType? scalar = type.ToLowerInvariant() switch
        {
            "int" or "int32" or "integer" => ScalarType.Int,
            "long" or "int64" => ScalarType.Long,
            "float" or "single" => ScalarType.Float,
            "double" => ScalarType.Double,
            "string" => ScalarType.String,
            _ => null,
        };

        if (scalar is null)
        {
            unread ??= ($"the conversion into {type}, which is not one of the scalars the vocabulary of expressions converts into", QueryFeature.Expression);
            return null;
        }

        return value is null || !IsLeaf(value) ? null : QueryOperand.Computed(QueryExpression.Call(QueryFunction.Cast, [value], castTo: scalar));
    }

    private QueryOperand? Call(QueryFunction function, List<QueryOperand> arguments, string name)
    {
        var (least, most) = QueryExpression.Arity(function);
        if (arguments.Count < least || arguments.Count > most)
        {
            unread ??= ($"{name} with {arguments.Count} argument(s), a number the function of the vocabulary does not take", QueryFeature.Expression);
            return null;
        }

        return QueryOperand.Computed(QueryExpression.Call(function, arguments));
    }

    /// <summary>
    /// A searched <c>case when … then … [else …] end</c>, or a simple <c>case x when v then …</c>
    /// read as the searched form with an equality per branch (decision 107).
    /// </summary>
    private QueryOperand? ParseCase()
    {
        ConsumeKeyword("case");
        var carried = true;

        QueryOperand? input = null;
        if (!AtKeyword("when"))
        {
            input = ParseOperand();
            carried &= input is not null && IsLeaf(input);
        }

        var branches = new List<CaseBranch>();
        while (TryConsumeKeyword("when"))
        {
            ConditionNode? when;
            if (input is null && !carried)
            {
                // The input was not carried; the branch is consumed for the syntax only.
                ParseOperand();
                when = null;
            }
            else if (input is not null)
            {
                var value = ParseOperand();
                when = value is null || !IsLeaf(value) ? null : new ComparisonCondition(input, ComparisonOperator.Equal, value);
            }
            else
            {
                when = ParseCondition();
            }

            ConsumeKeyword("then");
            var then = ParseOperand();

            if (when is null || then is null || !IsLeaf(then))
            {
                carried = false;
                continue;
            }

            branches.Add(new CaseBranch(when, then));
        }

        QueryOperand? otherwise = null;
        if (TryConsumeKeyword("else"))
        {
            if (AtKeyword("null"))
            {
                Advance();
            }
            else
            {
                otherwise = ParseOperand();
                carried &= otherwise is not null && IsLeaf(otherwise);
            }
        }

        ConsumeKeyword("end");

        if (!carried || branches.Count == 0)
        {
            unread ??= ("a case with a branch the query representation does not carry", QueryFeature.Expression);
            return null;
        }

        return QueryOperand.Computed(QueryExpression.Case(branches, otherwise));
    }

    /// <summary>
    /// One parameter token as the model carries it (decision 083): <c>:id</c> is the name
    /// id, the colon being HQL's decoration and stripped like the quotes of a string, and a
    /// bare <c>?</c> is positional with the order of its occurrence in the text, counted
    /// from one. The order is counted here because nothing else in the text states it - HQL
    /// says "the next one" and the model needs the number. A named one carries the scalar a
    /// &lt;query-param&gt; declared for it, if any; a positional one has no name to declare by.
    /// </summary>
    private QueryParameter ReadParameter(bool isCollection = false)
    {
        var text = Current.Text;
        Advance();

        if (text == "?")
        {
            return QueryParameter.Positional(++positionalParameters, isCollection: isCollection);
        }

        var name = text[1..];
        ScalarType? stated = statedScalars is not null && statedScalars.TryGetValue(name, out var scalar) ? scalar : null;

        return QueryParameter.Named(name, stated, isCollection);
    }

    private int positionalParameters;

    private QueryOperand? ParseOperandOrSubQuery()
    {
        if (AtSymbol("(") && NextIsSubQuery())
        {
            return QueryOperand.Nested(ParseParenthesizedSubQuery());
        }

        return ParseOperand();
    }

    private sealed record PathReference(string? Qualifier, string Attribute);

    /// <summary>
    /// alias.Property or a bare property. Three or more segments navigate an association,
    /// which the flat operand does not carry — the tokens are consumed and null is the
    /// answer, so the enclosing clause refuses the artifact.
    /// </summary>
    private PathReference? ParsePath()
    {
        var parts = ParseDottedName("expected a property reference");

        return parts.Count switch
        {
            1 => new PathReference(null, parts[0]),
            2 => new PathReference(parts[0], parts[1]),
            _ => null,
        };
    }

    private static QueryConstant NumberConstant(string text)
        => text.Contains('.')
            ? QueryConstant.Of(text, ScalarType.Decimal)
            : QueryConstant.Of(text, ScalarType.Int);

    private static bool IsAggregate(string name)
        => name.ToUpperInvariant() is "COUNT" or "SUM" or "MIN" or "MAX" or "AVG";

    /* ---- names through the mapping IR ----------------------------------------------- */

    private EntityMap? MapFor(string entity)
        => maps?.FirstOrDefault(m => string.Equals(m.Entity?.Name, entity, StringComparison.OrdinalIgnoreCase));

    private static string TableFor(EntityMap? map, string entity)
    {
        if (map is null)
        {
            return entity;
        }

        var table = map.Table ?? entity;
        return string.IsNullOrWhiteSpace(map.Schema) ? table : $"{map.Schema}.{table}";
    }

    /// <summary>
    /// The column a property maps to — the exact inverse of the builder visitor's
    /// column-to-property lookup, with the same fallback: a name the maps do not know
    /// passes through verbatim.
    /// </summary>
    private string ColumnFor(string? qualifier, string property)
    {
        aliases.TryGetValue(qualifier ?? sourceAlias, out var map);

        return map?.PropertyMaps
                   .FirstOrDefault(p => string.Equals(p.Property.Name, property, StringComparison.OrdinalIgnoreCase))
                   ?.ColumnName
               ?? property;
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

    private void Report(ConversionRecordKind kind, string reason, QueryFeature? feature = null)
        => queryBuilder.Report(new ConversionRecord
        {
            Kind = kind,
            Framework = queryBuilder.Descriptor.Framework,
            Artifact = ConversionContentType.HqlQuery,
            Feature = feature,
            Reason = reason,
        });
}
