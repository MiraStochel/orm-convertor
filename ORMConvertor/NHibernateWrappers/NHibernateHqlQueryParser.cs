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
/// </summary>
public class NHibernateHqlQueryParser(Func<AbstractQueryBuilder> queryBuilders) : IQueryParser
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
        "and", "or", "not", "like", "in", "is", "null", "between", "exists",

        // Not part of the read subset - HQL in NHibernate 5.7.0 has no set operations - but
        // reserved so that "from Customer union ..." fails as a syntax error instead of
        // taking "union" for an alias.
        "union", "intersect", "except", "all",
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

            if (i + 1 < source.Length && source.Substring(i, 2) is ("<>" or "<=" or ">=" or "!=") and var pair)
            {
                read.Add(new Token(TokenKind.Symbol, pair, startLine, startColumn));
                i += 2;
                column += 2;
                continue;
            }

            if (c is '(' or ')' or ',' or '.' or '*' or '=' or '<' or '>' or '-')
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
        var projections = new List<Projection>();
        if (TryConsumeKeyword("select"))
        {
            // DISTINCT is a property of the whole projection, carried per (sub)query scope
            // (decision 073); it used to be refused here (decision 070).
            if (TryConsumeKeyword("distinct"))
            {
                queryBuilder.Distinct();
            }

            do
            {
                projections.Add(ParseProjection());
            }
            while (TryConsumeSymbol(","));
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

        EmitProjections(projections);

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
                if (ParsePath() is { } key)
                {
                    queryBuilder.GroupBy(key.Qualifier ?? sourceAlias, ColumnFor(key.Qualifier, key.Attribute));
                }
                else
                {
                    // Grouping decides which rows come back (decision 070).
                    Report(
                        ConversionRecordKind.Failure,
                        "A grouping key that is not a property reference cannot be carried, and a query grouped differently would return different rows; no artifact was generated.",
                        QueryFeature.Grouping);
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
                var key = ParsePath();
                bool asc = true;
                if (TryConsumeKeyword("desc"))
                {
                    asc = false;
                }
                else
                {
                    TryConsumeKeyword("asc");
                }

                if (key is null)
                {
                    Report(
                        ConversionRecordKind.Loss,
                        "An ordering key that is not a property reference was dropped.",
                        QueryFeature.Ordering);
                }
                else
                {
                    queryBuilder.OrderBy(key.Qualifier, ColumnFor(key.Qualifier, key.Attribute), asc);
                }
            }
            while (TryConsumeSymbol(","));
        }
    }

    private sealed record Projection(string? Function, PathReference? Path, string? Alias);

    private Projection ParseProjection()
    {
        string? function = null;
        PathReference? path;

        if (Current.Kind == TokenKind.Identifier && IsAggregate(Current.Text) && Next is { Kind: TokenKind.Symbol, Text: "(" })
        {
            function = Current.Text.ToUpperInvariant();
            Advance();
            Advance();
            path = TryConsumeSymbol("*") ? new PathReference(null, "*") : ParsePath();
            ConsumeSymbol(")");
        }
        else
        {
            path = ParsePath();
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

        return new Projection(function, path, alias);
    }

    private void EmitProjections(List<Projection> projections)
    {
        foreach (var projection in projections)
        {
            if (projection.Path is null)
            {
                Report(
                    ConversionRecordKind.Loss,
                    "A projected expression that is not a property reference or an aggregate was dropped.",
                    QueryFeature.Projection);
                continue;
            }

            var (qualifier, attribute) = (projection.Path.Qualifier, projection.Path.Attribute);

            // A bare declared alias projects the whole entity, which rule Q3 spells as the
            // absence of a projection — the same reading LINQ's Select(c => c) gets.
            if (projection.Function is null && qualifier is null && aliases.ContainsKey(attribute))
            {
                if (projections.Count > 1)
                {
                    Report(
                        ConversionRecordKind.Loss,
                        $"The whole-entity projection '{attribute}' next to other columns is not carried by the query representation; it was dropped.",
                        QueryFeature.Projection);
                }

                continue;
            }

            if (attribute == "*")
            {
                queryBuilder.Project(sourceAlias, "*", projection.Alias, projection.Function);
                continue;
            }

            queryBuilder.Project(
                qualifier ?? sourceAlias,
                ColumnFor(qualifier, attribute),
                projection.Alias,
                projection.Function);
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

        // alias.Property is an association path, whose predicate lives in the mapping;
        // JoinInstruction carries two tables and an explicit condition, so the join cannot
        // be carried - and a query emitted without its join returns different rows, so it
        // refuses the artifact (decision 070), the road an unreadable join takes in the
        // Dapper parser too.
        if (parts.Count > 1 && aliases.ContainsKey(parts[0]))
        {
            if (ParseOptionalAlias() is { } pathAlias)
            {
                aliases[pathAlias] = null;
            }

            if (TryConsumeKeyword("with"))
            {
                ParseCondition();
                unread = null;
            }

            Report(
                ConversionRecordKind.Failure,
                $"A join along the association path '{string.Join('.', parts)}' is not carried by the query representation, and a query emitted without its join would return different rows; no artifact was generated.",
                QueryFeature.Join);
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
            return left is null
                ? null
                : new ComparisonCondition(left, negated ? ComparisonOperator.IsNotNull : ComparisonOperator.IsNull);
        }

        bool notPrefixed = TryConsumeKeyword("not");

        if (TryConsumeKeyword("like"))
        {
            var pattern = ParseRequiredOperand();
            if (left is null || pattern is null)
            {
                return null;
            }

            ConditionNode like = new ComparisonCondition(left, ComparisonOperator.Like, pattern);
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

        var beforeRight = position;
        var right = ParseOperandOrSubQuery();
        if (right is null && position == beforeRight)
        {
            throw Error("expected a value, a property or a subquery");
        }

        return left is null || right is null ? null : new ComparisonCondition(left, op.Value, right);
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
    /// element has to be a literal, because the list carries values the query itself
    /// states: a parameter, which decision 083 keeps out of the list even though it gave it
    /// an operand of its own, a null is no value the model
    /// carries (decision 002) and would make <c>not in</c> mean different things in HQL and
    /// in LINQ, and a property path is no value at all. Each sinks the clause, named, for
    /// the clause to refuse; the list is consumed to its end either way, so that reading
    /// goes on and every reason reaches the caller.
    /// </summary>
    private QueryOperand? ParseValueList()
    {
        var values = new List<QueryConstant>();
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

            if (element.IsParameter)
            {
                unread ??= (
                    $"the parameter '{(before < position ? tokens[before].Text : element.ToString())}' among the values of an in list, which carries only values the query itself states",
                    QueryFeature.QueryParameter);
                carried = false;
                continue;
            }

            if (!element.IsConstant || element.Function is not null)
            {
                unread ??= ($"'{element}' among the values of an in list, which is not a literal", null);
                carried = false;
                continue;
            }

            values.Add(element.Constant!);
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

    private QueryOperand? ParseOperand()
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

        if (IsAggregate(Current.Text) && Next is { Kind: TokenKind.Symbol, Text: "(" })
        {
            var function = Current.Text.ToUpperInvariant();
            Advance();
            Advance();

            QueryOperand? aggregated;
            if (TryConsumeSymbol("*"))
            {
                aggregated = QueryOperand.Column(null, "*", function);
            }
            else
            {
                var path = ParsePath();
                aggregated = path is null
                    ? null
                    : QueryOperand.Column(path.Qualifier, ColumnFor(path.Qualifier, path.Attribute), function);
            }

            ConsumeSymbol(")");
            return aggregated;
        }

        var reference = ParsePath();
        return reference is null
            ? null
            : QueryOperand.Column(reference.Qualifier, ColumnFor(reference.Qualifier, reference.Attribute));
    }

    /// <summary>
    /// One parameter token as the model carries it (decision 083): <c>:id</c> is the name
    /// id, the colon being HQL's decoration and stripped like the quotes of a string, and a
    /// bare <c>?</c> is positional with the order of its occurrence in the text, counted
    /// from one. The order is counted here because nothing else in the text states it - HQL
    /// says "the next one" and the model needs the number.
    /// </summary>
    private QueryParameter ReadParameter(bool isCollection = false)
    {
        var text = Current.Text;
        Advance();

        return text == "?"
            ? QueryParameter.Positional(++positionalParameters, isCollection: isCollection)
            : QueryParameter.Named(text[1..], isCollection: isCollection);
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
