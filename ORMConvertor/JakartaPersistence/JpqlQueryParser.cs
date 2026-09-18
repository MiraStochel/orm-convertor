using System.Text;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using JavaEntityParsing;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace JakartaPersistence;

/// <summary>
/// Reads JPQL into the query IR (decision 077): a hand-written recursive descent over the
/// subset the IR carries, the shape decision 062 chose for HQL, because the only
/// reference parsers of JPQL live inside the implementations (S1). The grammar is pinned
/// by the round trip against the JPQL builder; a syntax error refuses the artifact with a
/// line and a column, and a construct outside the model gets the record the other query
/// parsers issue in the same situation (decisions 010 and 070).
///
/// Two languages are claimed (decision 025): bare JPQL, and a Java method wrapping the
/// JPQL in a createQuery call, from which the string literal is taken - the two phases the
/// Dapper parser has for SQL in C#. What an implementation's dialect adds over JPQL is
/// the fourth hook of decision 076: <see cref="TryReadDialectClause"/>.
/// </summary>
public abstract class JpqlQueryParser(Func<AbstractQueryBuilder> queryBuilders) : IQueryParser
{
    protected enum TokenKind { Identifier, Number, String, Symbol, Parameter, End }

    protected readonly record struct Token(TokenKind Kind, string Text, int Line, int Column);

    protected sealed class JpqlParseError(int line, int column, string message) : Exception(message)
    {
        public int Line { get; } = line;

        public int Column { get; } = column;
    }

    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "select", "distinct", "new", "from", "as", "inner", "left", "right", "full", "outer",
        "join", "fetch", "on", "with", "where", "group", "having", "order", "by", "asc", "desc",
        "and", "or", "not", "like", "in", "is", "null", "between", "exists",
        "union", "intersect", "except", "all", "limit", "offset",
    };

    /// <summary>
    /// The builder of the query being read. Assigned at the start of every Parse from the
    /// factory the orchestration supplied: one query, one fresh builder (decision 081). The
    /// parser may not make one itself - a builder belongs to the target framework and this
    /// parser to the source (S1) - and it is never touched outside a Parse call.
    /// </summary>
    protected AbstractQueryBuilder queryBuilder = default!;

    private List<Token> tokens = [];
    private int position;
    private IReadOnlyList<EntityMap>? maps;
    private string sourceAlias = "e";

    /// <summary>The declared aliases with their entities; a subquery sees the enclosing ones and shadows them.</summary>
    private Dictionary<string, EntityMap?> aliases = new(StringComparer.OrdinalIgnoreCase);

    private (string What, QueryFeature? Category)? unread;

    public bool CanParse(ConversionContentType contentType)
        => contentType is ConversionContentType.JpqlQuery or ConversionContentType.JavaQuery;

    public IReadOnlyCollection<AbstractQueryBuilder> Parse(ConversionContentType contentType, string source, IReadOnlyList<EntityMap>? entityMaps = null)
    {
        queryBuilder = queryBuilders();
        maps = entityMaps;
        aliases = new Dictionary<string, EntityMap?>(StringComparer.OrdinalIgnoreCase);

        // The builder leaves on every path, refused ones included: it holds the records of
        // what went wrong, and only the parser can say that this unit yielded a query at all
        // (decision 081).
        var jpql = contentType == ConversionContentType.JavaQuery ? ExtractQueryLiteral(source) : source;
        if (jpql is null)
        {
            return [queryBuilder];
        }

        queryBuilder.Push();
        try
        {
            tokens = Lex(jpql);
            position = 0;
            ParseQueryBody();

            while (TryParseSetOperator() is { } operation)
            {
                queryBuilder.Pop();
                queryBuilder.SetOperation(operation);
                queryBuilder.Push();
                ParseQueryBody();
            }

            if (Current.Kind != TokenKind.End)
            {
                throw Error("expected the end of the query");
            }
        }
        catch (JpqlParseError error)
        {
            Report(ConversionRecordKind.Failure,
                $"The JPQL could not be parsed at line {error.Line}, column {error.Column}: {error.Message}.");
        }

        queryBuilder.Pop();

        return [queryBuilder];
    }

    /// <summary>
    /// The JPQL inside a Java method: the first string literal handed to createQuery or
    /// createSelectionQuery, whether a plain literal or a text block. A query composed at
    /// run time - concatenation, a variable - is the same case the Dapper parser reports for
    /// SQL that is not a literal (decision 026): an incompleteness, and nothing to read.
    /// </summary>
    private string? ExtractQueryLiteral(string source)
    {
        List<JavaToken> javaTokens;
        try
        {
            javaTokens = JavaLexer.Lex(source);
        }
        catch (JavaSyntaxError error)
        {
            Report(ConversionRecordKind.Failure,
                $"The Java source could not be read at line {error.Line}, column {error.Column}: {error.Message}.");
            return null;
        }

        for (var i = 0; i + 2 < javaTokens.Count; i++)
        {
            if (javaTokens[i].Kind == JavaTokenKind.Identifier
                && javaTokens[i].Text is "createQuery" or "createSelectionQuery"
                && javaTokens[i + 1] is { Kind: JavaTokenKind.Symbol, Text: "(" })
            {
                if (javaTokens[i + 2].Kind == JavaTokenKind.String
                    && !(javaTokens[i + 3] is { Kind: JavaTokenKind.Symbol, Text: "+" }))
                {
                    return javaTokens[i + 2].Text;
                }

                Report(ConversionRecordKind.Incompleteness,
                    "The query handed to createQuery is not a string literal - it is composed at run time - so the parser has nothing to read.");
                return null;
            }
        }

        Report(ConversionRecordKind.Incompleteness,
            "The Java source calls neither createQuery nor createSelectionQuery with a literal, so no JPQL was found to read.");
        return null;
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
                        throw new JpqlParseError(startLine, startColumn, "unterminated string literal");
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

            if (c == ':' || c == '?')
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

            if (i + 1 < source.Length && source.Substring(i, 2) is ("<>" or "<=" or ">=" or "!=") and var pair)
            {
                read.Add(new Token(TokenKind.Symbol, pair, startLine, startColumn));
                i += 2;
                column += 2;
                continue;
            }

            if (c is '(' or ')' or ',' or '.' or '*' or '=' or '<' or '>' or '-' or '{' or '}')
            {
                read.Add(new Token(TokenKind.Symbol, c.ToString(), startLine, startColumn));
                i++;
                column++;
                continue;
            }

            throw new JpqlParseError(startLine, startColumn, $"unexpected character '{c}'");
        }

        read.Add(new Token(TokenKind.End, string.Empty, line, column));
        return read;
    }

    /* ---- token helpers -------------------------------------------------------------- */

    protected Token Current => tokens[position];

    protected Token Next => tokens[Math.Min(position + 1, tokens.Count - 1)];

    protected void Advance() => position++;

    protected bool AtKeyword(string keyword)
        => Current.Kind == TokenKind.Identifier && string.Equals(Current.Text, keyword, StringComparison.OrdinalIgnoreCase);

    protected bool TryConsumeKeyword(string keyword)
    {
        if (!AtKeyword(keyword))
        {
            return false;
        }

        Advance();
        return true;
    }

    protected void ConsumeKeyword(string keyword)
    {
        if (!TryConsumeKeyword(keyword))
        {
            throw Error($"expected '{keyword}'");
        }
    }

    protected bool AtSymbol(string symbol) => Current.Kind == TokenKind.Symbol && Current.Text == symbol;

    protected bool TryConsumeSymbol(string symbol)
    {
        if (!AtSymbol(symbol))
        {
            return false;
        }

        Advance();
        return true;
    }

    protected void ConsumeSymbol(string symbol)
    {
        if (!TryConsumeSymbol(symbol))
        {
            throw Error($"expected '{symbol}'");
        }
    }

    /// <summary>A non-negative integer literal in a position that needs one - a limit, an offset.</summary>
    protected long ConsumeInteger()
    {
        if (Current.Kind != TokenKind.Number || !long.TryParse(Current.Text, out var value))
        {
            throw Error("expected an integer");
        }

        Advance();
        return value;
    }

    protected JpqlParseError Error(string message) => new(
        Current.Line,
        Current.Column,
        Current.Kind == TokenKind.End ? $"{message}, found the end of the query" : $"{message}, found '{Current.Text}'");

    /* ---- clauses -------------------------------------------------------------------- */

    private SetOperationType? TryParseSetOperator()
    {
        if (TryConsumeKeyword("union"))
        {
            return TryConsumeKeyword("all") ? SetOperationType.UnionAll : SetOperationType.Union;
        }

        if (TryConsumeKeyword("intersect"))
        {
            if (TryConsumeKeyword("all"))
            {
                Report(ConversionRecordKind.Failure,
                    "INTERSECT ALL has no form in the query representation; no artifact was generated.",
                    QueryFeature.SetOperation);
            }

            return SetOperationType.Intersect;
        }

        if (TryConsumeKeyword("except"))
        {
            return TryConsumeKeyword("all") ? SetOperationType.ExceptAll : SetOperationType.Except;
        }

        return null;
    }

    /// <summary>
    /// One (sub)query body in JPQL's clause order: the select clause first, emitted after
    /// the source and the joins because a whole-entity projection needs the declared
    /// aliases (decision 023). The select clause is optional on reading - HQL admits its
    /// absence - and always written on emission.
    /// </summary>
    private void ParseQueryBody()
    {
        var projections = new List<Projection>();
        if (TryConsumeKeyword("select"))
        {
            if (TryConsumeKeyword("distinct"))
            {
                queryBuilder.Distinct();
            }

            if (TryConsumeKeyword("new"))
            {
                // A constructor expression names a DTO the representation does not carry;
                // the projected columns inside it are read as they are (decision 070: the
                // output is poorer, not different).
                var dto = ParseDottedName("expected a class name after 'new'");
                Report(ConversionRecordKind.Loss,
                    $"The constructor expression new {string.Join('.', dto)}(...) names a result class the query representation does not carry; the projected columns are kept and the class is dropped.",
                    QueryFeature.Projection);
                ConsumeSymbol("(");
                do
                {
                    projections.Add(ParseProjection());
                }
                while (TryConsumeSymbol(","));

                ConsumeSymbol(")");
            }
            else
            {
                do
                {
                    projections.Add(ParseProjection());
                }
                while (TryConsumeSymbol(","));
            }
        }

        ConsumeKeyword("from");
        var (table, alias) = ParseEntityReference();
        sourceAlias = alias;
        queryBuilder.From(table, alias);

        while (TryConsumeSymbol(","))
        {
            Report(ConversionRecordKind.Failure,
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
                    Report(ConversionRecordKind.Failure,
                        "A grouping key that is not an attribute reference cannot be carried, and a query grouped differently would return different rows; no artifact was generated.",
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
                    Report(ConversionRecordKind.Loss, "An ordering key that is not an attribute reference was dropped.", QueryFeature.Ordering);
                }
                else
                {
                    queryBuilder.OrderBy(key.Qualifier, ColumnFor(key.Qualifier, key.Attribute), asc);
                }
            }
            while (TryConsumeSymbol(","));
        }

        while (TryReadDialectClause())
        {
        }
    }

    /// <summary>
    /// The fourth hook of decision 076: a clause the implementation's dialect adds over
    /// JPQL after the standard ones. The base reads none; an override consumes its clause
    /// and returns true, or leaves the tokens and returns false.
    /// </summary>
    protected virtual bool TryReadDialectClause() => false;

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
        else if (Current.Kind == TokenKind.Identifier && !Keywords.Contains(Current.Text))
        {
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
                Report(ConversionRecordKind.Loss, "A projected expression that is not an attribute reference or an aggregate was dropped.", QueryFeature.Projection);
                continue;
            }

            var (qualifier, attribute) = (projection.Path.Qualifier, projection.Path.Attribute);

            // count(c) over an identification variable is JPQL's count(*); decided here,
            // after the from clause has declared the aliases.
            if (projection.Function is not null && qualifier is null && aliases.ContainsKey(attribute))
            {
                attribute = "*";
            }

            if (projection.Function is null && qualifier is null && aliases.ContainsKey(attribute))
            {
                if (projections.Count > 1)
                {
                    Report(ConversionRecordKind.Loss,
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

            queryBuilder.Project(qualifier ?? sourceAlias, ColumnFor(qualifier, attribute), projection.Alias, projection.Function);
        }
    }

    private (string Table, string Alias) ParseEntityReference()
    {
        var parts = ParseDottedName("expected an entity name");
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
            Report(ConversionRecordKind.Loss,
                "The fetch modifier of a join only changes what is loaded eagerly; the join was read without it.",
                QueryFeature.Join);
        }

        var parts = ParseDottedName("expected an entity name after 'join'");

        if (parts.Count > 1 && aliases.ContainsKey(parts[0]))
        {
            if (ParseOptionalAlias() is { } pathAlias)
            {
                aliases[pathAlias] = null;
            }

            if (TryConsumeKeyword("on") || TryConsumeKeyword("with"))
            {
                ParseCondition();
                unread = null;
            }

            Report(ConversionRecordKind.Failure,
                $"A join along the association path '{string.Join('.', parts)}' is not carried by the query representation, and a query emitted without its join would return different rows; no artifact was generated.",
                QueryFeature.Join);
            return;
        }

        var entity = parts[^1];
        var map = MapFor(entity);
        var alias = ParseOptionalAlias() ?? entity;
        aliases[alias] = map;

        if (!TryConsumeKeyword("on") && !TryConsumeKeyword("with"))
        {
            Report(ConversionRecordKind.Failure,
                "An entity join without an on condition has no join predicate the query representation can carry, and a query emitted without its join would return different rows; no artifact was generated.",
                QueryFeature.Join);
            return;
        }

        var condition = ParseCondition();
        if (condition is null)
        {
            Refuse("join's on condition", "a query emitted without its join would return different rows", QueryFeature.Join);
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
            if (part is LogicalCondition logical && logical.Operator == op)
            {
                flattened.AddRange(logical.Operands);
            }
            else
            {
                flattened.Add(part!);
            }
        }

        return new LogicalCondition(op, flattened);
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
            if (NextIsSubQuery())
            {
                var sub = ParseParenthesizedSubQuery();
                var op = ParseComparisonOperator() ?? throw Error("expected a comparison operator after the subquery");
                var right = ParseOperandOrSubQuery();
                return right is null ? null : new ComparisonCondition(QueryOperand.Nested(sub), op, right);
            }

            Advance();
            var grouped = ParseCondition();
            ConsumeSymbol(")");
            return grouped;
        }

        if (TryConsumeKeyword("exists"))
        {
            var sub = ParseParenthesizedSubQuery();
            return new ComparisonCondition(QueryOperand.Nested(sub), ComparisonOperator.Exists);
        }

        return ParsePredicate();
    }

    private ConditionNode? ParsePredicate()
    {
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
            return left is null ? null : new ComparisonCondition(left, negated ? ComparisonOperator.IsNotNull : ComparisonOperator.IsNull);
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

            QueryOperand? members;
            if (NextIsSubQuery())
            {
                members = QueryOperand.Nested(ParseParenthesizedSubQuery());
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

            Report(ConversionRecordKind.Convention, "A between predicate was rewritten as a pair of comparisons (rule Q14).", QueryFeature.Filtering);

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
            return null;
        }

        var beforeRight = position;
        var right = ParseOperandOrSubQuery();
        if (right is null && position == beforeRight)
        {
            throw Error("expected a value, an attribute or a subquery");
        }

        return left is null || right is null ? null : new ComparisonCondition(left, op.Value, right);
    }

    private QueryOperand? ParseRequiredOperand()
    {
        var before = position;
        var operand = ParseOperand();
        if (operand is null && position == before)
        {
            throw Error("expected a value or an attribute");
        }

        return operand;
    }

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

        if (AtSymbol("{"))
        {
            return ParseJdbcEscape();
        }

        if (Current.Kind == TokenKind.Parameter)
        {
            unread ??= ($"the parameter '{Current.Text}', for which the query representation has no operand", QueryFeature.QueryParameter);
            Advance();
            return null;
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
                    : path.Qualifier is null && aliases.ContainsKey(path.Attribute)
                        ? QueryOperand.Column(null, "*", function)
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
    /// The JDBC escape syntax JPQL prescribes for temporal literals (§4.6.1): {d '…'},
    /// {t '…'} and {ts '…'}, read into the Date, TimeOfDay and DateTime scalars.
    /// </summary>
    private QueryOperand ParseJdbcEscape()
    {
        ConsumeSymbol("{");
        if (Current.Kind != TokenKind.Identifier)
        {
            throw Error("expected d, t or ts after '{'");
        }

        var marker = Current.Text.ToLowerInvariant();
        Advance();

        if (Current.Kind != TokenKind.String)
        {
            throw Error("expected a quoted literal in the JDBC escape");
        }

        var text = Current.Text;
        Advance();
        ConsumeSymbol("}");

        var scalar = marker switch
        {
            "d" => ScalarType.Date,
            "t" => ScalarType.TimeOfDay,
            "ts" => ScalarType.DateTime,
            _ => throw Error($"unknown JDBC escape '{marker}'"),
        };

        return QueryOperand.Value(QueryConstant.Of(text, scalar));
    }

    private QueryOperand? ParseOperandOrSubQuery()
    {
        if (AtSymbol("(") && NextIsSubQuery())
        {
            return QueryOperand.Nested(ParseParenthesizedSubQuery());
        }

        return ParseOperand();
    }

    private sealed record PathReference(string? Qualifier, string Attribute);

    private PathReference? ParsePath()
    {
        var parts = ParseDottedName("expected an attribute reference");

        return parts.Count switch
        {
            1 => new PathReference(null, parts[0]),
            2 => new PathReference(parts[0], parts[1]),
            _ => null,
        };
    }

    private static QueryConstant NumberConstant(string text)
        => text.Contains('.') ? QueryConstant.Of(text, ScalarType.Decimal) : QueryConstant.Of(text, ScalarType.Int);

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

    private string ColumnFor(string? qualifier, string property)
    {
        aliases.TryGetValue(qualifier ?? sourceAlias, out var map);

        return map?.PropertyMaps
                   .FirstOrDefault(p => string.Equals(p.Property.Name, property, StringComparison.OrdinalIgnoreCase))
                   ?.ColumnName
               ?? property;
    }

    private void Refuse(string clause, string consequence, QueryFeature feature)
    {
        var (what, category) = unread ?? ("a construct the condition tree cannot carry", (QueryFeature?)null);
        unread = null;

        Report(ConversionRecordKind.Failure, $"The {clause} uses {what}, and {consequence}; no artifact was generated.", category ?? feature);
    }

    protected void Report(ConversionRecordKind kind, string reason, QueryFeature? feature = null)
        => queryBuilder.Report(new ConversionRecord
        {
            Kind = kind,
            Framework = queryBuilder.Descriptor.Framework,
            Artifact = ConversionContentType.JpqlQuery,
            Feature = feature,
            Reason = reason,
        });
}
