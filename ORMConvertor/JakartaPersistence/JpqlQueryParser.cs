using System.Text;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Naming;
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
        "and", "or", "not", "like", "in", "is", "null", "between", "exists", "escape",
        "union", "intersect", "except", "all", "limit", "offset",
        "case", "when", "then", "else", "end",
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

    /// <summary>
    /// The limits this parser reads its input under (decision 092). The orchestration sets
    /// them on every parser it creates; one constructed by hand - in a test - runs under the
    /// default, which is the cap the application uses unless its operator moved it.
    /// </summary>
    public ParseLimits Limits { get; set; } = ParseLimits.Default;

    public bool CanParse(ConversionContentType contentType)
        => contentType is ConversionContentType.JpqlQuery or ConversionContentType.Java;

    /// <summary>
    /// A bare JPQL unit is one query. A Java unit carries a query for every createQuery and
    /// createSelectionQuery call in it, not for the first one alone (decision 109): each call
    /// makes a query object of its own and is read into a builder of its own, so a call whose
    /// query is composed at run time refuses itself alone. Queries of one unit are numbered by
    /// the position of their call in the text, since the calls name nothing.
    ///
    /// The Java unit is a whole file or a fragment of one (decision 111), and the entity pass
    /// reads the same text for its classes. One without a call is therefore no error - a file
    /// holding an entity alone is an ordinary input - and yields no query; whether the unit
    /// yielded anything at all is asked of both passes together (decision 081). A text the
    /// lexer cannot read yields nothing here either, and says nothing: the entity pass, which
    /// read the same text first, has reported it.
    /// </summary>
    public IReadOnlyCollection<AbstractQueryBuilder> Parse(ConversionContentType contentType, string source, IReadOnlyList<EntityMap>? entityMaps = null)
    {
        maps = entityMaps;

        // Every builder leaves, refused ones included: it holds the records of what went
        // wrong, and only the parser can say that this unit yielded a query at all (decision
        // 081).
        if (contentType != ConversionContentType.Java)
        {
            queryBuilder = queryBuilders();
            ReadQuery(source);
            return [queryBuilder];
        }

        var calls = ExtractQueryLiterals(source);
        var builders = new List<AbstractQueryBuilder>(calls.Count);

        for (var i = 0; i < calls.Count; i++)
        {
            queryBuilder = queryBuilders();

            // Given before the reading, to every call, so that a refused query does not
            // renumber its neighbours (decisions 108 and 109); a single query keeps the fixed
            // name.
            if (calls.Count > 1)
            {
                queryBuilder.QueryName = QueryMethodNaming.Positional(i + 1);
            }

            if (calls[i] is { } jpql)
            {
                ReadQuery(jpql);
            }
            else
            {
                Report(ConversionRecordKind.Incompleteness,
                    "The query handed to createQuery is not a string literal - it is composed at run time - so the parser has nothing to read.");
            }

            builders.Add(queryBuilder);
        }

        return builders;
    }

    /// <summary>One query of JPQL into the builder of the moment, with the state of the previous one cleared.</summary>
    private void ReadQuery(string jpql)
    {
        aliases = new Dictionary<string, EntityMap?>(StringComparer.OrdinalIgnoreCase);
        unread = null;

        queryBuilder.Push();
        try
        {
            tokens = Lex(jpql);

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
        }
        catch (JpqlParseError error)
        {
            Report(ConversionRecordKind.Failure,
                $"The JPQL could not be parsed at line {error.Line}, column {error.Column}: {error.Message}.");
        }

        queryBuilder.Pop();
    }

    /// <summary>
    /// The JPQL inside a Java unit: for every createQuery or createSelectionQuery call, in the
    /// order of the text, the string literal it is handed, whether a plain literal or a text
    /// block (decision 109). A query composed at run time - concatenation, a variable - is null
    /// in the list: the same case the Dapper parser reports for SQL that is not a literal
    /// (decision 026), an incompleteness of that query and nothing to read.
    /// </summary>
    private static List<string?> ExtractQueryLiterals(string source)
    {
        var javaTokens = Calls(source, out var calls);

        return [.. calls.Select(i => javaTokens[i + 2].Kind == JavaTokenKind.String
            && !(javaTokens[i + 3] is { Kind: JavaTokenKind.Symbol, Text: "+" })
                ? javaTokens[i + 2].Text
                : null)];
    }

    /// <summary>
    /// The places where a Java unit hands a query over (decisions 109 and 111): the offset in
    /// the source of every call <see cref="Parse"/> reads a query of, found by the same
    /// search. The entity pass of the wrapper asks it which classes hold the code around
    /// queries rather than an entity, and the answer has to be the one the query pass acts on.
    /// A text the lexer cannot read has none.
    /// </summary>
    public static IReadOnlyList<int> FindHandovers(string source)
    {
        var javaTokens = Calls(source, out var calls);
        return [.. calls.Select(i => javaTokens[i].Offset)];
    }

    /// <summary>
    /// The tokens of the unit and the index of every createQuery or createSelectionQuery
    /// called with an argument list, in the order of the text; none where the lexer cannot
    /// read the text.
    /// </summary>
    private static List<JavaToken> Calls(string source, out List<int> calls)
    {
        calls = [];

        List<JavaToken> javaTokens;
        try
        {
            javaTokens = JavaLexer.Lex(source);
        }
        catch (JavaSyntaxError)
        {
            return [];
        }

        for (var i = 0; i + 2 < javaTokens.Count; i++)
        {
            if (javaTokens[i].Kind == JavaTokenKind.Identifier
                && javaTokens[i].Text is "createQuery" or "createSelectionQuery"
                && javaTokens[i + 1] is { Kind: JavaTokenKind.Symbol, Text: "(" })
            {
                calls.Add(i);
            }
        }

        return javaTokens;
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

            if (i + 1 < source.Length && source.Substring(i, 2) is ("<>" or "<=" or ">=" or "!=" or "||") and var pair)
            {
                read.Add(new Token(TokenKind.Symbol, pair, startLine, startColumn));
                i += 2;
                column += 2;
                continue;
            }

            // The arithmetic operators and the concatenation of Jakarta Persistence 3.2
            // entered with decision 107.
            if (c is '(' or ')' or ',' or '.' or '*' or '=' or '<' or '>' or '-' or '{' or '}' or '+' or '/' or '%')
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

    /// <summary>
    /// The lexer's own tokens as the shared nesting guard reads them - text and position, and
    /// nothing else, because that is all a token type has in common across five languages
    /// (decision 092). Lazy on purpose: the guard stops at the first token past the cap.
    /// </summary>
    private static IEnumerable<SourceToken> Tracked(IEnumerable<Token> read)
        => read.Select(token => new SourceToken(token.Text, token.Line, token.Column));

    /* ---- token helpers -------------------------------------------------------------- */

    protected Token Current => tokens[position];

    protected Token Next => tokens[Math.Min(position + 1, tokens.Count - 1)];

    /// <summary>The token n places ahead, clamped to the end marker.</summary>
    protected Token Ahead(int n) => tokens[Math.Min(position + n, tokens.Count - 1)];

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

    /// <summary>
    /// A row count in a position that needs one: the number the query states, or the
    /// parameter it leaves to the caller (decision 085). JPQL itself has no clause that
    /// takes a row count - the slice is setFirstResult and setMaxResults on the query object
    /// (decision 060) - so the only caller is the dialect hook of a profile whose language
    /// does have one, which is HQL's limit and offset (decision 076).
    /// </summary>
    protected RowCount ConsumeRowCount()
    {
        if (Current.Kind != TokenKind.Parameter)
        {
            return RowCount.Literal(ConsumeInteger());
        }

        // Built before the token is consumed, so that it points at the parameter rather than
        // at whatever follows it.
        var malformed = Error("expected a parameter naming a name or an ordinal");

        return ReadParameter() is { } parameter ? RowCount.Bound(parameter) : throw malformed;
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
        // The select clause is skipped on the first pass and read after the from clause and
        // the joins have declared the aliases (decision 107): a projected expression
        // resolves its attributes through the aliases the same way a condition does, and the
        // tokens are a list, so the reader comes back to the clause by position.
        int? selectStart = null;
        if (TryConsumeKeyword("select"))
        {
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

        if (selectStart is { } selectAt)
        {
            var afterJoins = position;
            position = selectAt;
            var projections = new List<Projection>();

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
                else if (key is { IsExpression: true })
                {
                    // A grouping by an expression is the one position the expression does not
                    // take (decision 107); refused by name.
                    unread = null;
                    Report(ConversionRecordKind.Failure,
                        $"The grouping key '{key}' is an expression, which the query representation carries in every position but the grouping, and a query grouped differently would return different rows; no artifact was generated.",
                        QueryFeature.Expression);
                }
                else
                {
                    unread = null;
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
                // An attribute, an aggregate (order by count(e) desc), an expression or a
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
                    var (what, category) = unread ?? ("a construct that is not an attribute reference", null);
                    unread = null;
                    Report(ConversionRecordKind.Loss, $"An ordering key that is {what} was dropped.", category ?? QueryFeature.Ordering);
                }
                else if (key is { IsColumn: true, Property: "*" })
                {
                    queryBuilder.OrderBy(QueryOperand.Column(sourceAlias, "*", key.Function, key.Distinct), asc);
                }
                else
                {
                    queryBuilder.OrderBy(key, asc);
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
        else if (Current.Kind == TokenKind.Identifier && !Keywords.Contains(Current.Text))
        {
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

            if (operand is null || (operand.IsConstant && !operand.IsAggregate) || operand.IsParameter)
            {
                var (what, category) = unread ?? ("an attribute reference, an aggregate or an expression", null);
                unread = null;
                Report(ConversionRecordKind.Loss, $"A projected expression that is not {what} was dropped.", category ?? QueryFeature.Projection);
                continue;
            }

            // A bare identification variable projects the whole entity, which rule Q3 spells
            // as the absence of a projection; count(c) over one is JPQL's count(*).
            if (operand is { IsColumn: true, IsAggregate: false, Property: "*" })
            {
                if (projections.Count > 1)
                {
                    Report(ConversionRecordKind.Loss,
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

            // The projection names its table; an attribute the text left unqualified belongs
            // to the source.
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
            ParseAssociationJoin(kind, parts);
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

    /// <summary>
    /// A join along an association path - <c>join o.customer c</c>, the shape JPQL writes a
    /// join in (decision 101, paper rule Q7). The predicate is not in the query but in the
    /// mapping, so it is derived from the relation the path names, FK(left) = PK(right) over
    /// its column pairs, and the join reaches the builder in the very shape an entity join
    /// with a written condition takes. What the maps of the conversion do not hold is
    /// refused by name and never guessed (decision 067): a query emitted without its join
    /// would return different rows (decision 070). The syntax is consumed either way, so
    /// that the clauses after it are still read and every reason arrives at once.
    /// </summary>
    private void ParseAssociationJoin(JoinKind kind, List<string> parts)
    {
        var association = ResolveAssociation(parts, out var failure);

        // The alias is declared before the written condition is read, because that
        // condition may refer to it; an unresolved path still declares it, as null.
        var alias = ParseOptionalAlias() ?? parts[^1];
        aliases[alias] = association?.Target;

        ConditionNode? written = null;
        if (TryConsumeKeyword("on") || TryConsumeKeyword("with"))
        {
            written = ParseCondition();
            if (written is null)
            {
                if (failure is null)
                {
                    Refuse("join's on condition", "a query emitted without its join would return different rows", QueryFeature.Join);
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

        var condition = AssociationCondition(association.Relation, parts[0], alias, written);
        queryBuilder.Join(kind, sourceAlias, TableFor(association.Target, association.Relation.TargetEntity), condition, alias);
    }

    private sealed record AssociationJoin(Relation Relation, EntityMap Target);

    /// <summary>
    /// The relation a path names, or null with the sentence that says what the maps of the
    /// conversion are missing (decision 101). The path is the alias and one association: a
    /// longer one crosses an embeddable or an intermediate join the model does not carry.
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

        if (relation.ColumnPairs.Count == 0)
        {
            failure = $"The join along the association path '{path}' has no foreign key columns to derive its condition from: the relation to '{relation.TargetEntity}' states none and the database catalog supplied none; {consequence}";
            return null;
        }

        failure = null;
        return new AssociationJoin(relation, target);
    }

    /// <summary>
    /// FK(left) = PK(right) over the column pairs, in their order (decision 101). Which alias
    /// holds the foreign key follows the role of the relation: the entity behind the path
    /// for an owning one, the joined entity for an inverse one. A condition the source
    /// wrote after the path narrows the join further and joins the conjunction.
    /// </summary>
    private static ConditionNode AssociationCondition(Relation relation, string pathAlias, string joinAlias, ConditionNode? written)
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
            // The grammar of Jakarta Persistence 3.2 puts a collection-valued input
            // parameter in IN's place itself, without parentheses; both implementations also
            // take it parenthesized, so both spellings are read as the same collection
            // parameter (decision 083).
            if (Current.Kind == TokenKind.Parameter)
            {
                var bound = ReadParameter(isCollection: true);
                if (bound is null || left is null)
                {
                    return null;
                }

                ConditionNode bare = new ComparisonCondition(left, ComparisonOperator.In, QueryOperand.Bound(bound));
                return notPrefixed ? new NotCondition(bare) : bare;
            }

            if (!AtSymbol("("))
            {
                throw Error("expected '(' after 'in'");
            }

            QueryOperand? members;
            if (NextIsSubQuery())
            {
                members = QueryOperand.Nested(ParseParenthesizedSubQuery());
            }
            else if (Next.Kind == TokenKind.Parameter && Ahead(2) is { Kind: TokenKind.Symbol, Text: ")" })
            {
                Advance();
                var bound = ReadParameter(isCollection: true);
                ConsumeSymbol(")");
                members = bound is null ? null : QueryOperand.Bound(bound);
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

            // A scalar parameter stands among the values (decision 102); its scalar comes
            // from the left side of in through the builder's parameter gate.
            if (element is not null && element.IsParameter)
            {
                values.Add(element);
                continue;
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
    /// the additive level - <c>+</c>, <c>-</c> and the concatenation <c>||</c> of Jakarta
    /// Persistence 3.2 - over the multiplicative one, over the primaries. A null anywhere
    /// sinks the whole operand after every token of it has been consumed, so that the
    /// clause refuses by name.
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

        if (AtSymbol("{"))
        {
            return ParseJdbcEscape();
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
            return ReadParameter() is { } parameter ? QueryOperand.Bound(parameter) : null;
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

        if (AtKeyword("case"))
        {
            return ParseCase();
        }

        // The current moment: a keyword-like identifier without parentheses in JPQL.
        if (string.Equals(Current.Text, "current_timestamp", StringComparison.OrdinalIgnoreCase)
            && Next is not { Kind: TokenKind.Symbol, Text: "(" })
        {
            Advance();
            return QueryOperand.Computed(QueryExpression.Call(QueryFunction.CurrentTimestamp, []));
        }

        if (IsAggregate(Current.Text) && Next is { Kind: TokenKind.Symbol, Text: "(" })
        {
            return ParseAggregate();
        }

        if (string.Equals(Current.Text, "extract", StringComparison.OrdinalIgnoreCase) && Next is { Kind: TokenKind.Symbol, Text: "(" })
        {
            return ParseExtract();
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

        // A bare identification variable is the whole entity - the projection reads it as
        // rule Q3's absence of a projection, count(c) as count(*).
        if (reference.Qualifier is null && aliases.ContainsKey(reference.Attribute))
        {
            return QueryOperand.Column(reference.Attribute, "*");
        }

        return QueryOperand.Column(reference.Qualifier, ColumnFor(reference.Qualifier, reference.Attribute));
    }

    /* ---- expressions (decision 107) --------------------------------------------------- */

    /// <summary>
    /// One of the five aggregates: over the whole row (<c>count(*)</c>, or the count of an
    /// identification variable, which is JPQL's spelling), over an attribute, or over an
    /// expression (decision 107), with <c>distinct</c> inside the function as the modifier
    /// of the aggregate (decision 102).
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
    /// The standard <c>extract(year from x)</c> of Jakarta Persistence 3.2 for the parts of
    /// a date the vocabulary names; Hibernate's own <c>year(x)</c> is not read in a JPQL
    /// unit, because EclipseLink does not know it (decision 107).
    /// </summary>
    private QueryOperand? ParseExtract()
    {
        Advance();
        ConsumeSymbol("(");

        if (Current.Kind != TokenKind.Identifier)
        {
            throw Error("expected the field of extract");
        }

        var field = Current.Text.ToLowerInvariant();
        Advance();
        ConsumeKeyword("from");
        var argument = ParseOperand();
        ConsumeSymbol(")");

        if (argument is null || !IsLeaf(argument))
        {
            return null;
        }

        QueryFunction? function = field switch
        {
            "year" => QueryFunction.Year,
            "month" => QueryFunction.Month,
            "day" => QueryFunction.Day,
            _ => null,
        };

        if (function is null)
        {
            unread ??= ($"extract({field} from …), a field outside the vocabulary of expressions the query representation carries", QueryFeature.Expression);
            return null;
        }

        return QueryOperand.Computed(QueryExpression.Call(function.Value, [argument]));
    }

    /// <summary>
    /// A call of a function under its JPQL name (decision 107): the vocabulary in lower
    /// case, <c>concat</c> of any number of arguments nested, <c>mod</c> as the modulo. A
    /// name outside the vocabulary - Hibernate's <c>year</c>, <c>replace</c>, <c>size</c> -
    /// is consumed to its closing parenthesis and refused by name.
    /// </summary>
    private QueryOperand? ParseFunctionCall()
    {
        var name = Current.Text.ToLowerInvariant();
        var line = Current.Line;
        var column = Current.Column;
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
                    throw new JpqlParseError(line, column, $"expected an argument of {name}");
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
            "current_timestamp" => QueryFunction.CurrentTimestamp,
            _ => null,
        };

        if (function is null)
        {
            unread ??= ($"the function {name}, which is outside the vocabulary of expressions the query representation carries", QueryFeature.Expression);
            return null;
        }

        return Call(function.Value, arguments, name);
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

    /// <summary>
    /// One parameter token as the model carries it (decision 083): <c>:id</c> is the name
    /// id and <c>?1</c> is the order 1, the colon and the question mark being JPQL's
    /// decoration and stripped like the quotes of a string. A bare <c>?</c> is not JPQL -
    /// the specification writes an ordinal after it - and there is no order to carry, so it
    /// sinks the clause under the parameter's own category.
    /// </summary>
    private QueryParameter? ReadParameter(bool isCollection = false)
    {
        var text = Current.Text;
        Advance();

        if (text[0] == ':')
        {
            return QueryParameter.Named(text[1..], isCollection: isCollection);
        }

        if (text.Length > 1 && int.TryParse(text[1..], out var order) && order >= 1)
        {
            return QueryParameter.Positional(order, isCollection: isCollection);
        }

        unread ??= (
            $"the parameter '{text}', which names neither a name nor an ordinal",
            QueryFeature.QueryParameter);
        return null;
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
