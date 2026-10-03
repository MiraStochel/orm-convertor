using System.Globalization;
using Common.Convertors;
using Common.Naming;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace EFCoreWrappers;

/// <summary>
/// The lexical scope a LINQ chain is being written in. A LINQ lambda names one parameter,
/// and what that parameter holds changes as the chain grows: the source row at first, a
/// transparent tuple after a join, a grouping after GroupBy. Rendering a column therefore
/// needs this state, which is why the LINQ visitor carries it and the SQL one does not.
/// </summary>
public sealed class LinqScope
{
    /// <summary>Name of the current lambda parameter.</summary>
    public string Param { get; set; } = "c";

    /// <summary>True once a join has made the parameter hold a tuple of rows.</summary>
    public bool Composite { get; set; }

    public bool Grouped { get; set; }

    /// <summary>
    /// The keys of the grouping, each with the member of the key object that names it - none
    /// for a single key, which is <c>g.Key</c> itself (decision 113).
    /// </summary>
    public IReadOnlyList<LinqGroupKey> GroupKeys { get; set; } = [];

    /// <summary>Parameter used inside an aggregate lambda, which ranges over group elements.</summary>
    public string ElementParam { get; set; } = "e";

    public Dictionary<string, EntityMap> Entities { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Aliases this scope itself declares - the source and each join, mapped or not. What a
    /// nested subquery's scope does not declare it looks up in the enclosing scope, which is
    /// how a correlated reference finds the outer lambda's parameter (decision 061).
    /// </summary>
    public HashSet<string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Aliases on the side of an outer join that may find no match - the joined row of a left
    /// join, the rows before a right one, both of a full one. A column of such a row is null
    /// wherever the join found no match, whatever type its entity declares.
    /// </summary>
    public HashSet<string> OptionalAliases { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string Row(string? alias) => Composite && alias is not null ? $"{Param}.{alias}" : Param;

    public string ElementRow(string? alias) => Composite && alias is not null ? $"{ElementParam}.{alias}" : ElementParam;
}

/// <summary>
/// One key of a LINQ grouping (decision 113): the value grouped by, a column or an
/// expression, and the member of the anonymous key object it goes by - null where the key is
/// the only one and <c>g.Key</c> is the value itself.
/// </summary>
public sealed record LinqGroupKey(QueryOperand Key, string? Member);

/// <summary>
/// Writes query instructions as LINQ (decision 022). Unlike the SQL visitor this one is
/// stateful: it reads <see cref="LinqScope"/> to know what the current lambda parameter
/// stands for.
/// </summary>
/// <param name="typing">The typed view of the query's expressions the builder's gate filled (decision 107): the scalar of a CASE without an ELSE, which C# needs to spell its null.</param>
public sealed class EFCoreLinqQueryVisitor(
    LinqScope scope,
    Action<ConversionRecordKind, string, QueryFeature?> report,
    Func<SubQueryInstruction, ComparisonOperator, string?> renderSubQuery,
    ExpressionTyping typing,
    EFCoreLinqQueryVisitor? outer = null)
    : IQueryVisitor
{
    public LinqScope Scope { get; } = scope;

    /// <summary>
    /// True while the argument of an aggregate is being written: the lambda there ranges
    /// over the elements of the group, so a column is a member of the element rather than
    /// a key of the grouping.
    /// </summary>
    private bool insideAggregate;

    /// <summary>The escape character of the LIKE whose pattern is being written (decision 107); null outside a pattern.</summary>
    private string? patternEscape;

    /// <summary>Whether the alias belongs to this scope or one enclosing it (decision 061).</summary>
    public bool Knows(string alias) => Scope.Aliases.Contains(alias) || outer?.Knows(alias) == true;

    public string Visit(FromInstruction instr) => Scope.Param;

    public string Visit(ProjectInstruction instr) => Operand(instr.Operand);

    public string Visit(SelectInstruction instr) => instr.Condition.Accept(this);

    public string Visit(HavingInstruction instr) => instr.Condition.Accept(this);

    /// <summary>The value a key selector of GroupBy returns (decision 113): a column, or an expression over the row.</summary>
    public string Visit(GroupByInstruction instr) => Operand(instr.Key);

    public string Visit(OrderByInstruction instr) => Operand(instr.Operand);

    public string Visit(JoinInstruction instr) => instr.OnCondition.Accept(this);

    /// <summary>
    /// The set operations LINQ names, exhaustively. ExceptAll has no LINQ method and used to
    /// fall into the Except branch, which deduplicates rows the source keeps - a silent change
    /// of meaning of exactly the kind decision 004 forbids, closed here by decision 053.
    /// </summary>
    public string Visit(SetOperationInstruction instr)
    {
        switch (instr.OperationType)
        {
            case SetOperationType.Union: return "Union";
            case SetOperationType.UnionAll: return "Concat";
            case SetOperationType.Intersect: return "Intersect";
            case SetOperationType.Except: return "Except";
            default:
                report(
                    ConversionRecordKind.Fallback,
                    $"The set operation {instr.OperationType} has no LINQ form",
                    QueryFeature.SetOperation);
                return string.Empty;
        }
    }

    public string Visit(ComparisonCondition cond)
    {
        // EXISTS carries its subquery as the left operand, the way IS NULL carries its
        // column (decisions 002 and 061). LINQ's EXISTS is Any() on the nested chain; the
        // chain itself is the builder's to render, and null means it refused.
        if (cond.Operator == ComparisonOperator.Exists)
        {
            var sub = renderSubQuery(cond.Left.SubQuery!, cond.Operator);
            return sub is null ? string.Empty : $"{sub}.Any()";
        }

        if (cond.Left.IsSubQuery || cond.Right?.IsSubQuery == true)
        {
            return SubQueryComparison(cond);
        }

        var left = Operand(cond.Left);

        if (cond.Operator == ComparisonOperator.IsNull)
        {
            return $"{left} == null";
        }

        if (cond.Operator == ComparisonOperator.IsNotNull)
        {
            return $"{left} != null";
        }

        if (cond.Right is null)
        {
            // Unreachable: the template refuses such a tree before any step runs
            // (decision 053). Reported rather than substituted, because a tautology in
            // place of a filter returns rows the source excluded.
            report(ConversionRecordKind.Failure, $"Operator {cond.Operator} has no right operand; the query was not generated.", QueryFeature.Filtering);
            return string.Empty;
        }

        if (cond.Operator == ComparisonOperator.Like)
        {
            return Like(left, cond.Right, cond.Escape);
        }

        if (cond.Operator == ComparisonOperator.In)
        {
            // IN over enumerated values (decision 074) turns around into Contains on an
            // inline array of constants, which EF Core translates back to IN (...). The
            // subquery right side went through SubQueryComparison above; anything else is
            // unreachable, because the template's gate refuses it (decisions 061 and 074).
            if (cond.Right.IsValueList)
            {
                return $"{ValueList(cond.Right, NullableElementType(cond.Left))}.Contains({left})";
            }

            // A collection parameter turns around the same way (decision 083): the sequence
            // the caller binds is the receiver, which is the shape the source wrote in the
            // first place when the source was LINQ. The sequence is declared over the plain
            // scalar (decision 083), and IEnumerable<int> has no Contains that takes an
            // int?, so a nullable column offers its value - which EF Core translates as the
            // column itself, and IN never matches a NULL anyway.
            if (cond.Right.IsParameter && cond.Right.Parameter!.IsCollection)
            {
                var member = NullableElementType(cond.Left) is null ? left : $"{left}.Value";
                return $"{QueryParameterNaming.IdentifierFor(cond.Right.Parameter!)}.Contains({member})";
            }

            report(ConversionRecordKind.Failure, "An IN whose right side is neither a subquery, a list of values nor a collection parameter has no LINQ form; the query was not generated.", QueryFeature.Filtering);
            return string.Empty;
        }

        return $"{left} {Operator(cond.Operator)} {Operand(cond.Right)}";
    }

    /// <summary>
    /// The inline array of an IN list (decision 074). Its element type is inferred from the
    /// literals unless the column it is compared with is a nullable value type: int[] has
    /// no Contains that takes an int?, so against such a column the array declares the
    /// nullable element type and the artifact compiles - which the inferred form did not.
    /// </summary>
    private string ValueList(QueryOperand operand, string? elementType = null)
        => $"new{(elementType is null ? string.Empty : " " + elementType)}[] {{ {string.Join(", ", operand.Values!.Select(Operand))} }}";

    /// <summary>
    /// The C# spelling of the column's type when the mapping makes it a nullable value
    /// type (<c>int?</c>), which is the one case an IN over it has to be typed for; null
    /// for a column that is not mapped, not nullable, or of a reference type, where the
    /// inferred element type is the right one already.
    /// </summary>
    private string? NullableElementType(QueryOperand operand)
    {
        if (!operand.IsColumn || operand.Function is not null)
        {
            return null;
        }

        var type = PropertyType(operand.Table, operand.Property!);
        if (type?.ScalarType is not { } scalar || !type.IsNullable || scalar is ScalarType.String or ScalarType.Object or ScalarType.ByteArray)
        {
            return null;
        }

        // The EF Core entity declares a part of the key non-nullable whatever the language type
        // says - a MyBatis source's Integer key comes out an int (decision 113, found by the
        // fourth level over a key converted to text).
        if (IsKeyPart(operand.Table, operand.Property!))
        {
            return null;
        }

        return CSharpTypeConvertor.ToString(LangType.Scalar(scalar)) + "?";
    }

    /// <summary>Whether the column is a part of the primary key of its entity, in this scope or an enclosing one.</summary>
    private bool IsKeyPart(string? alias, string column)
    {
        if (alias is not null && outer is not null && !Scope.Aliases.Contains(alias) && outer.Knows(alias))
        {
            return outer.IsKeyPart(alias, column);
        }

        var map = alias is not null && Scope.Entities.TryGetValue(alias, out var found) ? found : null;
        return map?.PrimaryKey?.Parts.Any(part =>
            string.Equals(part.PropertyMap.ColumnName ?? part.PropertyMap.Property.Name, column, StringComparison.OrdinalIgnoreCase)) == true;
    }

    /// <summary>
    /// Whether the mapping says the column holds no NULL: its nullability as a column where a
    /// source or the catalog states one - a Java String is a reference whatever the column
    /// holds -, else the nullability of its language type (decision 113).
    /// </summary>
    private bool HoldsNoNull(string? alias, string column)
    {
        if (alias is not null && outer is not null && !Scope.Aliases.Contains(alias) && outer.Knows(alias))
        {
            return outer.HoldsNoNull(alias, column);
        }

        var map = alias is not null && Scope.Entities.TryGetValue(alias, out var found) ? found : null;
        var mapped = map?.PropertyMaps
            .FirstOrDefault(p => string.Equals(p.ColumnName ?? p.Property.Name, column, StringComparison.OrdinalIgnoreCase));

        return mapped is not null && (mapped.IsNullable ?? mapped.Property.Type?.IsNullable) == false;
    }

    /// <summary>The language type the mapping gives a column of the scope, or of an enclosing scope for a correlated reference; null where nothing maps it.</summary>
    private LangType? PropertyType(string? alias, string column)
    {
        if (alias is not null && outer is not null && !Scope.Aliases.Contains(alias) && outer.Knows(alias))
        {
            return outer.PropertyType(alias, column);
        }

        var map = alias is not null && Scope.Entities.TryGetValue(alias, out var found) ? found : null;
        return EFCoreColumnMember.TypeOf(map, column);
    }

    /// <summary>
    /// A comparison one of whose sides is a subquery (decision 061). IN turns around into
    /// Contains on the nested chain; the scalar operators compare against the chain's
    /// terminal aggregate.
    /// </summary>
    private string SubQueryComparison(ComparisonCondition cond)
    {
        if (cond.Operator == ComparisonOperator.In)
        {
            var values = renderSubQuery(cond.Right!.SubQuery!, cond.Operator);
            var element = OperandOrSubQuery(cond.Left);
            if (values is null || element is null)
            {
                return string.Empty;
            }

            // The nested chain projects the plain scalar where its column is not nullable -
            // a key the catalog supplied is not -, and IQueryable<int> has no Contains that
            // takes an int?, so a nullable column on the left offers its value, exactly as
            // it does against a collection parameter below. EF Core translates the value as
            // the column itself, and IN never matches a NULL anyway. Found by the fourth
            // level over the categories of T2: a MyBatis source declares the column as
            // Integer, the catalog makes the product's key an int, and the artifact did not
            // compile in that one direction.
            if (NullableElementType(cond.Left) is not null)
            {
                element += ".Value";
            }

            return $"{values}.Contains({element})";
        }

        var left = OperandOrSubQuery(cond.Left);
        var right = OperandOrSubQuery(cond.Right!);
        if (left is null || right is null)
        {
            return string.Empty;
        }

        return $"{left} {Operator(cond.Operator)} {right}";
    }

    /// <summary>
    /// The operand's text, rendering a subquery in a scalar position - which is what a
    /// subquery standing anywhere but as IN's right side is, whatever the operator around
    /// it says.
    /// </summary>
    private string? OperandOrSubQuery(QueryOperand operand)
        => operand.IsSubQuery ? renderSubQuery(operand.SubQuery!, ComparisonOperator.Equal) : Operand(operand);

    /// <summary>
    /// A LIKE pattern in LINQ (decision 051). Where the pattern is anchored only at its ends
    /// and its core carries no wildcard, the exact LINQ counterpart exists and is written:
    /// <c>%x%</c> is Contains, <c>x%</c> StartsWith, <c>%x</c> EndsWith and a pattern without
    /// wildcards is equality. Anything else - a wildcard in the middle, an underscore, a
    /// character class, or a right side that is not a literal at all - goes out as
    /// EF.Functions.Like, which EF Core translates to LIKE unchanged. Handing the pattern to
    /// Contains verbatim, as this used to, searched for literal percent signs.
    ///
    /// With an escape character (decision 102) the pattern is read past it: a wildcard
    /// behind the escape is a literal character, the core goes out without the escapes -
    /// EF Core escapes the argument of a string method itself, so <c>'A!_%' ESCAPE '!'</c>
    /// is <c>StartsWith("A_")</c> - and where the split is not exact, the escape travels
    /// as the third argument of EF.Functions.Like.
    ///
    /// A pattern that is an expression (decision 107) is the same table over a value the
    /// caller binds: a wildcard concatenated onto an <see cref="QueryFunction.EscapePattern"/>
    /// of a value is the string method over that value - <c>StartsWith(prefix)</c>, exact,
    /// because EF Core's provider escapes the argument -, and a concatenation without the
    /// escaping is EF.Functions.Like over the concatenation, which is what the source
    /// meant: a wildcard in the value stays a wildcard there.
    /// </summary>
    private string Like(string left, QueryOperand right, string? escape)
    {
        var literalPattern = right.IsConstant && right.Function is null ? right.Constant!.Text : null;

        if (literalPattern is not null && TryReadPattern(literalPattern, escape, out var method, out var core))
        {
            return method is null
                ? $"{left} == {StringLiteral(core)}"
                : $"{left}.{method}({StringLiteral(core)})";
        }

        if (right.IsExpression && right.Function is null && TryReadAnchoredValue(right.Expression!, out var anchoredMethod, out var value))
        {
            return $"{left}.{anchoredMethod}({Operand(value!)})";
        }

        // A LIKE pattern is a string whatever scalar the parser managed to put on it, so a
        // constant goes out quoted rather than through the general literal rendering, which
        // would spell an untyped one bare and the result would not compile.
        patternEscape = escape;
        var pattern = literalPattern is not null ? StringLiteral(literalPattern) : Operand(right);
        patternEscape = null;

        return escape is null
            ? $"EF.Functions.Like({left}, {pattern})"
            : $"EF.Functions.Like({left}, {pattern}, {StringLiteral(escape)})";
    }

    /// <summary>
    /// The three anchored shapes over an escaped value (decision 107): <c>EscapePattern(x) + '%'</c>
    /// is StartsWith, <c>'%' + EscapePattern(x)</c> EndsWith, <c>'%' + EscapePattern(x) + '%'</c>
    /// Contains, the concatenations nested as the reader nests them.
    /// </summary>
    private static bool TryReadAnchoredValue(QueryExpression pattern, out string? method, out QueryOperand? value)
    {
        method = null;
        value = null;

        if (pattern.Operator is not (ExpressionOperator.Concat or ExpressionOperator.Add))
        {
            return false;
        }

        var (left, right) = (pattern.Left!, pattern.Right!);

        if (IsWildcard(right))
        {
            if (Escaped(left) is { } startsWith)
            {
                (method, value) = ("StartsWith", startsWith);
                return true;
            }

            if (left is { IsExpression: true, Function: null }
                && left.Expression!.Operator is ExpressionOperator.Concat or ExpressionOperator.Add
                && IsWildcard(left.Expression.Left!)
                && Escaped(left.Expression.Right!) is { } contains)
            {
                (method, value) = ("Contains", contains);
                return true;
            }

            return false;
        }

        if (IsWildcard(left) && Escaped(right) is { } endsWith)
        {
            (method, value) = ("EndsWith", endsWith);
            return true;
        }

        return false;
    }

    private static bool IsWildcard(QueryOperand operand)
        => operand is { IsConstant: true, Function: null } && operand.Constant!.Text == "%";

    private static QueryOperand? Escaped(QueryOperand operand)
        => operand is { IsExpression: true, Function: null }
           && operand.Expression!.Function == QueryFunction.EscapePattern
            ? operand.Expression.Arguments![0]
            : null;

    /// <summary>
    /// Splits a LIKE pattern into its anchors and its core, or refuses. The core has to be
    /// free of every wildcard: EF Core escapes the argument of Contains and friends, so a
    /// core holding <c>_</c> would come out matching a literal underscore where the source
    /// matched any character. A character behind the escape (decision 102) is no wildcard
    /// and reaches the core without its escape; an escape character with nothing behind it
    /// refuses the split, so that the text goes out as it was written.
    /// </summary>
    /// <param name="method">The string method to call, or null for plain equality.</param>
    private static bool TryReadPattern(string pattern, string? escape, out string? method, out string core)
    {
        method = null;
        core = pattern;

        var characters = new List<(char Value, bool Literal)>(pattern.Length);
        for (var i = 0; i < pattern.Length; i++)
        {
            if (escape is not null && pattern[i] == escape[0])
            {
                if (i + 1 >= pattern.Length)
                {
                    return false;
                }

                characters.Add((pattern[++i], true));
                continue;
            }

            characters.Add((pattern[i], false));
        }

        var leading = characters.Count > 0 && characters[0] is ('%', false);
        var trailing = characters.Count > (leading ? 1 : 0) && characters[^1] is ('%', false);

        var inner = characters[(leading ? 1 : 0)..(characters.Count - (trailing ? 1 : 0))];

        if (inner.Any(c => !c.Literal && c.Value is '%' or '_' or '['))
        {
            return false;
        }

        core = new string(inner.Select(c => c.Value).ToArray());

        method = (leading, trailing) switch
        {
            (true, true) => "Contains",
            (false, true) => "StartsWith",
            (true, false) => "EndsWith",
            _ => null,
        };

        return true;
    }

    private static string StringLiteral(string text)
        => $"\"{text.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    public string Visit(LogicalCondition cond)
    {
        var keyword = cond.Operator == LogicalOperator.And ? "&&" : "||";

        var parts = cond.Operands.Select(operand =>
            operand is LogicalCondition
                ? $"({operand.Accept(this)})"
                : operand.Accept(this));

        return string.Join($" {keyword} ", parts);
    }

    public string Visit(NotCondition cond) => $"!({cond.Operand.Accept(this)})";

    /// <summary>
    /// The relational operators, exhaustively. No catch-all branch: a value the target has
    /// no form for used to come out as the neighbouring operator, which is a silent change
    /// of meaning rather than a loss (decision 053). Like, In and the null tests never reach
    /// here - they have their own shapes above.
    /// </summary>
    private string Operator(ComparisonOperator op)
    {
        switch (op)
        {
            case ComparisonOperator.Equal: return "==";
            case ComparisonOperator.NotEqual: return "!=";
            case ComparisonOperator.GreaterThan: return ">";
            case ComparisonOperator.GreaterThanOrEqual: return ">=";
            case ComparisonOperator.LessThan: return "<";
            case ComparisonOperator.LessThanOrEqual: return "<=";
            default:
                report(ConversionRecordKind.Failure, $"Operator {op} has no LINQ form; the query was not generated.", QueryFeature.Filtering);
                return string.Empty;
        }
    }

    /// <summary>
    /// An operand in the current scope: a list, a parameter captured under its own name
    /// (decision 083 - LINQ has no placeholder), a constant, a subquery in a scalar position,
    /// an expression (decision 107), or a column; an aggregate over any of the last three is
    /// the aggregate call over the group's elements.
    /// </summary>
    public string Operand(QueryOperand operand)
    {
        if (operand.IsValueList)
        {
            return ValueList(operand);
        }

        if (operand.IsAggregate)
        {
            return Aggregate(operand);
        }

        // After GroupBy a value equal to a key is read off the key object - g.Key, or a member
        // of it - whether it is a column or an expression (decision 113); the structural
        // equality is the one the template's rule of grouping holds the projection to.
        if (Scope.Grouped && !insideAggregate && KeyPath(operand) is { } key)
        {
            return key;
        }

        if (operand.IsParameter)
        {
            return QueryParameterNaming.IdentifierFor(operand.Parameter!);
        }

        if (operand.IsConstant)
        {
            return Literal(operand.Constant!);
        }

        if (operand.IsSubQuery)
        {
            return renderSubQuery(operand.SubQuery!, ComparisonOperator.Equal) ?? string.Empty;
        }

        if (operand.IsExpression)
        {
            return Expression(operand.Expression!);
        }

        return Column(operand.Table, operand.Property!, null);
    }

    /// <summary>
    /// Renders a column reference in the current scope: a plain member access, a group key,
    /// an element of the group inside an aggregate's lambda, or an aggregate over the
    /// group's elements - over their distinct values when <paramref name="distinct"/> says
    /// so (decision 102).
    /// </summary>
    public string Column(string? alias, string attribute, string? function, bool distinct = false)
    {
        // A reference this scope does not declare but an enclosing one does is a correlated
        // reference: it renders through the outer visitor, whose lambda parameter is still
        // in scope inside the nested chain (decision 061).
        if (alias is not null && outer is not null && !Scope.Aliases.Contains(alias) && outer.Knows(alias))
        {
            return outer.Column(alias, attribute, function, distinct);
        }

        if (function is not null)
        {
            return Aggregate(QueryOperand.Column(alias, attribute, function, distinct));
        }

        if (Scope.Grouped && insideAggregate)
        {
            return $"{Scope.ElementRow(alias)}.{Property(alias, attribute)}";
        }

        if (Scope.Grouped)
        {
            var key = KeyPath(QueryOperand.Column(alias, attribute));
            if (key is not null)
            {
                return key;
            }

            report(
                ConversionRecordKind.Loss,
                $"Column {attribute} is neither a grouping key nor an aggregate, so it cannot be read after GroupBy; it was dropped.",
                QueryFeature.Projection);
            return $"{Scope.Param}.Key";
        }

        return $"{Scope.Row(alias)}.{Property(alias, attribute)}";
    }

    /// <summary>
    /// An aggregate over the group's elements (decisions 022 and 102): the parameterless
    /// Count() for a count of rows, the aggregate method with a lambda over the element for
    /// a column or an expression (decision 107), and a Select of the argument, collapsed,
    /// then the parameterless aggregate for one over distinct values.
    /// </summary>
    private string Aggregate(QueryOperand operand)
    {
        var function = operand.Function!;
        var bare = operand.Bare();

        // An aggregate over an ungrouped scope has no place inside a LINQ projection: the
        // chain must end in the aggregate call, which is not the IQueryable this builder
        // emits. Writing the bare column instead answers with every row where the source
        // answered with one number - a different result, not a poorer one (decision 053) -
        // and over COUNT(*) it wrote `c.*`, which is not even valid C#; a grouping by a
        // constant returns no row over an empty table where SQL returns one with a zero. So
        // the query goes out in native SQL (decision 113). The same shape inside a subquery
        // operand goes the same way from RenderSubQuery.
        if (!Scope.Grouped)
        {
            report(
                ConversionRecordKind.Fallback,
                $"{function} is projected without a grouping, which a LINQ chain can only express by ending in the aggregate call rather than by a query",
                QueryFeature.Aggregation);
            return string.Empty;
        }

        // COUNT(column) counts the non-null values of the column, not the rows - over a
        // nullable column Count() answered with another number, the kind of difference
        // decision 004 forbids. A member the entity declares as a value type that cannot be
        // null holds no null to skip, so Count() is exact there, a key part included - unless
        // the row stands on the side of an outer join that may find no match, where the
        // column is null exactly when the row is missing, so the row itself is what is
        // tested; and a constant is counted once per row, as COUNT(*) is.
        // Any other is counted through a test for null, which EF Core 10.0.10 translates to
        // the COUNT of the column's non-null values (measured with ToQueryString).
        if (function == "COUNT" && !operand.Distinct)
        {
            // The representation carries no NULL literal, so every constant is a value.
            if (bare.Property == "*" || bare.IsConstant)
            {
                return $"{Scope.Param}.Count()";
            }

            var map = bare.Table is not null && Scope.Entities.TryGetValue(bare.Table, out var found) ? found : null;
            if (bare.IsColumn && EFCoreColumnMember.DeclaredValue(map, bare.Property!) is { Nullable: false })
            {
                return bare.Table is not null && Scope.OptionalAliases.Contains(bare.Table)
                    ? $"{Scope.Param}.Count({Scope.ElementParam} => {Scope.ElementRow(bare.Table)} != null)"
                    : $"{Scope.Param}.Count()";
            }

            var wasCounting = insideAggregate;
            insideAggregate = true;
            var counted = Operand(bare);
            insideAggregate = wasCounting;

            return $"{Scope.Param}.Count({Scope.ElementParam} => {counted} != null)";
        }

        var method = function switch
        {
            "COUNT" => "Count",
            "SUM" => "Sum",
            "MIN" => "Min",
            "MAX" => "Max",
            "AVG" => "Average",
            _ => null,
        };

        if (method is null)
        {
            report(
                ConversionRecordKind.Loss,
                $"Aggregate function {function} has no LINQ counterpart; it was dropped.",
                QueryFeature.Aggregation);
            return $"{Scope.Param}.Count()";
        }

        var wasInsideAggregate = insideAggregate;
        insideAggregate = true;
        var element = $"{Scope.ElementParam} => {Operand(bare)}";
        insideAggregate = wasInsideAggregate;

        // The aggregate over the distinct values of the argument (decision 102): a Select of
        // the argument, collapsed, then the parameterless aggregate - the shape EF Core
        // translates to COUNT(DISTINCT ...). It counts distinct non-null values, exactly as
        // COUNT(DISTINCT x) does, so unlike Count() it needs no record.
        if (operand.Distinct)
        {
            return $"{Scope.Param}.Select({element}).Distinct().{method}()";
        }

        return $"{Scope.Param}.{method}({element})";
    }

    /// <summary>
    /// The path to the grouping key the operand is equal to, or null when it is none. A
    /// single key is reached through Key itself; a key of several parts through the member
    /// the builder named it by (decision 113).
    /// </summary>
    private string? KeyPath(QueryOperand operand)
    {
        foreach (var key in Scope.GroupKeys)
        {
            if (OperandStructure.Same(key.Key, operand))
            {
                return key.Member is null ? $"{Scope.Param}.Key" : $"{Scope.Param}.Key.{key.Member}";
            }
        }

        return null;
    }

    /// <summary>
    /// The member a column is written as: its property, or for a foreign key column no
    /// property of the source maps, the property the entity builder declares for it
    /// (<see cref="EFCoreColumnMember"/>).
    /// </summary>
    public string Property(string? alias, string column)
    {
        var map = alias is not null && Scope.Entities.TryGetValue(alias, out var found) ? found : null;
        return EFCoreColumnMember.PathOf(map, column) ?? column;
    }

    /* ---- expressions (decision 107) --------------------------------------------------- */

    /// <summary>
    /// An expression in C#'s spelling (decision 107): the operators as written, <c>+</c> for
    /// a concatenation as for an addition, the string methods and members of System.String
    /// and System.DateTime, <c>??</c> for COALESCE, <c>Math.Abs</c>, <c>DateTime.Now</c>, the
    /// conditional operator for a searched CASE, and a chain of Replace for the escaping of
    /// a pattern value. SUBSTRING's position is translated, not carried: the model counts
    /// from one, C# from zero.
    /// </summary>
    private string Expression(QueryExpression expression)
    {
        // LINQ has no ranking function over a window; the descriptor says so and the template
        // sends the query to native SQL before any step runs (decision 113).
        if (expression.IsWindow)
        {
            report(ConversionRecordKind.Fallback, "LINQ has no ranking function over a window", QueryFeature.WindowFunction);
            return string.Empty;
        }

        if (expression.IsListAggregate)
        {
            return ListAggregate(expression);
        }

        if (expression.IsBinary)
        {
            var symbol = expression.Operator!.Value switch
            {
                ExpressionOperator.Concat or ExpressionOperator.Add => "+",
                ExpressionOperator.Subtract => "-",
                ExpressionOperator.Multiply => "*",
                ExpressionOperator.Divide => "/",
                ExpressionOperator.Modulo => "%",
                _ => throw new ArgumentOutOfRangeException(nameof(expression), expression.Operator, null),
            };

            return $"{Side(expression.Left!, expression, rightSide: false)} {symbol} {Side(expression.Right!, expression, rightSide: true)}";
        }

        if (expression.IsCall)
        {
            var arguments = expression.Arguments!;
            return expression.Function!.Value switch
            {
                QueryFunction.Upper => $"{Receiver(arguments[0])}.ToUpper()",
                QueryFunction.Lower => $"{Receiver(arguments[0])}.ToLower()",
                QueryFunction.Trim => $"{Receiver(arguments[0])}.Trim()",
                QueryFunction.Substring => $"{Receiver(arguments[0])}.Substring({ZeroBased(arguments[1])}, {Operand(arguments[2])})",
                QueryFunction.Length => $"{Receiver(arguments[0])}.Length",
                QueryFunction.Coalesce => $"({string.Join(" ?? ", arguments.Select(Operand))})",
                QueryFunction.Abs => $"Math.Abs({Operand(arguments[0])})",
                QueryFunction.Year => $"{Receiver(arguments[0])}.Year",
                QueryFunction.Month => $"{Receiver(arguments[0])}.Month",
                QueryFunction.Day => $"{Receiver(arguments[0])}.Day",
                QueryFunction.CurrentTimestamp => "DateTime.Now",
                QueryFunction.EscapePattern => EscapePattern(Receiver(arguments[0]), patternEscape ?? "!"),
                QueryFunction.DateAdd => DateAdd(expression),
                QueryFunction.DateDiff => DateDiff(expression),
                QueryFunction.Round => Round(arguments[0], arguments[1]),
                QueryFunction.Sqrt => $"Math.Sqrt({AsDouble(arguments[0])})",
                QueryFunction.Cast => Cast(arguments[0], expression.CastTo!.Value),
                _ => throw new ArgumentOutOfRangeException(nameof(expression), expression.Function, null),
            };
        }

        return Case(expression);
    }

    /* ---- the functions of decision 113 ------------------------------------------------ */

    /// <summary>
    /// DATEADD as the Add method of the moment's type - AddYears through AddSeconds, which
    /// EF Core 10 translates to DATEADD with that unit (verified). A date without a time of
    /// day has no AddHours, AddMinutes or AddSeconds, and the query goes out in native SQL.
    /// </summary>
    private string DateAdd(QueryExpression expression)
    {
        var (count, moment) = (expression.Arguments![0], expression.Arguments[1]);
        if (ScalarOf(moment) == ScalarType.Date && expression.Unit is DateUnit.Hour or DateUnit.Minute or DateUnit.Second)
        {
            report(ConversionRecordKind.Fallback, $"A date without a time of day has no Add{expression.Unit}s in .NET", QueryFeature.Expression);
            return string.Empty;
        }

        return $"{Receiver(moment)}.Add{expression.Unit}s({Operand(count)})";
    }

    /// <summary>
    /// DATEDIFF as EF.Functions.DateDiffYear through DateDiffSecond, which EF Core 10
    /// translates to DATEDIFF with that unit. Each overload takes two values of one type - a
    /// moment, a moment with an offset, a date, each also nullable - so a date against a moment
    /// has no overload C# can choose and the call does not compile (measured against
    /// Microsoft.EntityFrameworkCore.SqlServer 10.0.10); the query goes out in native SQL. A
    /// side whose scalar this visitor cannot know - a parameter, typed by the gate from the
    /// other side - is no mismatch.
    /// </summary>
    private string DateDiff(QueryExpression expression)
    {
        var (start, end) = (expression.Arguments![0], expression.Arguments[1]);
        if (ScalarOf(start) is { } from && ScalarOf(end) is { } to && from != to)
        {
            report(ConversionRecordKind.Fallback, $"EF.Functions.DateDiff{expression.Unit} has no overload over a {from} and a {to}", QueryFeature.Expression);
            return string.Empty;
        }

        return $"EF.Functions.DateDiff{expression.Unit}({Operand(start)}, {Operand(end)})";
    }

    /// <summary>
    /// ROUND as Math.Round(x, places), which EF Core translates to ROUND for the database to
    /// compute. A whole number - or a value whose scalar the gate could not derive - has no
    /// unambiguous Math.Round in C#: an int converts to decimal and to double alike, and the
    /// call does not compile; the query goes out in native SQL.
    /// </summary>
    private string Round(QueryOperand value, QueryOperand places)
    {
        if (ScalarOf(value) is not (ScalarType.Decimal or ScalarType.Double or ScalarType.Float))
        {
            report(ConversionRecordKind.Fallback, $"Math.Round over '{value}' has no overload C# can choose without the value being a decimal or a floating-point number", QueryFeature.Expression);
            return string.Empty;
        }

        return $"Math.Round({Unwrapped(value)}, {Operand(places)})";
    }

    /// <summary>The argument of Math.Sqrt, which takes a double: a decimal or a value of no known scalar is cast, which EF Core translates to the CAST AS float SQL Server makes of it anyway.</summary>
    private string AsDouble(QueryOperand value)
        => ScalarOf(value) is ScalarType.Double or ScalarType.Float or ScalarType.Int or ScalarType.Long or ScalarType.Short or ScalarType.Byte
            ? Operand(value)
            : $"(double)({Operand(value)})";

    /// <summary>
    /// A conversion: into text by ToString(), which EF Core translates to CONVERT - the same
    /// text for a number or a moment -, into a number by a C# cast, nullable where the column
    /// is, because SQL converts a NULL into NULL. A text converted into a number has no C#
    /// cast, and the query goes out in native SQL.
    /// </summary>
    private string Cast(QueryOperand value, ScalarType into)
    {
        if (into == ScalarType.String)
        {
            return $"{Receiver(value)}.ToString()";
        }

        if (ScalarOf(value) is ScalarType.String or ScalarType.Char)
        {
            report(ConversionRecordKind.Fallback, $"The text '{value}' has no C# cast into {into}", QueryFeature.Expression);
            return string.Empty;
        }

        var type = CSharpTypeConvertor.ToString(LangType.Scalar(into));
        var nullable = NullableElementType(value) is not null;
        return $"({type}{(nullable ? "?" : string.Empty)})({Operand(value)})";
    }

    /// <summary>
    /// STRING_AGG as string.Join over the elements of the group, ordered by the steps before
    /// its Select (decision 113). EF Core 10 translates it to STRING_AGG over values it first
    /// turns from NULL into the empty string, and the whole from NULL into the empty string
    /// too - verified -, which is STRING_AGG exactly over a column that holds no NULL, in a
    /// group, which never is empty. Anywhere else - a column that may hold NULL, a value that
    /// is not text, a subquery, where EF Core 10 joins the list on the client - the query goes
    /// out in native SQL.
    /// </summary>
    private string ListAggregate(QueryExpression expression)
    {
        var value = expression.Listed!;

        if (!Scope.Grouped || insideAggregate)
        {
            report(ConversionRecordKind.Fallback, "string.Join over anything but the elements of a group is not translated by EF Core 10, which joins the list on the client", QueryFeature.ListAggregation);
            return string.Empty;
        }

        if (value is not { IsColumn: true, IsAggregate: false }
            || PropertyType(value.Table, value.Property!) is not { ScalarType: ScalarType.String }
            || !HoldsNoNull(value.Table, value.Property!))
        {
            report(ConversionRecordKind.Fallback, $"string.Join over '{value}', which is not a column of text known to hold no NULL, takes a NULL as the empty string in EF Core 10, where STRING_AGG leaves it out", QueryFeature.ListAggregation);
            return string.Empty;
        }

        var wasInsideAggregate = insideAggregate;
        insideAggregate = true;

        var element = Scope.ElementParam;
        var chain = Scope.Param;
        for (var i = 0; i < expression.Ordering!.Count; i++)
        {
            var key = expression.Ordering[i];
            var method = (i == 0, key.Ascending) switch
            {
                (true, true) => "OrderBy",
                (true, false) => "OrderByDescending",
                (false, true) => "ThenBy",
                (false, false) => "ThenByDescending",
            };

            chain += $".{method}({element} => {Operand(key.Operand)})";
        }

        chain += $".Select({element} => {Operand(value)})";
        insideAggregate = wasInsideAggregate;

        return $"string.Join({StringLiteral(expression.Separator!)}, {chain})";
    }

    /// <summary>
    /// The scalar of an operand as this visitor can know it: a constant's own, a column's from
    /// the mapping, an expression's from the typed view, a COUNT's a count; null for the rest.
    /// </summary>
    private ScalarType? ScalarOf(QueryOperand operand)
    {
        if (string.Equals(operand.Function, "COUNT", StringComparison.OrdinalIgnoreCase))
        {
            return ScalarType.Long;
        }

        if (operand.IsConstant)
        {
            return operand.Constant!.Type;
        }

        if (operand.IsExpression)
        {
            return typing.ScalarOf(operand.Expression!);
        }

        return operand.IsColumn && operand.Property != "*" && PropertyType(operand.Table, operand.Property!) is { Category: LangTypeCategory.Scalar } type
            ? type.ScalarType
            : null;
    }

    /// <summary>
    /// A searched CASE as nested conditional operators, parenthesized as a whole because the
    /// conditional binds weaker than any comparison around it. Without an ELSE the value is
    /// the null of the branches' scalar, which the gate derived (decision 107); a scalar
    /// nobody derived leaves C# no type to spell the null in, and the CASE is refused rather
    /// than emitted uncompilable.
    /// </summary>
    private string Case(QueryExpression expression)
    {
        string otherwise;
        if (expression.Else is not null)
        {
            otherwise = Operand(expression.Else);
        }
        else if (typing.ScalarOf(expression) is { } scalar)
        {
            var type = CSharpTypeConvertor.ToString(LangType.Scalar(scalar));
            otherwise = scalar is ScalarType.String or ScalarType.Object or ScalarType.ByteArray
                ? $"({type})null"
                : $"({type}?)null";
        }
        else
        {
            report(
                ConversionRecordKind.Failure,
                $"The CASE '{expression}' has no ELSE and the scalar of its branches does not follow from the mapping, so C# has no type to spell its null in; no artifact was generated.",
                QueryFeature.Expression);
            otherwise = "null";
        }

        var text = otherwise;
        foreach (var branch in expression.Branches!.Reverse())
        {
            text = $"{branch.When.Accept(this)} ? {Operand(branch.Then)} : {text}";
        }

        return $"({text})";
    }

    /// <summary>
    /// One side of a binary expression, parenthesized where C# would regroup it otherwise -
    /// the same precedence the SQL visitor follows.
    /// </summary>
    private string Side(QueryOperand side, QueryExpression parent, bool rightSide)
    {
        var text = Operand(side);
        return ExpressionSpelling.NeedsParentheses(side, parent, rightSide) ? $"({text})" : text;
    }

    /// <summary>
    /// The receiver of a member access: an operation is parenthesized, everything else binds
    /// tighter than the dot already. A column of a nullable value type is reached through its
    /// Value - DateTime? has no Year and no AddDays -, which EF Core translates as the column
    /// itself (decision 113; the shape a MyBatis source declares a moment in).
    /// </summary>
    private string Receiver(QueryOperand operand)
    {
        if (operand is { IsExpression: true, IsAggregate: false } && operand.Expression!.IsBinary)
        {
            return $"({Operand(operand)})";
        }

        return Unwrapped(operand);
    }

    /// <summary>The operand, through its Value where it is a column of a nullable value type.</summary>
    private string Unwrapped(QueryOperand operand)
        => NullableElementType(operand) is null ? Operand(operand) : $"{Operand(operand)}.Value";

    /// <summary>
    /// The start of a SUBSTRING as C# counts it (decision 107): the model carries the
    /// position from one, so a number goes out one less, a position the LINQ reader wrote
    /// as <c>x + 1</c> goes out as <c>x</c>, and anything else as <c>x - 1</c>.
    /// </summary>
    private string ZeroBased(QueryOperand position)
    {
        if (position is { IsConstant: true, Function: null }
            && long.TryParse(position.Constant!.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return (number - 1).ToString(CultureInfo.InvariantCulture);
        }

        if (position is { IsExpression: true, Function: null }
            && position.Expression!.Operator == ExpressionOperator.Add
            && position.Expression.Right is { IsConstant: true, Function: null } one
            && one.Constant!.Text == "1")
        {
            return Operand(position.Expression.Left!);
        }

        return $"{Receiver(position)} - 1";
    }

    /// <summary>
    /// The value with every wildcard of SQL Server made literal (decision 107), as the chain
    /// of Replace calls EF Core translates to REPLACE: the escape character first, then
    /// <c>%</c>, <c>_</c> and <c>[</c>.
    /// </summary>
    private static string EscapePattern(string receiver, string escape)
    {
        var text = $"{receiver}.Replace({StringLiteral(escape)}, {StringLiteral(escape + escape)})";
        foreach (var wildcard in new[] { "%", "_", "[" })
        {
            text += $".Replace({StringLiteral(wildcard)}, {StringLiteral(escape + wildcard)})";
        }

        return text;
    }

    /// <summary>
    /// Writes a constant the way C# wants it (decision 024). The model carries the value
    /// undecorated, so the quoting and the numeric suffix are added here from the scalar
    /// type rather than carried over from whatever the source language wrote.
    /// </summary>
    private string Literal(QueryConstant constant) => constant.Type switch
    {
        ScalarType.String => StringLiteral(constant.Text),
        ScalarType.Char => $"'{constant.Text}'",
        ScalarType.Bool => constant.Text.ToLowerInvariant(),
        ScalarType.Long => constant.Text + "L",
        ScalarType.Decimal => constant.Text + "m",
        ScalarType.Double => constant.Text + "d",
        ScalarType.Float => constant.Text + "f",
        ScalarType.DateTime => $"DateTime.Parse(\"{constant.Text}\")",
        ScalarType.Guid => $"Guid.Parse(\"{constant.Text}\")",
        // The same shape for the temporal scalars of decision 071: a parsed constant EF
        // Core evaluates on the client and sends as a parameter, like DateTime above.
        ScalarType.Date => $"DateOnly.Parse(\"{constant.Text}\")",
        ScalarType.TimeOfDay => $"TimeOnly.Parse(\"{constant.Text}\")",
        ScalarType.DateTimeOffset => $"DateTimeOffset.Parse(\"{constant.Text}\")",
        ScalarType.Duration => $"TimeSpan.Parse(\"{constant.Text}\")",
        _ => constant.Text,
    };
}
